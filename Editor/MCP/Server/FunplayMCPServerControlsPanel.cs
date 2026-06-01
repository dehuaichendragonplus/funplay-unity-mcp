// Copyright (C) Funplay. Licensed under MIT.

using System;
using Funplay.Editor.Settings;
using UnityEditor;
using UnityEngine.UIElements;

namespace Funplay.Editor.MCP.Server
{
    internal sealed class FunplayMCPServerControlsPanel
    {
        private readonly ISettingsController _settings;
        private readonly MCPServerService _server;
        private readonly Action _refreshStatus;

        public FunplayMCPServerControlsPanel(
            ISettingsController settings,
            MCPServerService server,
            Action refreshStatus)
        {
            _settings = settings;
            _server = server;
            _refreshStatus = refreshStatus;
        }

        public void AddTo(VisualElement parent)
        {
            var toggle = new Toggle("Enable MCP Server");
            toggle.SetValueWithoutNotify(_settings.MCPServerEnabled);
            toggle.RegisterValueChangedCallback(evt =>
            {
                _settings.MCPServerEnabled = evt.newValue;
                if (evt.newValue)
                    _ = _server.StartAsync();
                else
                    _ = _server.StopAsync();

                EditorApplication.delayCall += () =>
                    EditorApplication.delayCall += InvokeRefreshStatus;
            });
            toggle.style.marginBottom = 4;
            parent.Add(toggle);

            var portField = new IntegerField("Server Port");
            portField.SetValueWithoutNotify(_settings.MCPServerPort);
            portField.RegisterValueChangedCallback(evt =>
            {
                _settings.MCPServerPort = evt.newValue;
            });
            portField.style.marginBottom = 10;
            parent.Add(portField);
        }

        private void InvokeRefreshStatus()
        {
            _refreshStatus?.Invoke();
        }
    }
}
