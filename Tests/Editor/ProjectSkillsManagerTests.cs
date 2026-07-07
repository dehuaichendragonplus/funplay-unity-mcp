// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.IO;
using System.Linq;
using Funplay.Editor.MCP.Server;
using NUnit.Framework;

namespace Funplay.Editor.Tests
{
    public sealed class ProjectSkillsManagerTests
    {
        [Test]
        public void ApplyConfiguration_WritesSkillVersionMarkers()
        {
            var projectRoot = CreateTempProjectPath();

            try
            {
                ProjectSkillsManager.ApplyConfiguration(projectRoot, new[] { "codex" }, Array.Empty<string>());

                var manifest = ProjectSkillsManager.LoadManifest(projectRoot);
                var status = ProjectSkillsManager.GetUpgradeStatus(projectRoot, manifest, "codex");
                var agentsPath = ProjectSkillsManager.GetCodexAgentsPath(projectRoot);
                var skillPath = GetCodexWorkflowSkillPath(projectRoot);

                Assert.IsFalse(status.HasUpdates);
                Assert.IsTrue(File.Exists(agentsPath));
                Assert.IsTrue(File.Exists(skillPath));
                StringAssert.Contains("unity-mcp-workflow@1.0.0", File.ReadAllText(agentsPath));
                StringAssert.Contains("version: 1.0.0", File.ReadAllText(skillPath));
                StringAssert.Contains("<!-- Funplay Unity MCP skill version: unity-mcp-workflow@1.0.0 -->", File.ReadAllText(skillPath));

                var manifestJson = File.ReadAllText(ProjectSkillsManager.GetManifestPath(projectRoot));
                StringAssert.Contains("\"skillVersions\"", manifestJson);
                StringAssert.Contains("\"id\": \"unity-mcp-workflow\"", manifestJson);
                StringAssert.Contains("\"version\": \"1.0.0\"", manifestJson);
            }
            finally
            {
                DeleteTempProjectPath(projectRoot);
            }
        }

        [Test]
        public void GetUpgradeStatus_DetectsUnversionedManagedSkillFile()
        {
            var projectRoot = CreateTempProjectPath();

            try
            {
                ProjectSkillsManager.ApplyConfiguration(projectRoot, new[] { "codex" }, Array.Empty<string>());
                var skillPath = GetCodexWorkflowSkillPath(projectRoot);
                RemoveLinesContaining(skillPath, "Funplay Unity MCP skill version:");
                RemoveLinesContaining(skillPath, "version: 1.0.0");

                var manifest = ProjectSkillsManager.LoadManifest(projectRoot);
                var status = ProjectSkillsManager.GetUpgradeStatus(projectRoot, manifest, "codex");
                var skillStatus = status.Files.First(file => file.Path == skillPath);

                Assert.IsTrue(status.HasUpdates);
                Assert.IsTrue(skillStatus.RequiresUpgrade);
                Assert.AreEqual("unknown", skillStatus.InstalledVersion);
                Assert.AreEqual("1.0.0", skillStatus.ExpectedVersion);
            }
            finally
            {
                DeleteTempProjectPath(projectRoot);
            }
        }

        [Test]
        public void GetUpgradeStatus_DetectsMissingGeneratedSkillFile()
        {
            var projectRoot = CreateTempProjectPath();

            try
            {
                ProjectSkillsManager.ApplyConfiguration(projectRoot, new[] { "codex" }, Array.Empty<string>());
                var skillPath = GetCodexWorkflowSkillPath(projectRoot);
                File.Delete(skillPath);

                var manifest = ProjectSkillsManager.LoadManifest(projectRoot);
                var status = ProjectSkillsManager.GetUpgradeStatus(projectRoot, manifest, "codex");
                var skillStatus = status.Files.First(file => file.Path == skillPath);

                Assert.IsTrue(status.HasUpdates);
                Assert.IsTrue(skillStatus.Missing);
                Assert.AreEqual("missing", skillStatus.InstalledVersion);
                Assert.AreEqual("1.0.0", skillStatus.ExpectedVersion);
            }
            finally
            {
                DeleteTempProjectPath(projectRoot);
            }
        }

        [Test]
        public void GetPlatformConflictPaths_DetectsUnmanagedCursorRule()
        {
            var projectRoot = CreateTempProjectPath();

            try
            {
                var rulesRoot = ProjectSkillsManager.GetCursorRulesPath(projectRoot);
                Directory.CreateDirectory(rulesRoot);
                var path = Path.Combine(rulesRoot, "funplay-unity-mcp-workflow.mdc");
                File.WriteAllText(path, "# User-owned Cursor rule");

                var conflicts = ProjectSkillsManager.GetPlatformConflictPaths(projectRoot, new[] { "cursor" });

                CollectionAssert.Contains(conflicts, path);
            }
            finally
            {
                DeleteTempProjectPath(projectRoot);
            }
        }

        private static string GetCodexWorkflowSkillPath(string projectRoot)
        {
            return Path.Combine(
                ProjectSkillsManager.GetCodexSkillsRoot(projectRoot),
                "funplay-unity-mcp-workflow",
                "SKILL.md");
        }

        private static void RemoveLinesContaining(string path, string text)
        {
            var lines = File.ReadAllLines(path)
                .Where(line => !line.Contains(text))
                .ToArray();
            File.WriteAllLines(path, lines);
        }

        private static string CreateTempProjectPath()
        {
            var path = Path.Combine(Path.GetTempPath(), "FunplayProjectSkillsTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteTempProjectPath(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }
}
