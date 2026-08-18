global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Bennewitz.Ninja.AgentForge.Core.Settings;
global using Bennewitz.Ninja.AgentForge.Core.Schema;
// ClaudeMergePolicy: every workspace these tests build is a Claude workspace, so the
// policy argument appears in ~20 files. Global, for the same reason ConfigScope is.
global using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;
global using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;
global using System.Text.Json.Nodes;
// HookCommandType was merged into the SDK; this alias keeps every test file that
// referenced the former editor-local enum resolving to the single SDK type.
global using HookCommandType = Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Hooks.HookCommandType;