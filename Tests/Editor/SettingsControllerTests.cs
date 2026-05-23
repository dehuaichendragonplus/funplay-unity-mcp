// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.IO;
using Funplay.Editor.Services;
using Funplay.Editor.Settings;
using NUnit.Framework;

namespace Funplay.Editor
{
    public sealed class SettingsControllerTests
    {
        [Test]
        public void NewSettings_EnableExecuteCodeSafetyChecksByDefault()
        {
            var projectPath = CreateTempProjectPath();

            try
            {
                var controller = new SettingsController(new TestApplicationPaths(projectPath));

                Assert.IsTrue(controller.ExecuteCodeSafetyChecksEnabled);
                StringAssert.Contains("\"executeCodeSafetyChecksEnabled\": true", ReadSettingsJson(projectPath));
                StringAssert.Contains("\"executeCodeSafetyChecksConfigured\": true", ReadSettingsJson(projectPath));
            }
            finally
            {
                DeleteTempProjectPath(projectPath);
            }
        }

        [Test]
        public void ExistingSettingsWithoutSafetyField_MigrateToEnabledDefault()
        {
            var projectPath = CreateTempProjectPath();

            try
            {
                var settingsDirectory = Path.Combine(projectPath, "UserSettings");
                Directory.CreateDirectory(settingsDirectory);
                File.WriteAllText(
                    Path.Combine(settingsDirectory, "FunplayMcpSettings.json"),
                    "{\"enabled\":false,\"port\":8765,\"toolExportProfile\":\"core\"}");

                var controller = new SettingsController(new TestApplicationPaths(projectPath));

                Assert.IsTrue(controller.ExecuteCodeSafetyChecksEnabled);
                StringAssert.Contains("\"executeCodeSafetyChecksEnabled\": true", ReadSettingsJson(projectPath));
                StringAssert.Contains("\"executeCodeSafetyChecksConfigured\": true", ReadSettingsJson(projectPath));
            }
            finally
            {
                DeleteTempProjectPath(projectPath);
            }
        }

        [Test]
        public void ExecuteCodeSafetyChecksSetting_PersistsFalseValue()
        {
            var projectPath = CreateTempProjectPath();

            try
            {
                var controller = new SettingsController(new TestApplicationPaths(projectPath));
                controller.ExecuteCodeSafetyChecksEnabled = false;

                var reloaded = new SettingsController(new TestApplicationPaths(projectPath));

                Assert.IsFalse(reloaded.ExecuteCodeSafetyChecksEnabled);
                StringAssert.Contains("\"executeCodeSafetyChecksEnabled\": false", ReadSettingsJson(projectPath));
                StringAssert.Contains("\"executeCodeSafetyChecksConfigured\": true", ReadSettingsJson(projectPath));
            }
            finally
            {
                DeleteTempProjectPath(projectPath);
            }
        }

        private static string ReadSettingsJson(string projectPath)
        {
            return File.ReadAllText(Path.Combine(projectPath, "UserSettings", "FunplayMcpSettings.json"));
        }

        private static string CreateTempProjectPath()
        {
            var path = Path.Combine(Path.GetTempPath(), "FunplaySettingsTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteTempProjectPath(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }

        private sealed class TestApplicationPaths : IApplicationPaths
        {
            public TestApplicationPaths(string projectPath)
            {
                ProjectPath = projectPath;
                AssetsPath = Path.Combine(projectPath, "Assets");
                TempPath = Path.Combine(projectPath, "Temp", "Funplay");
                DataPath = AssetsPath;
                PersistentDataPath = Path.Combine(projectPath, "PersistentData");
            }

            public string ProjectPath { get; }
            public string AssetsPath { get; }
            public string TempPath { get; }
            public string DataPath { get; }
            public string PersistentDataPath { get; }
        }
    }
}
