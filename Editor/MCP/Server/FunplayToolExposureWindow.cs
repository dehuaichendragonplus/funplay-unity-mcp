// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Funplay.Editor.DI;
using Funplay.Editor.Services;
using Funplay.Editor.Settings;
using Funplay.Editor.Tools;

namespace Funplay.Editor.MCP.Server
{
    internal class FunplayToolExposureWindow : EditorWindow
    {
        private static readonly List<string> ProfileChoices = new List<string> { "core", "full" };

        private ISettingsController _settingsController;
        private MCPServerService _mcpServer;
        private PopupField<string> _editProfileField;
        private Label _statusLabel;
        private Label _descriptionLabel;
        private ScrollView _toolScrollView;
        private List<string> _allToolNames = new List<string>();
        private readonly Dictionary<string, string> _toolCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Toggle> _toolToggles = new Dictionary<string, Toggle>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _editingTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _editingProfile = "core";

        [MenuItem("Funplay/Tool Exposure")]
        public static void ShowWindow()
        {
            var window = GetWindow<FunplayToolExposureWindow>("Tool Exposure");
            window.minSize = new Vector2(460, 560);
            window.Show();
        }

        public void CreateGUI()
        {
            _settingsController = RootScopeServices.Services?.GetService(typeof(ISettingsController))
                as ISettingsController;
            _mcpServer = RootScopeServices.Services?.GetService(typeof(MCPServerService))
                as MCPServerService;

            if (_settingsController == null)
            {
                rootVisualElement.Add(new Label("Failed to initialize services."));
                return;
            }

            _settingsController.OnSettingsChanged += RefreshStatus;
            BuildUI();
        }

        private void OnDestroy()
        {
            if (_settingsController != null)
                _settingsController.OnSettingsChanged -= RefreshStatus;
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexGrow = 1;
            rootVisualElement.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            rootVisualElement.style.paddingLeft = 10;
            rootVisualElement.style.paddingRight = 10;
            rootVisualElement.style.paddingTop = 10;
            rootVisualElement.style.paddingBottom = 10;

            var title = new Label("Tool Exposure");
            title.style.fontSize = 17;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Color.white;
            title.style.marginBottom = 4;
            rootVisualElement.Add(title);

            var hint = new Label("Edit exactly which tools each MCP profile exposes. Choose the active profile from the MCP Server window. Saving changes restarts the running server automatically.");
            hint.style.fontSize = 11;
            hint.style.color = new Color(0.65f, 0.65f, 0.65f);
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 10;
            rootVisualElement.Add(hint);

            LoadAllTools();

            var activeProfile = GetActiveProfile();
            _editingProfile = ProfileChoices.Contains(_editingProfile) ? _editingProfile : activeProfile;
            _editProfileField = new PopupField<string>("Edit Tool List", ProfileChoices, ProfileChoices.IndexOf(_editingProfile));
            _editProfileField.style.marginBottom = 8;
            _editProfileField.RegisterValueChangedCallback(evt =>
            {
                _editingProfile = evt.newValue;
                LoadEditingTools();
                RebuildToolList();
                RefreshStatus();
            });
            rootVisualElement.Add(_editProfileField);

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginBottom = 10;

            var selectAllButton = CreateActionButton("Select All", SelectAllTools, 88, new Color(0.24f, 0.42f, 0.58f));
            buttonRow.Add(selectAllButton);

            var clearButton = CreateActionButton("Clear", ClearTools, 64, new Color(0.46f, 0.36f, 0.24f));
            clearButton.style.marginLeft = 6;
            buttonRow.Add(clearButton);

            var defaultButton = CreateActionButton("Use Default", UseDefaultTools, 92, new Color(0.34f, 0.34f, 0.34f));
            defaultButton.style.marginLeft = 6;
            buttonRow.Add(defaultButton);

            var saveButton = CreateActionButton("Save", SaveEditingTools, 64, new Color(0.2f, 0.5f, 0.3f));
            saveButton.style.marginLeft = 6;
            buttonRow.Add(saveButton);

            rootVisualElement.Add(buttonRow);

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 11;
            _statusLabel.style.marginBottom = 6;
            rootVisualElement.Add(_statusLabel);

            _descriptionLabel = new Label();
            _descriptionLabel.style.fontSize = 11;
            _descriptionLabel.style.color = new Color(0.68f, 0.68f, 0.68f);
            _descriptionLabel.style.whiteSpace = WhiteSpace.Normal;
            rootVisualElement.Add(_descriptionLabel);

            _toolScrollView = new ScrollView(ScrollViewMode.Vertical);
            _toolScrollView.style.flexGrow = 1;
            _toolScrollView.style.backgroundColor = new Color(0.14f, 0.14f, 0.14f);
            _toolScrollView.style.borderTopLeftRadius = 4;
            _toolScrollView.style.borderTopRightRadius = 4;
            _toolScrollView.style.borderBottomLeftRadius = 4;
            _toolScrollView.style.borderBottomRightRadius = 4;
            _toolScrollView.style.paddingLeft = 6;
            _toolScrollView.style.paddingRight = 6;
            _toolScrollView.style.paddingTop = 5;
            _toolScrollView.style.paddingBottom = 5;
            rootVisualElement.Add(_toolScrollView);

            LoadEditingTools();
            RebuildToolList();
            RefreshStatus();
        }

        private Button CreateActionButton(string text, Action action, int width, Color color)
        {
            var button = new Button(action);
            button.text = text;
            button.style.height = 26;
            button.style.width = width;
            button.style.backgroundColor = color;
            button.style.color = Color.white;
            return button;
        }

        private void LoadAllTools()
        {
            _toolCategories.Clear();

            _allToolNames = ToolSchemaBuilder.BuildAll()
                .Select(tool => tool.function.name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var toolName in _allToolNames)
                _toolCategories[toolName] = GetToolCategory(toolName);
        }

        private void LoadEditingTools()
        {
            _editingTools = new HashSet<string>(GetEffectiveTools(_editingProfile), StringComparer.OrdinalIgnoreCase);
        }

        private IEnumerable<string> GetEffectiveTools(string profile)
        {
            if (string.Equals(profile, "full", StringComparison.OrdinalIgnoreCase))
            {
                return _settingsController.MCPFullToolsConfigured
                    ? _settingsController.MCPFullTools
                    : _allToolNames;
            }

            return _settingsController.MCPCoreToolsConfigured
                ? _settingsController.MCPCoreTools
                : MCPToolExportPolicy.DefaultCoreTools.Where(tool => _allToolNames.Contains(tool, StringComparer.OrdinalIgnoreCase));
        }

        private bool IsEditingProfileConfigured()
        {
            return string.Equals(_editingProfile, "full", StringComparison.OrdinalIgnoreCase)
                ? _settingsController.MCPFullToolsConfigured
                : _settingsController.MCPCoreToolsConfigured;
        }

        private string GetActiveProfile()
        {
            var currentProfile = MCPToolExportPolicy.ToSettingValue(
                MCPToolExportPolicy.Parse(_settingsController.MCPToolExportProfile));
            return ProfileChoices.Contains(currentProfile) ? currentProfile : "core";
        }

        private void RebuildToolList()
        {
            if (_toolScrollView == null)
                return;

            _toolScrollView.contentContainer.Clear();
            _toolToggles.Clear();

            var groupedTools = _allToolNames
                .GroupBy(GetCachedToolCategory)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groupedTools)
            {
                var categoryTools = group
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                _toolScrollView.Add(CreateCategorySection(group.Key, categoryTools));
            }
        }

        private VisualElement CreateCategorySection(string category, IReadOnlyList<string> categoryTools)
        {
            var selectedCount = categoryTools.Count(tool => _editingTools.Contains(tool));

            var foldout = new Foldout
            {
                text = $"{category} ({selectedCount}/{categoryTools.Count})",
                value = true
            };
            foldout.style.marginBottom = 5;

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.marginLeft = 16;
            actions.style.marginTop = 2;
            actions.style.marginBottom = 4;

            var selectButton = CreateCategoryButton("Select", () => SetCategoryTools(categoryTools, true));
            actions.Add(selectButton);

            var clearButton = CreateCategoryButton("Clear", () => SetCategoryTools(categoryTools, false));
            clearButton.style.marginLeft = 4;
            actions.Add(clearButton);

            foldout.Add(actions);

            foreach (var toolName in categoryTools)
            {
                var toggle = new Toggle(toolName);
                toggle.SetValueWithoutNotify(_editingTools.Contains(toolName));
                toggle.style.marginLeft = 16;
                toggle.style.marginBottom = 2;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                        _editingTools.Add(toolName);
                    else
                        _editingTools.Remove(toolName);

                    RebuildToolList();
                    RefreshStatus();
                });

                _toolToggles[toolName] = toggle;
                foldout.Add(toggle);
            }

            return foldout;
        }

        private Button CreateCategoryButton(string text, Action action)
        {
            var button = new Button(action);
            button.text = text;
            button.style.height = 20;
            button.style.width = 58;
            button.style.fontSize = 10;
            return button;
        }

        private void SetCategoryTools(IEnumerable<string> categoryTools, bool enabled)
        {
            foreach (var toolName in categoryTools)
            {
                if (enabled)
                    _editingTools.Add(toolName);
                else
                    _editingTools.Remove(toolName);
            }

            RebuildToolList();
            RefreshStatus();
        }

        private void SetAllToolToggles(bool enabled)
        {
            if (enabled)
                _editingTools = new HashSet<string>(_allToolNames, StringComparer.OrdinalIgnoreCase);
            else
                _editingTools.Clear();

            foreach (var entry in _toolToggles)
                entry.Value.SetValueWithoutNotify(_editingTools.Contains(entry.Key));

            RefreshStatus();
        }

        private void SelectAllTools()
        {
            SetAllToolToggles(true);
        }

        private void ClearTools()
        {
            SetAllToolToggles(false);
        }

        private void UseDefaultTools()
        {
            if (string.Equals(_editingProfile, "full", StringComparison.OrdinalIgnoreCase))
                _settingsController.MCPFullTools = null;
            else
                _settingsController.MCPCoreTools = null;

            LoadEditingTools();
            RebuildToolList();
            RefreshStatus();
        }

        private void SaveEditingTools()
        {
            var selected = _editingTools
                .Where(tool => _allToolNames.Contains(tool, StringComparer.OrdinalIgnoreCase))
                .OrderBy(tool => tool, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (string.Equals(_editingProfile, "full", StringComparison.OrdinalIgnoreCase))
                _settingsController.MCPFullTools = selected;
            else
                _settingsController.MCPCoreTools = selected;

            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (_settingsController == null)
                return;

            var activeProfile = GetActiveProfile();
            if (_editProfileField != null)
                _editProfileField.SetValueWithoutNotify(_editingProfile);

            var selectedCount = _editingTools?.Count ?? 0;
            var totalCount = _allToolNames?.Count ?? 0;
            var source = IsEditingProfileConfigured() ? "custom" : "default";

            if (_statusLabel != null)
            {
                var serverState = _mcpServer != null && _mcpServer.IsRunning
                    ? $"Server running on http://127.0.0.1:{_mcpServer.Port}/"
                    : "Server stopped";
                _statusLabel.text = $"Active: {activeProfile} | Editing {_editingProfile}: {selectedCount}/{totalCount} tools ({source}) | {serverState}";
                _statusLabel.style.color = activeProfile == "core"
                    ? new Color(0.55f, 0.85f, 0.55f)
                    : new Color(0.55f, 0.75f, 1f);
            }

            if (_descriptionLabel != null)
            {
                _descriptionLabel.text = string.Equals(_editingProfile, "core", StringComparison.OrdinalIgnoreCase)
                    ? "core defaults to the focused Unity workflow tool set. Select tools below and click Save to override that list."
                    : "full defaults to every registered MCP tool. Select tools below and click Save to make full expose only that custom list.";
            }
        }

        private string GetCachedToolCategory(string toolName)
        {
            return _toolCategories.TryGetValue(toolName, out var category) ? category : "Other";
        }

        private string GetToolCategory(string toolName)
        {
            if (ToolRegistry.MethodCache.TryGetValue(toolName, out var method))
            {
                var provider = method.DeclaringType?.GetCustomAttribute<ToolProviderAttribute>();
                return FormatCategory(provider?.Category ?? method.DeclaringType?.Name ?? "Other");
            }

            if (ToolRegistry.ManualTools.ContainsKey(toolName))
                return "Manual";

            return "Other";
        }

        private static string FormatCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return "Other";

            var trimmed = category.Trim();
            var result = new System.Text.StringBuilder();
            for (var i = 0; i < trimmed.Length; i++)
            {
                var current = trimmed[i];
                if (i > 0 &&
                    char.IsUpper(current) &&
                    !char.IsWhiteSpace(trimmed[i - 1]) &&
                    (char.IsLower(trimmed[i - 1]) || (i + 1 < trimmed.Length && char.IsLower(trimmed[i + 1]))))
                {
                    result.Append(' ');
                }

                result.Append(current);
            }

            return result.ToString();
        }
    }
}
