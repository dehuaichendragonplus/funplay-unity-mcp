// Copyright (C) Funplay. Licensed under MIT.

using UnityEngine;
using UnityEngine.UIElements;

namespace Funplay.Editor.MCP.Server
{
    internal sealed class FunplayMCPUpdatePanel
    {
        private VisualElement _container;
        private Label _statusLabel;
        private Button _updateButton;
        private ProgressBar _progressBar;

        public void AddTo(VisualElement parent)
        {
            _container = new VisualElement();
            _container.style.display = DisplayStyle.None;
            _container.style.backgroundColor = new Color(0.23f, 0.20f, 0.13f);
            _container.style.borderLeftWidth = 3;
            _container.style.borderLeftColor = new Color(1f, 0.75f, 0.3f);
            _container.style.borderTopLeftRadius = 4;
            _container.style.borderTopRightRadius = 4;
            _container.style.borderBottomLeftRadius = 4;
            _container.style.borderBottomRightRadius = 4;
            _container.style.paddingLeft = 8;
            _container.style.paddingRight = 8;
            _container.style.paddingTop = 6;
            _container.style.paddingBottom = 6;
            _container.style.marginBottom = 10;

            var updateRow = new VisualElement();
            updateRow.style.flexDirection = FlexDirection.Row;
            updateRow.style.alignItems = Align.Center;
            _container.Add(updateRow);

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 11;
            _statusLabel.style.color = new Color(0.95f, 0.88f, 0.68f);
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.flexGrow = 1;
            updateRow.Add(_statusLabel);

            _updateButton = new Button(FunplayMCPUpdateChecker.UpdateToLatestFromWindow);
            _updateButton.text = "Update";
            _updateButton.style.height = 24;
            _updateButton.style.minWidth = 86;
            _updateButton.style.marginLeft = 8;
            _updateButton.style.backgroundColor = new Color(0.82f, 0.48f, 0.18f);
            _updateButton.style.color = Color.white;
            updateRow.Add(_updateButton);

            _progressBar = new ProgressBar
            {
                lowValue = 0f,
                highValue = 1f,
                value = 0f
            };
            _progressBar.style.display = DisplayStyle.None;
            _progressBar.style.height = 16;
            _progressBar.style.marginTop = 6;
            _container.Add(_progressBar);

            parent.Add(_container);
            Refresh();
        }

        public void Refresh()
        {
            if (_container == null || _statusLabel == null || _updateButton == null || _progressBar == null)
                return;

            var state = FunplayMCPUpdateChecker.CurrentState;
            var showUpdatePanel = state.HasUpdateAvailable || state.IsUpdating || state.UpdateStarted;
            _container.style.display = showUpdatePanel ? DisplayStyle.Flex : DisplayStyle.None;
            if (!showUpdatePanel)
                return;

            if (state.IsUpdating || state.UpdateStarted)
            {
                _statusLabel.text = string.IsNullOrEmpty(state.StatusMessage)
                    ? $"Updating to version {state.LatestVersion}..."
                    : state.StatusMessage;
            }
            else
            {
                var installDescription = string.IsNullOrEmpty(state.InstallDescription)
                    ? string.Empty
                    : $" ({state.InstallDescription})";
                _statusLabel.text = $"Version {state.LatestVersion} is available{installDescription}.";
            }

            var showButton = state.HasUpdateAvailable && !state.IsUpdating && !state.UpdateStarted;
            _updateButton.style.display = showButton ? DisplayStyle.Flex : DisplayStyle.None;
            _updateButton.text = string.IsNullOrEmpty(state.LatestVersion)
                ? "Update"
                : $"Update to v{state.LatestVersion}";

            var showProgress = state.IsUpdating || state.UpdateStarted;
            _progressBar.style.display = showProgress ? DisplayStyle.Flex : DisplayStyle.None;
            _progressBar.value = Mathf.Clamp01(state.Progress);
            _progressBar.title = string.IsNullOrEmpty(state.StatusMessage)
                ? "Updating..."
                : state.StatusMessage;
        }
    }
}
