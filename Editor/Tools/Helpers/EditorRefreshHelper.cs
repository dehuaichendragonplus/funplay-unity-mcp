// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Funplay.Editor.Tools.Helpers
{
    internal static class EditorRefreshHelper
    {
        private static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan DefaultStartDetectionTimeout = TimeSpan.FromSeconds(2);

        public static async Task<EditorRefreshResult> RefreshAndRequestCompilationAsync(
            bool forceUpdate = true,
            bool verifyScriptChanges = true,
            TimeSpan? startDetectionTimeout = null)
        {
            var result = new EditorRefreshResult
            {
                KnownHotReloadDetected = IsKnownHotReloadLoaded()
            };

            if (verifyScriptChanges)
                result.ScriptStateBefore = CaptureScriptChangeState(scanForUnknownProjectScripts: false);

            try
            {
                AssetDatabase.Refresh(forceUpdate ? ImportAssetOptions.ForceUpdate : ImportAssetOptions.Default);
                result.PrimaryRefreshInvoked = true;
            }
            catch (Exception ex)
            {
                result.PrimaryRefreshError = ex.Message;
            }

            if (await WaitForEditorBusyAsync(startDetectionTimeout ?? DefaultStartDetectionTimeout))
            {
                result.CompilationOrImportStarted = true;
                return result;
            }

            if (verifyScriptChanges)
                result.ScriptStateAfterPrimary = CaptureScriptChangeState(scanForUnknownProjectScripts: true);

            if (ShouldTryFallback(result))
            {
                try
                {
                    result.MenuRefreshInvoked = EditorApplication.ExecuteMenuItem("Assets/Refresh");
                }
                catch (Exception ex)
                {
                    result.MenuRefreshError = ex.Message;
                }

                if (await WaitForEditorBusyAsync(startDetectionTimeout ?? DefaultStartDetectionTimeout))
                {
                    result.CompilationOrImportStarted = true;
                    return result;
                }

                result.ScriptStateAfterMenuRefresh = CaptureScriptChangeState(scanForUnknownProjectScripts: true);
            }

            if (ShouldTryFallback(result))
            {
                try
                {
                    CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
                    result.ScriptCompilationRequested = true;
                }
                catch (Exception ex)
                {
                    result.ScriptCompilationRequestError = ex.Message;
                }

                if (await WaitForEditorBusyAsync(startDetectionTimeout ?? DefaultStartDetectionTimeout))
                {
                    result.CompilationOrImportStarted = true;
                    return result;
                }

                result.ScriptStateAfterCompilationRequest = CaptureScriptChangeState(scanForUnknownProjectScripts: true);
            }

            if (ShouldTryFallback(result))
            {
                try
                {
                    EditorUtility.RequestScriptReload();
                    result.ScriptReloadRequested = true;
                }
                catch (Exception ex)
                {
                    result.ScriptReloadRequestError = ex.Message;
                }

                if (await WaitForEditorBusyAsync(TimeSpan.FromMilliseconds(500)))
                    result.CompilationOrImportStarted = true;

                result.ScriptStateAfterScriptReload = CaptureScriptChangeState(scanForUnknownProjectScripts: true);
            }

            return result;
        }

        public static async Task RefreshAndWaitForReadyAsync(TimeSpan timeout)
        {
            var refreshResult = await RefreshAndRequestCompilationAsync(
                forceUpdate: false,
                verifyScriptChanges: true);

            if (refreshResult.ScriptChangesStillPending)
                throw new EditorRefreshDidNotStartCompilationException(refreshResult);

            if (EditorApplication.isCompiling || EditorApplication.isUpdating || refreshResult.CompilationOrImportStarted)
                await WaitForEditorReadyAsync(timeout);
        }

        public static Task WaitForEditorReadyAsync(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>();
            var start = DateTime.UtcNow;

            void Tick()
            {
                if (tcs.Task.IsCompleted)
                {
                    EditorApplication.update -= Tick;
                    return;
                }

                if ((DateTime.UtcNow - start) > timeout)
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetException(new TimeoutException("Editor still busy after timeout"));
                    return;
                }

                if (!EditorApplication.isCompiling && !EditorApplication.isUpdating)
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetResult(true);
                }
            }

            EditorApplication.update += Tick;
            EditorApplication.QueuePlayerLoopUpdate();
            return tcs.Task;
        }

        internal static ScriptChangeState AnalyzeScriptChangeState(
            IEnumerable<ScriptCompilationArtifact> artifacts,
            IEnumerable<string> projectScriptFiles,
            TimeSpan timestampTolerance)
        {
            var state = new ScriptChangeState();
            var knownSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var newestOutputTime = DateTime.MinValue;

            foreach (var artifact in artifacts ?? Enumerable.Empty<ScriptCompilationArtifact>())
            {
                if (artifact == null)
                    continue;

                var outputPath = NormalizePath(artifact.OutputPath);
                var outputExists = !string.IsNullOrEmpty(outputPath) && File.Exists(outputPath);
                var outputTime = outputExists ? File.GetLastWriteTimeUtc(outputPath) : DateTime.MinValue;
                if (outputTime > newestOutputTime)
                    newestOutputTime = outputTime;

                foreach (var sourcePath in artifact.SourceFiles ?? Array.Empty<string>())
                {
                    var normalizedSource = NormalizePath(sourcePath);
                    if (string.IsNullOrEmpty(normalizedSource))
                        continue;

                    knownSources.Add(normalizedSource);
                    if (!File.Exists(normalizedSource))
                        continue;

                    if (!outputExists || File.GetLastWriteTimeUtc(normalizedSource) - outputTime > timestampTolerance)
                        state.AddOutOfDateSource(normalizedSource);
                }
            }

            foreach (var scriptPath in projectScriptFiles ?? Enumerable.Empty<string>())
            {
                var normalizedScript = NormalizePath(scriptPath);
                if (string.IsNullOrEmpty(normalizedScript) || knownSources.Contains(normalizedScript) || !File.Exists(normalizedScript))
                    continue;

                if (newestOutputTime == DateTime.MinValue ||
                    File.GetLastWriteTimeUtc(normalizedScript) - newestOutputTime > timestampTolerance)
                {
                    state.AddUnknownProjectScript(normalizedScript);
                }
            }

            return state;
        }

        private static bool ShouldTryFallback(EditorRefreshResult result)
        {
            if (result == null || result.CompilationOrImportStarted)
                return false;

            return result.LatestScriptState.HasPendingScriptChanges;
        }

        private static async Task<bool> WaitForEditorBusyAsync(TimeSpan timeout)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return true;

            var tcs = new TaskCompletionSource<bool>();
            var start = DateTime.UtcNow;

            void Tick()
            {
                if (tcs.Task.IsCompleted)
                {
                    EditorApplication.update -= Tick;
                    return;
                }

                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetResult(true);
                    return;
                }

                if ((DateTime.UtcNow - start) > timeout)
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetResult(false);
                }
            }

            EditorApplication.update += Tick;
            EditorApplication.QueuePlayerLoopUpdate();
            return await tcs.Task;
        }

        private static ScriptChangeState CaptureScriptChangeState(bool scanForUnknownProjectScripts)
        {
            try
            {
                var artifacts = CompilationPipeline
                    .GetAssemblies(AssembliesType.Editor)
                    .Select(assembly => new ScriptCompilationArtifact(
                        assembly.outputPath,
                        assembly.sourceFiles ?? Array.Empty<string>()))
                    .ToArray();

                var projectScripts = scanForUnknownProjectScripts
                    ? EnumerateProjectScriptFiles()
                    : Enumerable.Empty<string>();

                return AnalyzeScriptChangeState(artifacts, projectScripts, TimestampTolerance);
            }
            catch (Exception ex)
            {
                var state = new ScriptChangeState { AnalysisError = ex.Message };
                return state;
            }
        }

        private static IEnumerable<string> EnumerateProjectScriptFiles()
        {
            var roots = new[]
            {
                Application.dataPath,
                Path.Combine(GetProjectRoot(), "Packages")
            };

            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                    continue;

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                    yield return file;
            }
        }

        private static string GetProjectRoot()
        {
            var dataPath = Application.dataPath;
            if (string.IsNullOrEmpty(dataPath))
                return Directory.GetCurrentDirectory();

            return Directory.GetParent(dataPath)?.FullName ?? Directory.GetCurrentDirectory();
        }

        private static bool IsKnownHotReloadLoaded()
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var name = assembly.GetName().Name;
                    if (name != null &&
                        name.StartsWith("SingularityGroup.HotReload", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (assembly.GetType("SingularityGroup.HotReload.Editor.CompileMethodDetourer", false) != null)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFullPath(path).Replace('\\', '/');
            }
            catch
            {
                return path.Replace('\\', '/');
            }
        }
    }

    internal sealed class EditorRefreshDidNotStartCompilationException : Exception
    {
        public EditorRefreshDidNotStartCompilationException(EditorRefreshResult refreshResult)
            : base(refreshResult?.BuildPendingScriptMessage() ?? "Editor refresh did not start script compilation.")
        {
            RefreshResult = refreshResult;
        }

        public EditorRefreshResult RefreshResult { get; }
    }

    internal sealed class EditorRefreshResult
    {
        public bool PrimaryRefreshInvoked;
        public bool MenuRefreshInvoked;
        public bool ScriptCompilationRequested;
        public bool ScriptReloadRequested;
        public bool CompilationOrImportStarted;
        public bool KnownHotReloadDetected;
        public string PrimaryRefreshError;
        public string MenuRefreshError;
        public string ScriptCompilationRequestError;
        public string ScriptReloadRequestError;
        public ScriptChangeState ScriptStateBefore = ScriptChangeState.Empty;
        public ScriptChangeState ScriptStateAfterPrimary = ScriptChangeState.Empty;
        public ScriptChangeState ScriptStateAfterMenuRefresh = ScriptChangeState.Empty;
        public ScriptChangeState ScriptStateAfterCompilationRequest = ScriptChangeState.Empty;
        public ScriptChangeState ScriptStateAfterScriptReload = ScriptChangeState.Empty;

        public ScriptChangeState LatestScriptState
        {
            get
            {
                if (!ScriptStateAfterScriptReload.IsEmpty) return ScriptStateAfterScriptReload;
                if (!ScriptStateAfterCompilationRequest.IsEmpty) return ScriptStateAfterCompilationRequest;
                if (!ScriptStateAfterMenuRefresh.IsEmpty) return ScriptStateAfterMenuRefresh;
                if (!ScriptStateAfterPrimary.IsEmpty) return ScriptStateAfterPrimary;
                return ScriptStateBefore ?? ScriptChangeState.Empty;
            }
        }

        public bool ScriptChangesStillPending => !CompilationOrImportStarted && LatestScriptState.HasPendingScriptChanges;

        public string BuildStrategySummary()
        {
            var steps = new List<string>();
            if (PrimaryRefreshInvoked) steps.Add("AssetDatabase.Refresh");
            if (MenuRefreshInvoked) steps.Add("Assets/Refresh menu");
            if (ScriptCompilationRequested) steps.Add("RequestScriptCompilation(CleanBuildCache)");
            if (ScriptReloadRequested) steps.Add("RequestScriptReload");
            if (steps.Count == 0) steps.Add("none");
            return string.Join(" -> ", steps);
        }

        public string BuildPendingScriptMessage()
        {
            return "Unity did not start compiling after refreshing external changes. " +
                   "Some script files still look newer than the compiled editor assemblies. " +
                   "A hot-reload or auto-refresh interception plugin may be preventing normal Unity compilation.";
        }

        public object ToResponseData()
        {
            return new
            {
                strategy = BuildStrategySummary(),
                known_hot_reload_detected = KnownHotReloadDetected,
                compilation_or_import_started = CompilationOrImportStarted,
                script_changes_still_pending = ScriptChangesStillPending,
                latest_script_state = LatestScriptState.ToResponseData(),
                errors = new
                {
                    primary_refresh = PrimaryRefreshError,
                    menu_refresh = MenuRefreshError,
                    request_script_compilation = ScriptCompilationRequestError,
                    request_script_reload = ScriptReloadRequestError
                }
            };
        }
    }

    internal sealed class ScriptChangeState
    {
        private const int MaxExamples = 5;
        private readonly List<string> _outOfDateSourceExamples = new List<string>();
        private readonly List<string> _unknownProjectScriptExamples = new List<string>();

        public static readonly ScriptChangeState Empty = new ScriptChangeState { IsEmpty = true };

        public bool IsEmpty { get; private set; }
        public string AnalysisError;
        public int OutOfDateSourceCount { get; private set; }
        public int UnknownProjectScriptCount { get; private set; }
        public bool HasPendingScriptChanges =>
            OutOfDateSourceCount > 0 || UnknownProjectScriptCount > 0 || !string.IsNullOrEmpty(AnalysisError);

        public void AddOutOfDateSource(string path)
        {
            IsEmpty = false;
            OutOfDateSourceCount++;
            AddExample(_outOfDateSourceExamples, path);
        }

        public void AddUnknownProjectScript(string path)
        {
            IsEmpty = false;
            UnknownProjectScriptCount++;
            AddExample(_unknownProjectScriptExamples, path);
        }

        public object ToResponseData()
        {
            return new
            {
                out_of_date_sources = OutOfDateSourceCount,
                unknown_project_scripts = UnknownProjectScriptCount,
                out_of_date_examples = _outOfDateSourceExamples.ToArray(),
                unknown_script_examples = _unknownProjectScriptExamples.ToArray(),
                analysis_error = AnalysisError
            };
        }

        private static void AddExample(List<string> list, string path)
        {
            if (list.Count < MaxExamples)
                list.Add(path);
        }
    }

    internal sealed class ScriptCompilationArtifact
    {
        public ScriptCompilationArtifact(string outputPath, IEnumerable<string> sourceFiles)
        {
            OutputPath = outputPath;
            SourceFiles = sourceFiles?.ToArray() ?? Array.Empty<string>();
        }

        public string OutputPath { get; }
        public string[] SourceFiles { get; }
    }
}
