// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Funplay.Editor.Settings;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Funplay.Editor.MCP.Server
{
    internal sealed class FunplayMCPClientConfigPanel
    {
        private readonly ISettingsController _settings;
        private readonly MCPServerService _server;
        private readonly Action _rebuildWindow;
        private MCPConfigTarget[] _targets;
        private int _selectedTargetIndex;
        private Label _configStatusLabel;
        private Label _configPathLabel;

        public FunplayMCPClientConfigPanel(
            ISettingsController settings,
            MCPServerService server,
            Action rebuildWindow)
        {
            _settings = settings;
            _server = server;
            _rebuildWindow = rebuildWindow;
        }

        public void AddTo(VisualElement parent)
        {
            var label = new Label("One-Click MCP Configuration");
            label.style.fontSize = 12;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new Color(0.75f, 0.75f, 0.75f);
            label.style.marginBottom = 6;
            parent.Add(label);

            var homePath = GetUserHomePath();
            _targets = CreateTargets(homePath);
            var names = _targets.Select(target => target.Name).ToList();

            _selectedTargetIndex = Mathf.Clamp(_selectedTargetIndex, 0, _targets.Length - 1);
            var persistedTargetName = _settings.MCPSelectedConfigTarget;
            if (!string.IsNullOrWhiteSpace(persistedTargetName))
            {
                var persistedIndex = names.FindIndex(name =>
                    string.Equals(name, persistedTargetName, StringComparison.OrdinalIgnoreCase));
                if (persistedIndex >= 0)
                    _selectedTargetIndex = persistedIndex;
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;

            var dropdown = new PopupField<string>(names, _selectedTargetIndex);
            dropdown.style.flexGrow = 1;
            dropdown.style.height = 26;
            dropdown.RegisterValueChangedCallback(evt =>
            {
                _selectedTargetIndex = names.IndexOf(evt.newValue);
                _settings.MCPSelectedConfigTarget = evt.newValue;
                _rebuildWindow?.Invoke();
            });
            row.Add(dropdown);

            var configureButton = new Button(() =>
            {
                ConfigureMCPForTarget(_targets[_selectedTargetIndex]);
                RefreshStatus();
            });
            configureButton.text = "Configure";
            configureButton.style.height = 26;
            configureButton.style.width = 80;
            configureButton.style.marginLeft = 4;
            configureButton.style.backgroundColor = new Color(0.2f, 0.5f, 0.3f);
            configureButton.style.color = Color.white;
            row.Add(configureButton);

            var selectedTarget = _targets[_selectedTargetIndex];
            var skillsSupported = !string.IsNullOrEmpty(MapTargetNameToSkillsPlatformId(selectedTarget.Name));
            var configureSkillsButton = new Button(() =>
            {
                ConfigureMCPAndSkillsForTarget(_targets[_selectedTargetIndex]);
                RefreshStatus();
            });
            configureSkillsButton.text = "Configure + Skills";
            configureSkillsButton.style.height = 26;
            configureSkillsButton.style.width = 130;
            configureSkillsButton.style.marginLeft = 4;
            configureSkillsButton.style.backgroundColor = new Color(0.25f, 0.45f, 0.65f);
            configureSkillsButton.style.color = Color.white;
            configureSkillsButton.SetEnabled(skillsSupported);
            row.Add(configureSkillsButton);

            parent.Add(row);

            var skillsHint = new Label(skillsSupported
                ? "Configure + Skills also installs the project MCP workflow skill."
                : "Project skills are currently available for Claude Code, Cursor, and Codex.");
            skillsHint.style.fontSize = 10;
            skillsHint.style.color = new Color(0.6f, 0.6f, 0.6f);
            skillsHint.style.marginBottom = 4;
            skillsHint.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(skillsHint);

            _configStatusLabel = new Label();
            _configStatusLabel.style.fontSize = 11;
            _configStatusLabel.style.marginBottom = 2;
            parent.Add(_configStatusLabel);

            _configPathLabel = new Label();
            _configPathLabel.style.fontSize = 10;
            _configPathLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            _configPathLabel.style.marginBottom = 6;
            _configPathLabel.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(_configPathLabel);

            RefreshStatus();
        }

        public void RefreshStatus()
        {
            if (_configStatusLabel == null || _configPathLabel == null || _targets == null)
                return;

            var idx = Mathf.Clamp(_selectedTargetIndex, 0, _targets.Length - 1);
            var target = _targets[idx];

            if (target.IsLMStudio)
            {
                var existingPaths = GetExistingLMStudioConfigPaths(GetUserHomePath());
                bool hasExistingConfig = existingPaths.Count > 0;

                _configStatusLabel.text = hasExistingConfig
                    ? "Status: Existing LM Studio config found"
                    : "Status: Configure opens LM Studio Add MCP link";
                _configStatusLabel.style.color = hasExistingConfig
                    ? new Color(0.4f, 1f, 0.4f)
                    : new Color(1f, 0.75f, 0.4f);

                _configPathLabel.text = hasExistingConfig
                    ? "Existing config: " + string.Join(" | ", existingPaths)
                    : "LM Studio config path varies by version. Configure uses lmstudio://add_mcp and does not create guessed paths.";
                return;
            }

            bool exists = File.Exists(target.ConfigPath);
            _configStatusLabel.text = exists ? "Status: Configured" : "Status: Not configured";
            _configStatusLabel.style.color = exists
                ? new Color(0.4f, 1f, 0.4f)
                : new Color(1f, 0.6f, 0.4f);
            _configPathLabel.text = target.ConfigPath;
        }

        private MCPConfigTarget[] CreateTargets(string homePath)
        {
            return new[]
            {
                new MCPConfigTarget
                {
                    Name = "Claude Code",
                    ConfigPath = Path.Combine(homePath, ".claude.json"),
                    IncludeTypeField = true
                },
                new MCPConfigTarget
                {
                    Name = "Cursor",
                    ConfigPath = Path.Combine(homePath, ".cursor", "mcp.json"),
                },
                new MCPConfigTarget
                {
                    Name = "LM Studio",
                    ConfigPath = GetLMStudioDisplayPath(homePath),
                    IsLMStudio = true,
                },
                new MCPConfigTarget
                {
                    Name = "VS Code",
                    ConfigPath = GetVSCodeConfigPath(homePath),
                    IncludeTypeField = true,
                    RootKey = "servers"
                },
                new MCPConfigTarget
                {
                    Name = "Trae",
                    ConfigPath = Path.Combine(homePath, ".trae", "mcp.json"),
                },
                new MCPConfigTarget
                {
                    Name = "Kiro",
                    ConfigPath = Path.Combine(homePath, ".kiro", "settings", "mcp.json"),
                    IncludeTypeField = true,
                    RootKey = "mcpServers"
                },
                new MCPConfigTarget
                {
                    Name = "Codex",
                    ConfigPath = Path.Combine(homePath, ".codex", "config.toml"),
                    IsToml = true,
                },
            };
        }

        private void ConfigureMCPForTarget(MCPConfigTarget target)
        {
            try
            {
                WriteMCPConfigurationForTarget(target);

                var message = target.IsLMStudio
                    ? BuildLMStudioConfiguredMessage()
                    : $"MCP configuration written to:\n{target.ConfigPath}\n\n" +
                      $"Please restart {target.Name} for it to take effect.";

                EditorUtility.DisplayDialog("MCP Configuration", message, "OK");
                _rebuildWindow?.Invoke();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "MCP Configuration Error",
                    $"Configuration failed:\n{ex.Message}",
                    "OK");
            }
        }

        private void ConfigureMCPAndSkillsForTarget(MCPConfigTarget target)
        {
            try
            {
                WriteMCPConfigurationForTarget(target);

                var platformId = MapTargetNameToSkillsPlatformId(target.Name);
                if (string.IsNullOrEmpty(platformId))
                {
                    EditorUtility.DisplayDialog(
                        "MCP Configuration",
                        $"MCP configuration written to:\n{target.ConfigPath}\n\n" +
                        "Project skills are currently available for Claude Code, Cursor, and Codex.",
                        "OK");

                    _rebuildWindow?.Invoke();
                    return;
                }

                if (!ConfigureProjectSkillsForPlatform(platformId))
                    return;

                var projectRoot = GetProjectRootPath();
                var manifest = ProjectSkillsManager.LoadManifest(projectRoot);
                var generatedPaths = ProjectSkillsManager.GetGeneratedPathsForPlatform(projectRoot, manifest, platformId);

                EditorUtility.DisplayDialog(
                    "MCP Configuration",
                    $"MCP configuration written to:\n{target.ConfigPath}\n\n" +
                    "Project MCP workflow skill installed:\n" +
                    string.Join("\n", generatedPaths),
                    "OK");

                _rebuildWindow?.Invoke();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "MCP Configuration Error",
                    $"Configuration failed:\n{ex.Message}",
                    "OK");
            }
        }

        private void WriteMCPConfigurationForTarget(MCPConfigTarget target)
        {
            if (target.IsLMStudio)
            {
                ConfigureLMStudioTarget(target);
                return;
            }

            var dir = Path.GetDirectoryName(target.ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (target.IsToml)
                ConfigureTomlTarget(target);
            else
                ConfigureJsonTarget(target);
        }

        private bool ConfigureProjectSkillsForPlatform(string platformId)
        {
            var projectRoot = GetProjectRootPath();
            var manifest = ProjectSkillsManager.LoadManifest(projectRoot);
            var selectedPlatforms = new HashSet<string>(manifest.platforms, StringComparer.OrdinalIgnoreCase)
            {
                platformId
            };

            var conflictPaths = ProjectSkillsManager.GetPlatformConflictPaths(projectRoot, selectedPlatforms);
            if (conflictPaths.Length > 0)
            {
                var overwrite = EditorUtility.DisplayDialog(
                    "Project Skills Configuration",
                    "Existing non-managed project instruction files were found:\n\n" +
                    string.Join("\n", conflictPaths) +
                    "\n\nOverwrite them with Funplay-managed files?",
                    "Overwrite",
                    "Cancel");

                if (!overwrite)
                    return false;
            }

            ProjectSkillsManager.ApplyConfiguration(projectRoot, selectedPlatforms, manifest.optionalSkills);
            return true;
        }

        private void ConfigureJsonTarget(MCPConfigTarget target)
        {
            var rootKey = string.IsNullOrEmpty(target.RootKey) ? "mcpServers" : target.RootKey;
            var serverName = "funplay";
            var entry = CreateHttpEntry(target);
            Dictionary<string, object> root;

            if (File.Exists(target.ConfigPath))
            {
                var existingJson = File.ReadAllText(target.ConfigPath);
                var parsed = SimpleJsonHelper.Deserialize(existingJson) as Dictionary<string, object>;

                if (parsed != null && parsed.ContainsKey(rootKey))
                {
                    root = parsed;
                    var servers = root[rootKey] as Dictionary<string, object>;
                    if (servers != null)
                        servers[serverName] = entry;
                    else
                        root[rootKey] = new Dictionary<string, object> { [serverName] = entry };
                }
                else
                {
                    root = parsed ?? new Dictionary<string, object>();
                    root[rootKey] = new Dictionary<string, object> { [serverName] = entry };
                }
            }
            else
            {
                root = new Dictionary<string, object>
                {
                    [rootKey] = new Dictionary<string, object> { [serverName] = entry }
                };
            }

            File.WriteAllText(target.ConfigPath, SimpleJsonHelper.Serialize(root));
        }

        private void ConfigureTomlTarget(MCPConfigTarget target)
        {
            var sectionHeader = "[mcp_servers.funplay]";
            var tomlSection = CreateTomlSection(target);
            var content = File.Exists(target.ConfigPath) ? File.ReadAllText(target.ConfigPath) : string.Empty;

            if (content.Contains(sectionHeader))
            {
                var startIdx = content.IndexOf(sectionHeader, StringComparison.Ordinal);
                var afterHeader = startIdx + sectionHeader.Length;
                var nextSection = content.IndexOf("\n[", afterHeader, StringComparison.Ordinal);
                var endIdx = nextSection >= 0 ? nextSection : content.Length;
                content = content.Substring(0, startIdx) + tomlSection + content.Substring(endIdx);
            }
            else
            {
                if (content.Length > 0 && !content.EndsWith("\n"))
                    content += "\n";
                content += "\n" + tomlSection;
            }

            File.WriteAllText(target.ConfigPath, content);
        }

        private void ConfigureLMStudioTarget(MCPConfigTarget target)
        {
            OpenLMStudioAddMCPLink(target);

            foreach (var configPath in GetExistingLMStudioConfigPaths(GetUserHomePath()))
            {
                var fileTarget = target;
                fileTarget.ConfigPath = configPath;
                ConfigureJsonTarget(fileTarget);
            }
        }

        private void OpenLMStudioAddMCPLink(MCPConfigTarget target)
        {
            var config = SimpleJsonHelper.Serialize(CreateHttpEntry(target));
            var encodedConfig = Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes(config)));
            Application.OpenURL($"lmstudio://add_mcp?name=funplay&config={encodedConfig}");
        }

        private string BuildLMStudioConfiguredMessage()
        {
            var existingPaths = GetExistingLMStudioConfigPaths(GetUserHomePath());
            var message = "Opened LM Studio's Add MCP link for Funplay.\n\n";

            if (existingPaths.Count > 0)
            {
                message += "Also updated existing LM Studio config file(s):\n" +
                           string.Join("\n", existingPaths) +
                           "\n\nPlease restart LM Studio or reload MCP integrations if needed.";
            }
            else
            {
                message += "No existing LM Studio mcp.json file was found, so Funplay did not create a guessed path.\n\n" +
                           "If LM Studio did not open automatically, open LM Studio > Program > Install > Edit mcp.json and add Funplay there.";
            }

            return message;
        }

        private Dictionary<string, object> CreateHttpEntry(MCPConfigTarget target)
        {
            var entry = new Dictionary<string, object>
            {
                ["url"] = GetServerUrl()
            };

            if (target.IncludeTypeField)
                entry["type"] = "http";

            return entry;
        }

        private string CreateTomlSection(MCPConfigTarget target)
        {
            if (!target.IsToml)
                return string.Empty;

            return $"[mcp_servers.funplay]\nurl = \"{GetServerUrl()}\"\n";
        }

        private string GetServerUrl()
        {
            var port = _server != null && _server.IsRunning
                ? _server.Port
                : _settings.MCPServerPort;
            return $"http://127.0.0.1:{port}/";
        }

        private static string GetProjectRootPath()
        {
            return Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
        }

        private static string MapTargetNameToSkillsPlatformId(string targetName)
        {
            switch (targetName?.Trim())
            {
                case "Codex":
                    return "codex";
                case "Claude Code":
                    return "claude";
                case "Cursor":
                    return "cursor";
                default:
                    return null;
            }
        }

        private static string GetUserHomePath()
        {
            var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(homePath))
                return homePath;

            var homeDrive = Environment.GetEnvironmentVariable("HOMEDRIVE");
            var homeDir = Environment.GetEnvironmentVariable("HOMEPATH");
            if (!string.IsNullOrEmpty(homeDrive) && !string.IsNullOrEmpty(homeDir))
                return homeDrive + homeDir;

            return Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        }

        private static string GetLMStudioDisplayPath(string homePath)
        {
            var existingPaths = GetExistingLMStudioConfigPaths(homePath);
            if (existingPaths.Count > 0)
                return string.Join(" | ", existingPaths);

            return "LM Studio Add MCP link (fallback: Program > Install > Edit mcp.json)";
        }

        private static List<string> GetExistingLMStudioConfigPaths(string homePath)
        {
            return GetLMStudioCandidateConfigPaths(homePath)
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<string> GetLMStudioCandidateConfigPaths(string homePath)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                yield return Path.Combine(homePath, ".cache", "lm-studio", "mcp.json");
                yield return Path.Combine(homePath, ".lmstudio", "mcp.json");
                yield break;
            }

            yield return Path.Combine(homePath, ".lmstudio", "mcp.json");
            yield return Path.Combine(homePath, ".cache", "lm-studio", "mcp.json");
        }

        private static string GetVSCodeConfigPath(string homePath)
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    if (!string.IsNullOrEmpty(appData))
                        return Path.Combine(appData, "Code", "User", "mcp.json");
                    break;

                case RuntimePlatform.OSXEditor:
                    var macPrimaryPath = Path.Combine(homePath, "Library", "Application Support", "Code", "User", "mcp.json");
                    var macPrimaryDirectory = Path.GetDirectoryName(macPrimaryPath);
                    if (File.Exists(macPrimaryPath) ||
                        (!string.IsNullOrEmpty(macPrimaryDirectory) && Directory.Exists(macPrimaryDirectory)))
                    {
                        return macPrimaryPath;
                    }

                    return Path.Combine(homePath, ".vscode", "mcp.json");

                case RuntimePlatform.LinuxEditor:
                    return Path.Combine(homePath, ".config", "Code", "User", "mcp.json");
            }

            return Path.Combine(homePath, ".vscode", "mcp.json");
        }

        private struct MCPConfigTarget
        {
            public string Name;
            public string ConfigPath;
            public string RootKey;
            public bool IsToml;
            public bool IncludeTypeField;
            public bool IsLMStudio;
        }
    }
}
