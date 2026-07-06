// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using Funplay.Editor.Tools.Helpers;
using Newtonsoft.Json;
using Unity.Profiling.Memory;
using UnityEditor;
using UnityEngine;

namespace Funplay.Editor.Tools.Builtins
{
    /// <summary>
    /// REAL Unity memory snapshots (.snap) -- the file format the Memory Profiler
    /// package opens for object-level reference-chain analysis. Complements the
    /// lightweight aggregate-counter snapshots in <see cref="ProfilerFunctions"/>
    /// (memory_take_snapshot / memory_compare_snapshots): use those to detect THAT
    /// memory grew, and these to find out WHAT is holding it and WHO references it.
    /// Capture uses <c>Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot</c>
    /// (core engine API, Unity 2022.2+); opening in the Memory Profiler window uses
    /// reflection into the optional com.unity.memoryprofiler package.
    /// </summary>
    [ToolProvider("MemorySnapshot")]
    internal static class MemorySnapshotFunctions
    {
        private const string DefaultStorageDirName = "MemoryCaptures";
        private const string PackageWindowTypeName = "Unity.MemoryProfiler.Editor.MemoryProfilerWindow";
        private const string PackageSettingsTypeName = "Unity.MemoryProfiler.Editor.MemoryProfilerSettings";
        private const string PackageEditorAssemblyName = "Unity.MemoryProfiler.Editor";

        [Description("Capture a REAL Unity memory snapshot (.snap) -- the full-detail file the Memory Profiler package " +
                     "opens for object-level reference-chain analysis ('who is holding this texture alive'). " +
                     "Complements memory_take_snapshot, which only records lightweight aggregate counters: use that to " +
                     "detect THAT memory grew, and this to find out WHAT is holding it. The file is written into the " +
                     "Memory Profiler package's snapshot folder (default 'MemoryCaptures/' at the project root) so it " +
                     "shows up in the window's snapshot list. WARNING: in-editor captures include editor-owned memory " +
                     "and can be hundreds of MB to several GB on large projects; the capture stalls the editor for a " +
                     "few seconds while it runs.")]
        [ReadOnlyTool]
        public static async Task<string> MemoryTakeFullSnapshot(
            [ToolParam("Base file name (timestamp and .snap extension are appended). Default 'mcp'.", Required = false)] string name = null,
            [ToolParam("Comma-separated capture flags: ManagedObjects, NativeObjects, NativeAllocations, " +
                       "NativeAllocationSites, NativeStackTraces. Default 'ManagedObjects,NativeObjects' " +
                       "(what reference-chain analysis needs; the NativeAllocation* flags add a lot of size).", Required = false)] string capture_flags = null,
            [ToolParam("Maximum seconds to wait for the capture to finish. Default 180.", Required = false)] int timeout_seconds = 180,
            [ToolParam("Open the finished snapshot in the Memory Profiler window (requires the com.unity.memoryprofiler package).", Required = false)] bool open_in_profiler = false)
        {
            if (!TryParseCaptureFlags(capture_flags, out var flags, out var flagsError))
                return ToolResultFormatter.Error("INVALID_CAPTURE_FLAGS", flagsError);

            timeout_seconds = Mathf.Clamp(timeout_seconds, 10, 900);

            string path;
            try
            {
                var dir = ResolveStorageDirectory();
                Directory.CreateDirectory(dir);
                var baseName = SanitizeFileNameFragment(string.IsNullOrWhiteSpace(name) ? "mcp" : name);
                path = Path.Combine(dir, baseName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".snap");
            }
            catch (Exception ex)
            {
                return ToolResultFormatter.Error("SNAPSHOT_PATH_FAILED", new { message = ex.Message });
            }

            var completion = new TaskCompletionSource<bool>();
            var startedAt = EditorApplication.timeSinceStartup;

            try
            {
                MemoryProfiler.TakeSnapshot(path, (finalPath, success) => completion.TrySetResult(success), flags);
            }
            catch (Exception ex)
            {
                return ToolResultFormatter.Error("SNAPSHOT_CAPTURE_FAILED", new { message = ex.Message });
            }

            // The snapshot is written at the end of a player-loop frame; a paused Edit Mode
            // editor may never tick one on its own, so keep pumping until the callback fires.
            while (!completion.Task.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup - startedAt > timeout_seconds)
                {
                    return ToolResultFormatter.Error("SNAPSHOT_TIMEOUT", new
                    {
                        timeout_seconds,
                        path,
                        hint = "The capture callback did not fire in time. Very large heaps can take minutes; retry with a bigger timeout_seconds."
                    });
                }

                EditorApplication.QueuePlayerLoopUpdate();
                await Task.Delay(250);
            }

            if (!completion.Task.Result)
                return ToolResultFormatter.Error("SNAPSHOT_CAPTURE_FAILED", new { path, hint = "Unity reported the capture as failed." });

            long sizeBytes = 0;
            try { sizeBytes = new FileInfo(path).Length; } catch { }

            string openError = null;
            var opened = false;
            if (open_in_profiler)
                opened = TryOpenSnapshotInProfiler(path, out openError);

            return JsonConvert.SerializeObject(Response.Success(
                "Memory snapshot captured.",
                new
                {
                    path,
                    size_bytes = sizeBytes,
                    size = FormatBytes(sizeBytes),
                    flags = flags.ToString(),
                    duration_seconds = Math.Round(EditorApplication.timeSinceStartup - startedAt, 1),
                    opened_in_profiler = opened,
                    open_error = openError,
                    hint = "Open it with memory_open_snapshot_in_profiler, then use capture_editor_window('Memory Profiler') to inspect the analysis visually."
                }));
        }

        [Description("List the REAL .snap memory snapshots in the Memory Profiler package's snapshot folder " +
                     "(the ones memory_take_full_snapshot writes and the Memory Profiler window lists). " +
                     "For the lightweight aggregate JSON snapshots, use memory_list_snapshots instead.")]
        [ReadOnlyTool]
        public static string MemoryListFullSnapshots()
        {
            string dir;
            try
            {
                dir = ResolveStorageDirectory();
            }
            catch (Exception ex)
            {
                return ToolResultFormatter.Error("SNAPSHOT_DIR_FAILED", new { message = ex.Message });
            }

            if (!Directory.Exists(dir))
                return JsonConvert.SerializeObject(Response.Success("No snapshot folder yet.", new { directory = dir, snapshots = Array.Empty<object>() }));

            var snapshots = Directory.GetFiles(dir, "*.snap")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => new
                {
                    file = f.Name,
                    path = f.FullName,
                    size_bytes = f.Length,
                    size = FormatBytes(f.Length),
                    modified = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                })
                .ToArray();

            return JsonConvert.SerializeObject(Response.Success(
                $"{snapshots.Length} snapshot(s) in {dir}.",
                new { directory = dir, snapshots }));
        }

        [Description("Open a REAL .snap memory snapshot in the Memory Profiler package window for object-level " +
                     "reference-chain analysis. Accepts an absolute path or a file name from the snapshot folder " +
                     "(see memory_list_full_snapshots). Requires the com.unity.memoryprofiler package. " +
                     "Combine with capture_editor_window('Memory Profiler') to inspect the loaded analysis visually.")]
        [ReadOnlyTool]
        public static string MemoryOpenSnapshotInProfiler(
            [ToolParam("Absolute .snap path, or a file name inside the snapshot folder (with or without the .snap extension).")] string snapshot)
        {
            if (string.IsNullOrWhiteSpace(snapshot))
                return ToolResultFormatter.Error("INVALID_SNAPSHOT", new { hint = "Provide a .snap path or file name." });

            string path;
            try
            {
                path = ResolveSnapshotPath(snapshot.Trim());
            }
            catch (Exception ex)
            {
                return ToolResultFormatter.Error("SNAPSHOT_DIR_FAILED", new { message = ex.Message });
            }

            if (path == null)
                return ToolResultFormatter.Error("SNAPSHOT_NOT_FOUND", new { requested = snapshot, hint = "Use memory_list_full_snapshots to see what exists." });

            if (!TryOpenSnapshotInProfiler(path, out var error))
                return ToolResultFormatter.Error("OPEN_IN_PROFILER_FAILED", new { path, message = error });

            return JsonConvert.SerializeObject(Response.Success(
                "Snapshot loaded in the Memory Profiler window.",
                new { path, hint = "Use capture_editor_window('Memory Profiler') to inspect the analysis visually." }));
        }

        // --- Helpers ---

        private static bool TryParseCaptureFlags(string raw, out CaptureFlags flags, out object error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                flags = CaptureFlags.ManagedObjects | CaptureFlags.NativeObjects;
                return true;
            }

            flags = 0;
            foreach (var token in raw.Split(','))
            {
                var trimmed = token.Trim();
                if (trimmed.Length == 0)
                    continue;
                if (!Enum.TryParse<CaptureFlags>(trimmed, ignoreCase: true, out var parsed))
                {
                    error = new { provided = trimmed, accepted = Enum.GetNames(typeof(CaptureFlags)) };
                    return false;
                }
                flags |= parsed;
            }

            if (flags == 0)
            {
                error = new { provided = raw, hint = "At least one capture flag is required." };
                return false;
            }
            return true;
        }

        /// <summary>
        /// The Memory Profiler package's configured snapshot folder when the package is
        /// installed (so captures show up in its window), else MemoryCaptures/ at the
        /// project root -- which is also that setting's default.
        /// </summary>
        private static string ResolveStorageDirectory()
        {
            try
            {
                var settingsType = ResolvePackageType(PackageSettingsTypeName);
                var pathProperty = settingsType?.GetProperty("AbsoluteMemorySnapshotStoragePath",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (pathProperty?.GetValue(null) is string configured && !string.IsNullOrEmpty(configured))
                    return configured;
            }
            catch
            {
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
            return Path.Combine(projectRoot, DefaultStorageDirName);
        }

        private static string ResolveSnapshotPath(string requested)
        {
            if (Path.IsPathRooted(requested))
                return File.Exists(requested) ? requested : null;

            var dir = ResolveStorageDirectory();
            var candidate = Path.Combine(dir, requested);
            if (File.Exists(candidate))
                return candidate;
            if (!requested.EndsWith(".snap", StringComparison.OrdinalIgnoreCase) && File.Exists(candidate + ".snap"))
                return candidate + ".snap";
            return null;
        }

        private static bool TryOpenSnapshotInProfiler(string path, out string error)
        {
            error = null;
            try
            {
                var windowType = ResolvePackageType(PackageWindowTypeName);
                if (windowType == null)
                {
                    error = "The com.unity.memoryprofiler package is not installed. Install it via the Package Manager to analyze snapshots.";
                    return false;
                }

                var window = EditorWindow.GetWindow(windowType);
                window.Show();
                window.Focus();

                var serviceField = windowType.GetField("m_SnapshotDataService", BindingFlags.NonPublic | BindingFlags.Instance);
                var service = serviceField?.GetValue(window);
                var loadMethod = service?.GetType().GetMethod("Load",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(string) }, null);
                if (loadMethod == null)
                {
                    error = "The Memory Profiler window opened, but its snapshot-loading internals were not found in this package version. Load the snapshot manually from the window's list.";
                    return false;
                }

                // Load() looks the file up in the service's session collection, which is
                // only populated by a folder scan -- a freshly captured file (or a freshly
                // opened window) hasn't been scanned yet and Load() throws a dictionary
                // key miss. Sync the folder first.
                var syncMethod = service.GetType().GetMethod("SyncSnapshotsFolder",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, Type.EmptyTypes, null);
                syncMethod?.Invoke(service, null);

                loadMethod.Invoke(service, new object[] { path });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        private static Type ResolvePackageType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.GetName().Name.Equals(PackageEditorAssemblyName, StringComparison.Ordinal))
                    continue;
                return assembly.GetType(fullName);
            }
            return null;
        }

        private static string SanitizeFileNameFragment(string value)
        {
            var chars = value.Trim().Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray();
            var sanitized = new string(chars).Trim('-');
            return string.IsNullOrEmpty(sanitized) ? "mcp" : sanitized;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):F2} GB";
            if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):F2} MB";
            if (bytes >= 1L << 10) return $"{bytes / (double)(1L << 10):F2} KB";
            return $"{bytes} B";
        }
    }
}
