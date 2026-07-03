// Copyright (C) Funplay. Licensed under MIT.

using System;
using Funplay.Editor.Settings;
using UnityEditor;
using UnityEngine;
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
        private Label _brokerMonoHint;

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
            _brokerMonoPathField.SetValueWithoutNotify(_settings.MCPBrokerMonoPath);
            _brokerMonoPathField.RegisterValueChangedCallback(evt =>
            {
                _settings.MCPBrokerMonoPath = evt.newValue;
                EditorApplication.delayCall += UpdateBrokerStatus;
            });
            _brokerMonoPathField.style.marginBottom = 4;
            parent.Add(_brokerMonoPathField);

            _brokerMonoHint = new Label();
            _brokerMonoHint.style.whiteSpace = WhiteSpace.Normal;
            _brokerMonoHint.style.color = new Color(0.9f, 0.35f, 0.35f);
            _brokerMonoHint.style.marginBottom = 4;
            parent.Add(_brokerMonoHint);

            RefreshMonoPathAutoDetection();

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
            if (_brokerMonoHint != null)
                _brokerMonoHint.style.display = enabled && !string.IsNullOrEmpty(_brokerMonoHint.text)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
        }

        /// <summary>
        /// Auto-detection is display-only: it never writes to <see cref="ISettingsController.MCPBrokerMonoPath"/>,
        /// so clearing the field (or never touching it) keeps the setting at its real "auto-detect" default.
        /// </summary>
        private void RefreshMonoPathAutoDetection()
        {
            if (_brokerMonoPathField == null)
                return;

            if (!string.IsNullOrEmpty(_settings.MCPBrokerMonoPath))
            {
                _brokerMonoPathField.tooltip =
                    "Optional override for Unity's bundled Mono executable. Leave empty to auto-detect it from the Unity editor install.";
                SetMonoHint(null);
                return;
            }

            var detected = MCPBrokerProcessManager.ResolveMono(null);
            if (!string.IsNullOrEmpty(detected))
            {
                _brokerMonoPathField.SetValueWithoutNotify(detected);
                _brokerMonoPathField.tooltip =
                    "Auto-detected from the Unity editor install. Fill this in only if you need to override it.";
                SetMonoHint(null);
            }
            else
            {
                _brokerMonoPathField.tooltip =
                    "Optional override for Unity's bundled Mono executable. Leave empty to auto-detect it from the Unity editor install.";
                SetMonoHint("Could not auto-detect Unity's bundled Mono executable. Broker mode needs this path set manually.");
            }
        }

        private void SetMonoHint(string text)
        {
            if (_brokerMonoHint == null)
                return;

            _brokerMonoHint.text = text ?? string.Empty;
            _brokerMonoHint.style.display = !string.IsNullOrEmpty(text) && _settings.MCPBrokerModeEnabled
                ? DisplayStyle.Flex
                : DisplayStyle.None;
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
