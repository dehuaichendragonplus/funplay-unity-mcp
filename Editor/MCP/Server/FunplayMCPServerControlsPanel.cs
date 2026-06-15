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
        private Label _brokerStatus;
        private TextField _brokerMonoPathField;

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
                {
                    _ = _server.StopAsync();
                    MCPBrokerProcessManager.Stop();
                }

                EditorApplication.delayCall += () =>
                    EditorApplication.delayCall += () => { UpdateBrokerStatus(); InvokeRefreshStatus(); };
            });
            toggle.style.marginBottom = 4;
            parent.Add(toggle);

            var portField = new IntegerField("Server Port");
            portField.SetValueWithoutNotify(_settings.MCPServerPort);
            portField.RegisterValueChangedCallback(evt =>
            {
                _settings.MCPServerPort = evt.newValue;
            });
            portField.style.marginBottom = 8;
            parent.Add(portField);

            var brokerToggle = new Toggle("Experimental Broker Mode");
            brokerToggle.tooltip =
                "Optional: runs a tiny local broker process that owns the MCP HTTP port and keeps " +
                "client requests alive while Unity reloads the scripting domain. Default direct mode is unchanged.";
            brokerToggle.SetValueWithoutNotify(_settings.MCPBrokerModeEnabled);
            brokerToggle.RegisterValueChangedCallback(evt =>
            {
                _settings.MCPBrokerModeEnabled = evt.newValue;
                UpdateBrokerControls(evt.newValue);

                if (_settings.MCPServerEnabled)
                {
                    _ = _server.StopAsync();
                    EditorApplication.delayCall += () => _ = _server.StartAsync();
                }
                else if (!evt.newValue)
                {
                    MCPBrokerProcessManager.Stop();
                }

                EditorApplication.delayCall += () =>
                    EditorApplication.delayCall += () => { UpdateBrokerStatus(); InvokeRefreshStatus(); };
            });
            brokerToggle.style.marginBottom = 4;
            parent.Add(brokerToggle);

            _brokerMonoPathField = new TextField("Broker Mono Path");
            _brokerMonoPathField.tooltip =
                "Optional override for Unity's bundled Mono executable. Leave empty to auto-detect it from the Unity editor install.";
            _brokerMonoPathField.SetValueWithoutNotify(_settings.MCPBrokerMonoPath);
            _brokerMonoPathField.RegisterValueChangedCallback(evt =>
            {
                _settings.MCPBrokerMonoPath = evt.newValue;
                EditorApplication.delayCall += UpdateBrokerStatus;
            });
            _brokerMonoPathField.style.marginBottom = 4;
            parent.Add(_brokerMonoPathField);

            _brokerStatus = new Label();
            _brokerStatus.style.whiteSpace = WhiteSpace.Normal;
            _brokerStatus.style.opacity = 0.78f;
            _brokerStatus.style.marginBottom = 10;
            parent.Add(_brokerStatus);

            UpdateBrokerControls(_settings.MCPBrokerModeEnabled);
            UpdateBrokerStatus();
        }

        private void InvokeRefreshStatus()
        {
            _refreshStatus?.Invoke();
        }

        private void UpdateBrokerControls(bool enabled)
        {
            if (_brokerMonoPathField != null)
                _brokerMonoPathField.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateBrokerStatus()
        {
            if (_brokerStatus == null)
                return;

            if (!_settings.MCPBrokerModeEnabled)
            {
                _brokerStatus.text = "Transport: Direct HTTP.";
                return;
            }

            if (MCPBrokerProcessManager.IsRunning(out var pid, out var port))
            {
                _brokerStatus.text = "Transport: Broker running (pid " + pid + ", port " + port + ").";
                return;
            }

            var error = MCPBrokerProcessManager.LastError;
            _brokerStatus.text = string.IsNullOrEmpty(error)
                ? "Transport: Broker will start with the MCP server."
                : "Transport: Broker not running - " + error;
        }
    }
}
