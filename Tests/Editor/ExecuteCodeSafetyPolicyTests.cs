// Copyright (C) Funplay. Licensed under MIT.

using System.IO;
using Funplay.Editor.Tools.Builtins;
using Funplay.Editor.Tools.Helpers;
using NUnit.Framework;
using UnityEngine;

namespace Funplay.Editor.Tests
{
    public sealed class ExecuteCodeSafetyPolicyTests
    {
        [Test]
        public void BaseSafety_BlocksExistingDangerousPatterns()
        {
            AssertBlocked("File.Delete(\"Assets/test.txt\");", false, "File.Delete");
            AssertBlocked("while (true) { }", false, "while");
        }

        [Test]
        public void StrictSafety_BlocksBroadFilesystemWritesAndPaths()
        {
            AssertBlocked("File.WriteAllText(\"Assets/test.txt\", \"x\");", true, "File write");
            AssertBlocked("Directory.CreateDirectory(\"Assets/generated\");", true, "Directory write");
            AssertBlocked("var path = \"/Users/xyz/.ssh/config\";", true, "Absolute");
            AssertBlocked("var path = \"../ProjectSettings/ProjectSettings.asset\";", true, "Path traversal");
            AssertBlocked("var path = @\"C:\\Users\\Public\\test.txt\";", true, "Absolute");
        }

        [Test]
        public void NonStrictSafety_DoesNotApplyStrictFilesystemRules()
        {
            AssertAllowed("File.WriteAllText(\"Assets/test.txt\", \"x\");", false);
            AssertAllowed("var path = \"/Users/xyz/.ssh/config\";", false);
        }

        [Test]
        public void ToolResultFormatter_DetectsStructuredErrorResponses()
        {
            var error = ToolResultFormatter.Error("TEST_ERROR");

            Assert.IsTrue(ToolResultFormatter.IsError(error));
            Assert.IsFalse(ToolResultFormatter.IsError("Error: legacy"));
            Assert.IsFalse(ToolResultFormatter.IsError("{\"success\":true,\"message\":\"ok\"}"));
        }

        [Test]
        public void BuildCodeForCompilation_ProjectNamespaceInjectionDisabled_DoesNotInjectProjectNamespaces()
        {
            var fullCode = ScriptExecutionFunctions.BuildCodeForCompilation(
                "public class Smoke { public static string Run() { return \"ok\"; } }",
                "TempScript",
                false,
                out var actualClassName);

            Assert.AreEqual("Smoke", actualClassName);
            Assert.IsFalse(fullCode.Contains("using Funplay.Editor.Tools.Builtins;"), fullCode);
            Assert.IsFalse(fullCode.Contains("using Funplay.Editor.Tests;"), fullCode);
        }

        [Test]
        public void ReachableProjectNamespaceUsings_AreDerivedFromLoadedProjectAssemblies()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var usings = ScriptExecutionFunctions.GetReachableProjectNamespaceUsings(
                new[] { typeof(ScriptExecutionFunctions).Assembly },
                projectRoot);

            StringAssert.Contains("using Funplay.Editor.Tools.Builtins;", usings);
            Assert.IsFalse(usings.Contains("Funplay.Repro.Unreachable"), usings);
        }

        private static void AssertBlocked(string code, bool strict, string expectedReasonPart)
        {
            Assert.IsTrue(ExecuteCodeSafetyPolicy.TryFindViolation(code, strict, out _, out var reason));
            StringAssert.Contains(expectedReasonPart, reason);
        }

        private static void AssertAllowed(string code, bool strict)
        {
            Assert.IsFalse(ExecuteCodeSafetyPolicy.TryFindViolation(code, strict, out _, out _));
        }
    }
}
