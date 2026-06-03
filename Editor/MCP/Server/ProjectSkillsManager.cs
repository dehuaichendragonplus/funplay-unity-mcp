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
    internal static class ProjectSkillsManager
    {
        internal const string ManagedMarker = "<!-- Funplay Unity MCP managed project skills -->";

        private const string ManifestDirectory = ".funplay/skills";
        private const string ManifestFileName = "manifest.json";

        private static readonly string[] SupportedPlatforms = { "codex", "claude", "cursor" };

        private static readonly SkillDefinition[] SkillCatalog =
        {
            new SkillDefinition(
                "unity-mcp-workflow",
                "Unity MCP Workflow",
                "Efficient workflow for using Unity MCP to edit, import, compile, inspect, and test Unity projects.",
                true,
                "Use this skill when Codex or another AI agent is working in a Unity project and needs to verify code, prefabs, UI, Play Mode behavior, screenshots, scene hierarchy, console logs, domain reloads, or MCP connection issues.",
                new[]
                {
                    "Use Unity MCP as the source of truth for Editor state, scene hierarchy, prefab references, runtime objects, compilation status, and Play Mode behavior.",
                    "Locate the real Unity project root and active scene before editing.",
                    "Inspect hierarchy, prefab paths, selected objects, and relevant component references through MCP before changing user-named objects.",
                    "Tool returns are structured JSON: `{success, message, data}` for success and `{success: false, code, error, data}` for errors. Parse `data` and check `code` (UPPERCASE_SNAKE_CASE) for branching — do not pattern-match free-form text.",
                    "Prefer `instanceId` returned by tools for follow-up calls. Pass it back via the `find_method=by_id` (or auto-detect when the value parses as an integer). This is more reliable than re-resolving by `name` when scenes contain duplicates.",
                    "Use the `find_method` parameter on GameObject/Component tools to choose how a target is resolved: `by_id`, `by_name`, `by_path`, `by_tag`, `by_layer`, `by_component`, or `by_id_or_name_or_path`. Default auto-detect picks `by_id` for integers, `by_path` for slashed strings, otherwise `by_name`.",
                    "When a GameObject has multiple components of the same type, target a specific one with `component_instance_id` instead of the type name to avoid hitting the wrong component.",
                    "Set component fields with `set_component_property(ies)`: it now writes through SerializedObject, so `[SerializeField] private` fields are reachable. Pass Object references as JSON `{\"fileID\": <instanceId>}` (preferred) or `{\"assetPath\": \"Assets/...\"}`. The response reports per-field success/failure.",
                    "Inspect editor-level state through dedicated tools: `get_selection`, `set_selection`, `get_prefab_stage`, `get_active_tool`, `get_windows`, `get_tags`, `get_layers`, `get_build_settings`. Do not write `execute_code` snippets just to read this.",
                    "When no specialized MCP tool covers an editor action, try `execute_menu_item` (e.g. 'GameObject/2D Object/Sprite', 'Window/Layouts/Default', 'Edit/Project Settings...') before falling back to `execute_code`.",
                    "When Tool Exposure uses the default `core` profile, rely on the focused workflow tools: `execute_code`, recompilation, Play Mode control, hierarchy, console logs, screenshots, input simulation, and performance inspection.",
                    "When Tool Exposure uses the default `full` profile, all registered MCP tools are available. Prefer specific tools for simple scene, asset, GameObject, component, prefab, camera, UI, package, animation, file, or visual-feedback operations.",
                    "If Tool Exposure has been customized and a named tool is unavailable, adapt to the exposed tool list and report which expected tool is missing.",
                    "Choose the correct edit surface: source files with normal repo tools, scene objects through Unity APIs and saved scenes, prefab assets through `PrefabUtility.LoadPrefabContents` and `SaveAsPrefabAsset`.",
                    "For `execute_code`, prefer the IFunplayCommand template over the legacy `static string Run()`: implement `IFunplayCommand` and use `ctx.RegisterObjectCreation`, `ctx.RegisterObjectModification`, `ctx.DestroyObject` so created/modified objects participate in editor Undo automatically. Use `ctx.Log` / `ctx.LogWarning` / `ctx.LogError` for traceable output that comes back in the response (without polluting the Unity console).",
                    "Batch related Unity-side changes in one guarded `execute_code` snippet, with explicit missing-object reports and concise before/after values.",
                    "`execute_code` now refreshes the asset database and waits for compilation to finish before compiling the snippet, so external file edits are picked up automatically. For other tools that depend on the latest assemblies (e.g. `get_compilation_errors`), still call `request_recompile` after external file edits.",
                    "Call `wait_for_compilation` before Play Mode, screenshots, or conclusions when a previous edit has not yet been confirmed.",
                    "After `enter_play_mode`, the MCP HTTP server briefly drops while Unity reloads the domain. Before the next tool call, poll a cheap tool such as `tools/list` or `get_reload_recovery_status` until the server responds again — do not assume the connection is immediately ready.",
                    "`request_recompile` is rejected while Unity is in Play Mode — Unity does not process script compilation or domain reloads while playing. Call `exit_play_mode` first, then retry `request_recompile`.",
                    "Read back exact values from Unity after changes, not only success messages.",
                    "Test actual behavior in Unity through hierarchy, console logs, Play Mode, UI interactions, screenshots, or targeted `execute_code` checks.",
                    "When Unity readback and text files disagree for serialized scene or prefab state, trust Unity readback and investigate the asset path.",
                    "If Play Mode is entered, exit Play Mode before finishing unless the user explicitly wants it left running."
                }),
        };

        internal static IReadOnlyList<SkillDefinition> GetBuiltInSkills()
        {
            return SkillCatalog.Where(skill => skill.IsBuiltIn).ToArray();
        }

        internal static IReadOnlyList<SkillDefinition> GetOptionalSkills()
        {
            return SkillCatalog.Where(skill => !skill.IsBuiltIn).ToArray();
        }

        internal static IReadOnlyList<string> GetSupportedPlatforms()
        {
            return SupportedPlatforms;
        }

        internal static ProjectSkillsManifest LoadManifest(string projectRoot)
        {
            var manifestPath = GetManifestPath(projectRoot);
            try
            {
                if (File.Exists(manifestPath))
                {
                    var json = File.ReadAllText(manifestPath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var loaded = JsonUtility.FromJson<ProjectSkillsManifest>(json);
                        if (loaded != null)
                            return NormalizeManifest(loaded);
                    }
                }
            }
            catch
            {
            }

            return CreateDefaultManifest();
        }

        internal static void SaveManifest(string projectRoot, ProjectSkillsManifest manifest)
        {
            var normalized = NormalizeManifest(manifest);
            var manifestPath = GetManifestPath(projectRoot);
            var directory = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(manifestPath, JsonUtility.ToJson(normalized, true));
        }

        internal static string GetManifestPath(string projectRoot)
        {
            return Path.Combine(projectRoot, ManifestDirectory, ManifestFileName);
        }

        internal static string GetCodexAgentsPath(string projectRoot)
        {
            return Path.Combine(projectRoot, "AGENTS.md");
        }

        internal static string GetClaudeInstructionsPath(string projectRoot)
        {
            return Path.Combine(projectRoot, "CLAUDE.md");
        }

        internal static string GetCursorRulesPath(string projectRoot)
        {
            return Path.Combine(projectRoot, ".cursor", "rules");
        }

        internal static string GetCodexSkillsRoot(string projectRoot)
        {
            return Path.Combine(projectRoot, ".codex", "skills");
        }

        internal static string GetClaudeSkillsRoot(string projectRoot)
        {
            return Path.Combine(projectRoot, ".claude", "skills");
        }

        internal static void ApplyConfiguration(string projectRoot, IEnumerable<string> selectedPlatforms, IEnumerable<string> selectedOptionalSkills)
        {
            var manifest = new ProjectSkillsManifest
            {
                platforms = selectedPlatforms?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
                optionalSkills = selectedOptionalSkills?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>()
            };

            SaveManifest(projectRoot, manifest);

            var normalized = LoadManifest(projectRoot);
            SyncCodex(projectRoot, normalized);
            SyncClaude(projectRoot, normalized);
            SyncCursor(projectRoot, normalized);
        }

        internal static bool IsPlatformConfigured(string projectRoot, string platformId)
        {
            var manifest = LoadManifest(projectRoot);
            return manifest.platforms.Contains(platformId, StringComparer.OrdinalIgnoreCase);
        }

        internal static IReadOnlyList<SkillDefinition> GetInstalledSkills(ProjectSkillsManifest manifest)
        {
            var installedIds = new HashSet<string>(
                GetBuiltInSkills().Select(skill => skill.Id),
                StringComparer.OrdinalIgnoreCase);

            if (manifest?.optionalSkills != null)
            {
                foreach (var id in manifest.optionalSkills)
                    installedIds.Add(id);
            }

            return SkillCatalog.Where(skill => installedIds.Contains(skill.Id)).ToArray();
        }

        internal static bool IsManagedFile(string path)
        {
            try
            {
                return File.Exists(path) && File.ReadAllText(path).Contains(ManagedMarker, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        internal static string[] GetPlatformConflictPaths(string projectRoot, IEnumerable<string> selectedPlatforms)
        {
            var conflicts = new List<string>();
            var platforms = new HashSet<string>(selectedPlatforms ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            if (platforms.Contains("codex"))
            {
                var path = GetCodexAgentsPath(projectRoot);
                if (File.Exists(path) && !IsManagedFile(path))
                    conflicts.Add(path);
            }

            if (platforms.Contains("claude"))
            {
                var path = GetClaudeInstructionsPath(projectRoot);
                if (File.Exists(path) && !IsManagedFile(path))
                    conflicts.Add(path);
            }

            return conflicts.ToArray();
        }

        internal static IReadOnlyList<string> GetGeneratedPathsForPlatform(string projectRoot, ProjectSkillsManifest manifest, string platformId)
        {
            var enabled = manifest != null && manifest.platforms.Contains(platformId, StringComparer.OrdinalIgnoreCase);
            if (!enabled)
                return Array.Empty<string>();

            var paths = new List<string> { GetManifestPath(projectRoot) };

            switch (platformId?.Trim().ToLowerInvariant())
            {
                case "codex":
                    paths.Add(GetCodexAgentsPath(projectRoot));
                    paths.Add(GetCodexSkillsRoot(projectRoot));
                    break;
                case "claude":
                    paths.Add(GetClaudeInstructionsPath(projectRoot));
                    paths.Add(GetClaudeSkillsRoot(projectRoot));
                    break;
                case "cursor":
                    paths.Add(GetCursorRulesPath(projectRoot));
                    break;
            }

            return paths;
        }

        private static void SyncCodex(string projectRoot, ProjectSkillsManifest manifest)
        {
            var enabled = manifest.platforms.Contains("codex", StringComparer.OrdinalIgnoreCase);
            var agentsPath = GetCodexAgentsPath(projectRoot);
            var skillsRoot = GetCodexSkillsRoot(projectRoot);

            if (!enabled)
            {
                DeleteManagedFile(agentsPath);
                DeleteManagedSkillDirectories(skillsRoot);
                return;
            }

            Directory.CreateDirectory(skillsRoot);

            File.WriteAllText(agentsPath, BuildCodexAgentsContent(projectRoot, manifest));
            WriteManagedSkillDirectories(skillsRoot, manifest, SkillPlatform.Codex);
        }

        private static void SyncClaude(string projectRoot, ProjectSkillsManifest manifest)
        {
            var enabled = manifest.platforms.Contains("claude", StringComparer.OrdinalIgnoreCase);
            var claudePath = GetClaudeInstructionsPath(projectRoot);
            var skillsRoot = GetClaudeSkillsRoot(projectRoot);

            if (!enabled)
            {
                DeleteManagedFile(claudePath);
                DeleteManagedSkillDirectories(skillsRoot);
                return;
            }

            Directory.CreateDirectory(skillsRoot);

            File.WriteAllText(claudePath, BuildClaudeInstructionsContent(projectRoot, manifest));
            WriteManagedSkillDirectories(skillsRoot, manifest, SkillPlatform.Claude);
        }

        private static void SyncCursor(string projectRoot, ProjectSkillsManifest manifest)
        {
            var enabled = manifest.platforms.Contains("cursor", StringComparer.OrdinalIgnoreCase);
            var rulesRoot = GetCursorRulesPath(projectRoot);

            if (!enabled)
            {
                DeleteManagedCursorRules(rulesRoot);
                return;
            }

            Directory.CreateDirectory(rulesRoot);
            WriteManagedCursorRules(rulesRoot, manifest);
        }

        private static void WriteManagedSkillDirectories(string skillsRoot, ProjectSkillsManifest manifest, SkillPlatform platform)
        {
            DeleteManagedSkillDirectories(skillsRoot);

            foreach (var skill in GetInstalledSkills(manifest))
            {
                var directory = Path.Combine(skillsRoot, $"funplay-{skill.Id}");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "SKILL.md"), BuildSkillDocument(skill, platform));
            }
        }

        private static void WriteManagedCursorRules(string rulesRoot, ProjectSkillsManifest manifest)
        {
            DeleteManagedCursorRules(rulesRoot);

            foreach (var skill in GetInstalledSkills(manifest))
            {
                var path = Path.Combine(rulesRoot, $"funplay-{skill.Id}.mdc");
                File.WriteAllText(path, BuildCursorRuleContent(skill));
            }
        }

        private static void DeleteManagedSkillDirectories(string skillsRoot)
        {
            if (!Directory.Exists(skillsRoot))
                return;

            foreach (var directory in Directory.GetDirectories(skillsRoot, "funplay-*", SearchOption.TopDirectoryOnly))
            {
                var skillPath = Path.Combine(directory, "SKILL.md");
                if (IsManagedFile(skillPath))
                    Directory.Delete(directory, true);
            }
        }

        private static void DeleteManagedCursorRules(string rulesRoot)
        {
            if (!Directory.Exists(rulesRoot))
                return;

            foreach (var file in Directory.GetFiles(rulesRoot, "funplay-*.mdc", SearchOption.TopDirectoryOnly))
            {
                if (IsManagedFile(file))
                    File.Delete(file);
            }
        }

        private static void DeleteManagedFile(string path)
        {
            if (IsManagedFile(path))
                File.Delete(path);
        }

        private static string BuildCodexAgentsContent(string projectRoot, ProjectSkillsManifest manifest)
        {
            var installed = GetInstalledSkills(manifest);
            return
$@"# AGENTS.md
{ManagedMarker}

# Funplay Unity MCP Project Guidance

This file is managed by Funplay MCP for Unity.

## Installed project skills

{string.Join("\n", installed.Select(skill => $"- `funplay-{skill.Id}` - {skill.Description}"))}

## Codex workflow rules

- Prefer project-local Funplay skills under `.codex/skills/`.
- Use `execute_code` as the primary Unity automation tool. For new snippets, implement `IFunplayCommand` and use `ctx.RegisterObjectCreation` / `RegisterObjectModification` / `DestroyObject` so changes participate in Undo automatically.
- Inspect Unity objects through MCP before changing user-named scene or prefab targets. Carry the returned `instanceId` into follow-up calls (`find_method=by_id`) instead of re-resolving by name.
- Tool returns are structured JSON (`{{success, message, data}}` / `{{success: false, code, error, data}}`). Branch on `code`, not free-form text.
- Set component fields with `set_component_property(ies)` — it picks up `[SerializeField] private` fields and accepts Object references as `{{""fileID"": <instanceId>}}` or `{{""assetPath"": ""Assets/...""}}`.
- Read editor state through dedicated tools (`get_selection`, `get_prefab_stage`, `get_tags`, `get_layers`, `get_build_settings`); use `execute_menu_item` before falling back to ad-hoc `execute_code`.
- Save only the scene or prefab assets intentionally modified, then read back exact values.
- With default `core` exposure, use the focused workflow tools. With default `full` exposure, prefer specific MCP tools for simple editor operations.
- `execute_code` refreshes the asset database and waits for compilation before running. For other tools that depend on freshly compiled code, still call `request_recompile` after external script edits.
- `request_recompile` is rejected while Unity is in Play Mode. Call `exit_play_mode` first, then retry.
- After `enter_play_mode`, the HTTP server briefly drops while Unity reloads the domain. Poll `tools/list` or `get_reload_recovery_status` until it responds again before issuing the next tool call.
- If recompilation triggers a domain reload, call `get_reload_recovery_status`.
- Avoid changing `Library/`, `Temp/`, `Logs/`, or `obj/`.

## Project

- Project root: `{projectRoot}`
- Product name: `{Application.productName}`

## Notes

- Re-run `Funplay > Project Skills` after changing selected skills or platforms.
";
        }

        private static string BuildClaudeInstructionsContent(string projectRoot, ProjectSkillsManifest manifest)
        {
            var installed = GetInstalledSkills(manifest);
            return
$@"# CLAUDE.md
{ManagedMarker}

# Funplay Unity MCP Project Guidance

This file is managed by Funplay MCP for Unity for Claude Code.

## Installed skills

{string.Join("\n", installed.Select(skill => $"- `{skill.Id}` - {skill.Description}"))}

## Preferred workflow

- Use Funplay MCP tools for Unity editor state and automation.
- Use `execute_code` for non-trivial Unity orchestration. For new snippets, implement `IFunplayCommand` and use `ctx.RegisterObjectCreation` / `RegisterObjectModification` / `DestroyObject` so changes participate in Undo and `ctx.Log` for traceable output.
- Inspect Unity objects through MCP before changing user-named scene or prefab targets. Carry the returned `instanceId` into follow-up calls (`find_method=by_id`) instead of re-resolving by name.
- Tool returns are structured JSON (`{{success, message, data}}` / `{{success: false, code, error, data}}`). Branch on `code`, not free-form text.
- Set component fields with `set_component_property(ies)` — it picks up `[SerializeField] private` fields and accepts Object references as `{{""fileID"": <instanceId>}}` or `{{""assetPath"": ""Assets/...""}}`.
- Read editor state through `get_selection`, `get_prefab_stage`, `get_tags`, `get_layers`, `get_build_settings`; try `execute_menu_item` before writing ad-hoc `execute_code`.
- Save only the scene or prefab assets intentionally modified, then read back exact values.
- With default `core` exposure, use the focused workflow tools. With default `full` exposure, prefer specific MCP tools for simple editor operations.
- `execute_code` refreshes assets and waits for compilation before running. For other tools that depend on freshly compiled code, still call `request_recompile` after external script edits.
- `request_recompile` is rejected while Unity is in Play Mode. Call `exit_play_mode` first, then retry.
- After `enter_play_mode`, the HTTP server briefly drops while Unity reloads the domain. Poll `tools/list` or `get_reload_recovery_status` until it responds again before issuing the next tool call.
- If domain reload interrupts a request, follow with `get_reload_recovery_status`.
- Additional installed skills are available under `.claude/skills/`.

## Project

- Project root: `{projectRoot}`
- Product name: `{Application.productName}`
";
        }

        private static string BuildCursorRuleContent(SkillDefinition skill)
        {
            var alwaysApply = skill.IsBuiltIn ? "true" : "false";
            return
$@"---
description: {skill.Description}
alwaysApply: {alwaysApply}
---
{ManagedMarker}

# {skill.Title}

{skill.WhenToUse}

## Rules

{string.Join("\n", skill.Rules.Select(rule => $"- {rule}"))}

## Metadata

- Skill id: `{skill.Id}`
- Built-in: `{skill.IsBuiltIn}`
- Source: `https://github.com/FunplayAI/funplay-unity-mcp`
";
        }

        private static string BuildSkillDocument(SkillDefinition skill, SkillPlatform platform)
        {
            if (string.Equals(skill.Id, "unity-mcp-workflow", StringComparison.OrdinalIgnoreCase))
                return BuildUnityMcpWorkflowSkillDocument(skill, platform);

            return
$@"---
name: funplay-{skill.Id}
description: {skill.Description}
platform: {platform.ToString().ToLowerInvariant()}
---
{ManagedMarker}

# {skill.Title}

{skill.WhenToUse}

## Rules

{string.Join("\n", skill.Rules.Select(rule => $"- {rule}"))}

## Metadata

- Original skill id: `{skill.Id}`
- Source repository: `https://github.com/FunplayAI/funplay-unity-mcp`
";
        }

        private static string BuildUnityMcpWorkflowSkillDocument(SkillDefinition skill, SkillPlatform platform)
        {
            var header =
$@"---
name: funplay-{skill.Id}
description: {skill.Description}
platform: {platform.ToString().ToLowerInvariant()}
---
{ManagedMarker}

# {skill.Title}

{skill.WhenToUse}
";

            var body =
@"
## Operating Loop

1. Establish context.
   - Confirm the Unity project root and active scene.
   - Check that Unity MCP is reachable before assuming Editor state.
   - Inspect hierarchy, prefab paths, selected objects, and relevant component references through MCP.
   - If the user names an object, verify the real Unity object path before editing.
2. Choose the edit surface.
   - Edit source files with normal repo tools, then trigger Unity recompilation.
   - Edit scene objects through Unity APIs, mark the scene dirty, and save the scene.
   - Edit prefab assets with `PrefabUtility.LoadPrefabContents`, `PrefabUtility.SaveAsPrefabAsset`, and `PrefabUtility.UnloadPrefabContents`.
   - If the user is looking at an open scene instance, update the visible scene instance as well as the prefab asset when appropriate.
3. Execute changes.
   - Prefer one well-guarded `execute_code` batch over many fragile UI clicks.
   - Use null guards for every object lookup and return explicit missing-path messages.
   - Return concise before/after values from snippets.
   - Save only the assets or scenes intentionally modified.
4. Validate.
   - Read back the changed objects through MCP.
   - For file edits, call `request_recompile`, then `wait_for_compilation`, then inspect console or compilation errors.
   - For runtime behavior, enter Play Mode or inspect live objects when needed.
   - Report exactly what was verified and what still requires device, store, network, or manual validation.

## Tool Exposure

- With the default `core` profile, rely on the focused workflow tools: `execute_code`, recompilation, Play Mode control, hierarchy, console logs, screenshots, input simulation, and performance inspection.
- With the default `full` profile, prefer specific MCP tools for simple scene, asset, GameObject, component, prefab, camera, UI, package, animation, file, or visual-feedback operations.
- If Tool Exposure is customized and a named tool is unavailable, adapt to the exposed tool list and report which expected tool is missing.

## MCP Call Pattern

If native MCP tools are not directly available, probe the local HTTP endpoint:

```bash
curl -sS -m 1 -X POST http://127.0.0.1:8765/mcp \
  -H 'Content-Type: application/json' \
  -d '{""jsonrpc"":""2.0"",""id"":1,""method"":""tools/list""}'
```

For multi-line `execute_code` calls over curl, generate JSON with a real encoder instead of hand-escaping C#:

```bash
node - <<'NODE'
const code = String.raw`
using UnityEngine;

public class InspectSomething
{
    public static string Run()
    {
        var obj = GameObject.Find(""PracticeInGameUiRoot"");
        return obj != null ? obj.name : ""not found"";
    }
}
`;
const payload = {
  jsonrpc: ""2.0"",
  id: 1,
  method: ""tools/call"",
  params: { name: ""execute_code"", arguments: { code } }
};
process.stdout.write(JSON.stringify(payload));
NODE
```

## Unity C# Patterns

Add explicit `using` directives or use fully qualified types for project code. `execute_code` does not auto-inject project namespaces by default:

```csharp
var root = UnityEngine.GameObject.Find(""PracticeInGameUiRoot"");
var rect = root.GetComponent<UnityEngine.RectTransform>();
```

Use Unity null semantics for `UnityEngine.Object` references:

```csharp
if (image == null)
{
    return ""Image missing"";
}
```

For prefab edits:

```csharp
var path = ""Assets/MyGame/UI/Prefabs/PF_PracticeInGameUiRoot.prefab"";
var prefab = UnityEditor.PrefabUtility.LoadPrefabContents(path);
try
{
    var target = prefab.transform.Find(""SafeArea/SwingCancelZone"");
    if (target == null)
    {
        return ""SwingCancelZone not found in prefab"";
    }

    var rect = target.GetComponent<UnityEngine.RectTransform>();
    var before = rect.anchoredPosition;
    rect.anchoredPosition = new UnityEngine.Vector2(-76f, 448f);

    UnityEditor.EditorUtility.SetDirty(rect);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(prefab, path);
    UnityEditor.AssetDatabase.SaveAssets();
    return ""Prefab saved: pos "" + before + "" -> "" + rect.anchoredPosition;
}
finally
{
    UnityEditor.PrefabUtility.UnloadPrefabContents(prefab);
}
```

For scene edits:

```csharp
var obj = UnityEngine.GameObject.Find(""PracticeInGameUiRoot/SafeArea/SwingCancelZone"");
if (obj == null)
{
    return ""Scene object not found"";
}

var rect = obj.GetComponent<UnityEngine.RectTransform>();
var before = rect.sizeDelta;
UnityEditor.Undo.RecordObject(rect, ""Update cancel zone"");
rect.sizeDelta = new UnityEngine.Vector2(220f, 116f);
UnityEditor.EditorUtility.SetDirty(rect);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(obj.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(obj.scene);
return ""Scene saved: size "" + before + "" -> "" + rect.sizeDelta;
```

## Recompile And Reload

After external C# or asset file edits:

1. If Unity is in Play Mode, call `exit_play_mode` first — `request_recompile` is rejected during play because Unity does not run script compilation or domain reloads while playing.
2. Call `request_recompile`.
3. Call `wait_for_compilation`.
4. Read console or compilation errors before continuing.
5. If a domain reload drops the request, call `get_reload_recovery_status` when available, re-scan the MCP endpoint if needed, then continue from `wait_for_compilation`.

Do not treat a disconnected request as a successful compile.

After `enter_play_mode`, the HTTP server is briefly unreachable while Unity reloads the domain. Before issuing the next tool call, poll a cheap endpoint such as `tools/list` (or `get_reload_recovery_status` if exposed) until you get a response — do not assume the connection survives the Play Mode transition.

## Verification Checklist

Use readback snippets that print exact values, not only `success`:

```csharp
var all = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Transform>();
UnityEngine.Transform target = null;
for (int i = 0; i < all.Length; i++)
{
    if (all[i].name == ""SwingCancelZone"")
    {
        target = all[i];
        break;
    }
}

if (target == null)
{
    return ""SwingCancelZone not found"";
}

var rect = target.GetComponent<UnityEngine.RectTransform>();
return ""path="" + target.name + ""; pos="" + rect.anchoredPosition + ""; size="" + rect.sizeDelta;
```

For UI work, verify prefab or scene hierarchy, sprite references, anchors, sorting order, active state, text fit, and button listeners. A populated `Content` hierarchy does not prove the user can see the UI.

For gameplay or network work, verify object identity, ownership, live instance existence, transform values, animation state, visibility, and whether client-side filters are discarding valid data.

## Failure Handling

- If MCP is unreachable, say so and fall back only to safe filesystem inspection or code edits. Do not claim scene, prefab, or runtime verification without Unity readback.
- If an object lookup fails, inspect hierarchy and prefab contents instead of inventing a path.
- If multiple matching objects exist, print their paths and choose the one matching the user-visible UI or current scene.
- If compile errors appear after a change, fix them before Play Mode validation.
- When Unity and text files disagree for serialized scene or prefab state, trust Unity readback and inspect the asset path.
";

            var footer =
$@"
## Metadata

- Original skill id: `{skill.Id}`
- Source repository: `https://github.com/FunplayAI/funplay-unity-mcp`
";

            return header + body + footer;
        }

        private static ProjectSkillsManifest CreateDefaultManifest()
        {
            return new ProjectSkillsManifest
            {
                platforms = new List<string>(),
                optionalSkills = new List<string>()
            };
        }

        private static ProjectSkillsManifest NormalizeManifest(ProjectSkillsManifest manifest)
        {
            manifest ??= CreateDefaultManifest();
            manifest.platforms ??= new List<string>();
            manifest.optionalSkills ??= new List<string>();

            manifest.platforms = manifest.platforms
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().ToLowerInvariant())
                .Where(value => SupportedPlatforms.Contains(value, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var optionalIds = new HashSet<string>(
                GetOptionalSkills().Select(skill => skill.Id),
                StringComparer.OrdinalIgnoreCase);

            manifest.optionalSkills = manifest.optionalSkills
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Where(value => optionalIds.Contains(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return manifest;
        }

        internal enum SkillPlatform
        {
            Codex,
            Claude,
            Cursor
        }

        [Serializable]
        internal sealed class ProjectSkillsManifest
        {
            public List<string> platforms = new List<string>();
            public List<string> optionalSkills = new List<string>();
        }

        internal sealed class SkillDefinition
        {
            public SkillDefinition(string id, string title, string description, bool isBuiltIn, string whenToUse, IReadOnlyList<string> rules)
            {
                Id = id;
                Title = title;
                Description = description;
                IsBuiltIn = isBuiltIn;
                WhenToUse = whenToUse;
                Rules = rules ?? Array.Empty<string>();
            }

            public string Id { get; }
            public string Title { get; }
            public string Description { get; }
            public bool IsBuiltIn { get; }
            public string WhenToUse { get; }
            public IReadOnlyList<string> Rules { get; }
        }
    }
}
