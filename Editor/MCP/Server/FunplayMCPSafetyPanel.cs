// Copyright (C) Funplay. Licensed under MIT.

using Funplay.Editor.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace Funplay.Editor.MCP.Server
{
    internal static class FunplayMCPSafetyPanel
    {
        public static void AddTo(VisualElement parent, ISettingsController settings)
        {
            if (parent == null || settings == null)
                return;

            var header = new Label("Safety");
            header.style.fontSize = 12;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = new Color(0.75f, 0.75f, 0.75f);
            header.style.marginBottom = 4;
            parent.Add(header);

            var safetyToggle = new Toggle("Default execute_code safety checks");
            safetyToggle.SetValueWithoutNotify(settings.ExecuteCodeSafetyChecksEnabled);
            safetyToggle.RegisterValueChangedCallback(evt =>
            {
                settings.ExecuteCodeSafetyChecksEnabled = evt.newValue;
            });
            safetyToggle.style.marginBottom = 4;
            parent.Add(safetyToggle);

            var strictToggle = new Toggle("Strict filesystem guard");
            strictToggle.SetValueWithoutNotify(settings.ExecuteCodeStrictFilesystemSafetyEnabled);
            strictToggle.RegisterValueChangedCallback(evt =>
            {
                settings.ExecuteCodeStrictFilesystemSafetyEnabled = evt.newValue;
            });
            strictToggle.style.marginBottom = 4;
            parent.Add(strictToggle);

            var projectNamespacesToggle = new Toggle("Auto-inject project namespaces");
            projectNamespacesToggle.SetValueWithoutNotify(settings.ExecuteCodeProjectNamespaceInjectionEnabled);
            projectNamespacesToggle.RegisterValueChangedCallback(evt =>
            {
                settings.ExecuteCodeProjectNamespaceInjectionEnabled = evt.newValue;
            });
            projectNamespacesToggle.style.marginBottom = 4;
            parent.Add(projectNamespacesToggle);

            var safetyHint = new Label(
                "Strict guard blocks obvious destructive code, broad System.IO file writes, raw file streams, and absolute/user/system/traversal paths. " +
                "This is a defensive guard, not a complete sandbox. Explicit safety_checks=false still bypasses it for trusted local calls. " +
                "Project namespace auto-injection is off by default; when enabled, it only uses namespaces from loaded project assemblies.");
            safetyHint.style.fontSize = 10;
            safetyHint.style.color = new Color(0.65f, 0.65f, 0.65f);
            safetyHint.style.marginBottom = 10;
            safetyHint.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(safetyHint);
        }
    }
}
