// Copyright (C) Funplay. Licensed under MIT.

using Funplay.Editor.Tools.Builtins;
using Funplay.Editor.Tools.Helpers;
using NUnit.Framework;

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
