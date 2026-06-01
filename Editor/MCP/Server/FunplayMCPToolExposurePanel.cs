// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.Collections.Generic;
using Funplay.Editor.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace Funplay.Editor.MCP.Server
{
    internal sealed class FunplayMCPToolExposurePanel
    {
        private readonly ISettingsController _settings;
        private readonly Action _refreshStatus;

        public FunplayMCPToolExposurePanel(ISettingsController settings, Action refreshStatus)
        {
            _settings = settings;
            _refreshStatus = refreshStatus;
        }

        public void AddTo(VisualElement parent)
        {
            var toolProfileChoices = new List<string> { "core", "full" };
            var currentProfile = _settings.MCPToolExportProfile ?? "core";
            var toolProfileField = new PopupField<string>(
                "Tool Exposure",
                toolProfileChoices,
                Mathf.Max(0, toolProfileChoices.IndexOf(currentProfile)));
            toolProfileField.SetValueWithoutNotify(currentProfile);
            toolProfileField.RegisterValueChangedCallback(evt =>
            {
                _settings.MCPToolExportProfile = evt.newValue;
                _refreshStatus?.Invoke();
            });
            toolProfileField.style.marginBottom = 4;
            parent.Add(toolProfileField);

            var hint = new Label("core reduces tool-list noise for AI clients. full exposes every tool.");
            hint.style.fontSize = 10;
            hint.style.color = new Color(0.65f, 0.65f, 0.65f);
            hint.style.marginBottom = 10;
            parent.Add(hint);
        }
    }
}
