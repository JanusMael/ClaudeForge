global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Bennewitz.Ninja.ClaudeForge.Core.Settings;
global using Bennewitz.Ninja.ClaudeForge.Core.Schema;
global using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;
global using System.Text.Json.Nodes;
// HookCommandType was merged into the SDK; this alias keeps every test file that
// referenced the former editor-local enum resolving to the single SDK type.
global using HookCommandType = Bennewitz.Ninja.ClaudeForge.Sdk.Hooks.HookCommandType;