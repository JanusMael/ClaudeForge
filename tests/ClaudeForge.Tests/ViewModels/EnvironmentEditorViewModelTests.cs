using System.Collections;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

// ---------------------------------------------------------------------------
// Fake IEnvironmentProvider for deterministic, side-effect-free tests.
// ---------------------------------------------------------------------------

internal sealed class FakeEnvironmentProvider : IEnvironmentProvider
{
    public Dictionary<string, string> Machine { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> User { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Process { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IDictionary GetVariables(EnvironmentVariableTarget target)
    {
        return target switch
        {
            EnvironmentVariableTarget.Machine => Machine,
            EnvironmentVariableTarget.User => User,
            EnvironmentVariableTarget.Process => Process,
            var _ => new Dictionary<string, string>(),
        };
    }

    public void SetVariable(string name, string? value, EnvironmentVariableTarget target)
    {
        Dictionary<string, string>? dict = target switch
        {
            EnvironmentVariableTarget.Machine => Machine,
            EnvironmentVariableTarget.User => User,
            EnvironmentVariableTarget.Process => Process,
            var _ => null,
        };
        if (dict == null)
        {
            return;
        }

        if (value == null)
        {
            dict.Remove(name);
        }
        else
        {
            dict[name] = value;
        }
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public class EnvironmentEditorViewModelTests
{
    private static SettingsWorkspace MakeWorkspace(string userJson = "{}")
    {
        JsonObject root = (JsonObject)JsonNode.Parse(userJson)!;
        SettingsDocument doc = new(ConfigScope.User, "user.json", root, isReadOnly: false);
        return new SettingsWorkspace([doc]);
    }

    /// <summary>
    /// Wrap an in-memory <see cref="SettingsWorkspace"/> in a ClaudeCodeClient
    /// via the internal <c>FromExistingWorkspace</c> overload. Avoids disk I/O
    /// while exercising the SDK-backed Environment editor migrated in
    /// 4.3.7 step 11. The grant lives in <c>ClaudeForge.Sdk.csproj</c>'s
    /// InternalsVisibleTo for ClaudeForge.Tests.
    /// </summary>
    private static ClaudeConfigClientCore MakeClient(string userJson = "{}")
    {
        SettingsWorkspace ws = MakeWorkspace(userJson);
        return ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
    }

    // -----------------------------------------------------------------------
    // Refresh / AllEntries
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Refresh_PopulatesEntriesFromAllLayers()
    {
        FakeEnvironmentProvider provider = new();
        provider.Machine["MACHINE_VAR"] = "machine-val";
        provider.User["USER_VAR"] = "user-val";
        provider.Process["PATH"] = "/usr/bin";

        EnvironmentEditorViewModel vm = new(provider, null);

        HashSet<string> names = vm.AllEntries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.IsTrue(names.Contains("MACHINE_VAR"), "MACHINE_VAR expected");
        Assert.IsTrue(names.Contains("USER_VAR"), "USER_VAR expected");
        Assert.IsTrue(names.Contains("PATH"), "PATH expected");
    }

    [TestMethod]
    public void Refresh_MergesClaudeEnvFromWorkspace()
    {
        FakeEnvironmentProvider provider = new();
        ClaudeConfigClientCore client = MakeClient("""{"env":{"ANTHROPIC_API_KEY":"sk-test"}}""");

        EnvironmentEditorViewModel vm = new(provider, client);

        EnvVarEntry? entry = vm.AllEntries.FirstOrDefault(e =>
            string.Equals(e.Name, "ANTHROPIC_API_KEY", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(entry);
        Assert.AreEqual("sk-test", entry.ClaudeValue);
    }

    // -----------------------------------------------------------------------
    // Priority: Process > Claude > User > Machine
    // -----------------------------------------------------------------------

    [TestMethod]
    public void EffectiveValue_ProcessWinsOverAll()
    {
        FakeEnvironmentProvider provider = new();
        provider.Machine["PATH"] = "machine-path";
        provider.User["PATH"] = "user-path";
        provider.Process["PATH"] = "process-path";

        ClaudeConfigClientCore client = MakeClient("""{"env":{"PATH":"claude-path"}}""");
        EnvironmentEditorViewModel vm = new(provider, client);

        EnvVarEntry entry = vm.AllEntries.First(e => e.Name.Equals("PATH", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("process-path", entry.EffectiveValue);
        Assert.AreEqual("Process", entry.EffectiveSource);
    }

    [TestMethod]
    public void EffectiveValue_ClaudeWinsOverUserAndMachine()
    {
        FakeEnvironmentProvider provider = new();
        provider.Machine["MY_VAR"] = "machine-val";
        provider.User["MY_VAR"] = "user-val";

        ClaudeConfigClientCore client = MakeClient("""{"env":{"MY_VAR":"claude-val"}}""");
        EnvironmentEditorViewModel vm = new(provider, client);

        EnvVarEntry entry = vm.AllEntries.First(e => e.Name.Equals("MY_VAR", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("claude-val", entry.EffectiveValue);
        Assert.AreEqual("Claude", entry.EffectiveSource);
    }

    // -----------------------------------------------------------------------
    // IsOverridden
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IsOverridden_TrueWhenMultipleLayersDefineVar()
    {
        FakeEnvironmentProvider provider = new();
        provider.Machine["CLAUDE_MODEL"] = "m1";
        provider.User["CLAUDE_MODEL"] = "m2";

        EnvironmentEditorViewModel vm = new(provider, null);
        EnvVarEntry entry = vm.AllEntries.First(e => e.Name.Equals("CLAUDE_MODEL", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(entry.IsOverridden);
    }

    [TestMethod]
    public void IsOverridden_FalseWhenOnlyOneLayer()
    {
        FakeEnvironmentProvider provider = new();
        provider.Process["UNIQUE_VAR"] = "only-here";

        EnvironmentEditorViewModel vm = new(provider, null);
        EnvVarEntry entry = vm.AllEntries.First(e => e.Name.Equals("UNIQUE_VAR", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(entry.IsOverridden);
    }

    // -----------------------------------------------------------------------
    // FilteredEntries — allowlist + ShowAll + text filter
    // -----------------------------------------------------------------------

    [TestMethod]
    public void FilteredEntries_AllowlistFiltersOutObscureVars()
    {
        FakeEnvironmentProvider provider = new();
        provider.Process["PATH"] = "/usr/bin";
        provider.Process["OBSCURE_ZZZZ"] = "hidden";

        EnvironmentEditorViewModel vm = new(provider, null);
        // ShowAll is false by default
        List<string> names = vm.FilteredEntries.Select(e => e.Name).ToList();

        Assert.IsTrue(names.Any(n => n.Equals("PATH", StringComparison.OrdinalIgnoreCase)),
            "PATH should be visible in allowlist mode");
        Assert.IsFalse(names.Any(n => n.Equals("OBSCURE_ZZZZ", StringComparison.OrdinalIgnoreCase)),
            "OBSCURE_ZZZZ should be hidden in allowlist mode");
    }

    [TestMethod]
    public void FilteredEntries_ShowAllExposesEverything()
    {
        FakeEnvironmentProvider provider = new();
        provider.Process["OBSCURE_ZZZZ"] = "visible-now";

        EnvironmentEditorViewModel vm = new(provider, null) { ShowAll = true };

        List<string> names = vm.FilteredEntries.Select(e => e.Name).ToList();
        Assert.IsTrue(names.Any(n => n.Equals("OBSCURE_ZZZZ", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void FilteredEntries_TextFilterNarrowsResults()
    {
        FakeEnvironmentProvider provider = new();
        provider.Process["CLAUDE_MODEL"] = "sonnet";
        provider.Process["ANTHROPIC_KEY"] = "sk-abc";

        EnvironmentEditorViewModel vm = new(provider, null)
        {
            ShowAll = true,
            FilterText = "CLAUDE",
        };

        List<string> names = vm.FilteredEntries.Select(e => e.Name).ToList();
        Assert.IsTrue(names.Any(n => n.Contains("CLAUDE", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(names.Any(n => n.Equals("ANTHROPIC_KEY", StringComparison.OrdinalIgnoreCase)));
    }

    // -----------------------------------------------------------------------
    // SaveEdit → writes to Claude workspace
    // -----------------------------------------------------------------------

    [TestMethod]
    public void SaveEdit_ClaudeScope_WritesToWorkspace()
    {
        FakeEnvironmentProvider provider = new();
        provider.Process["PATH"] = "/usr/bin"; // so PATH is in AllEntries
        ClaudeConfigClientCore client = MakeClient();
        EnvironmentEditorViewModel vm = new(provider, client);

        vm.EditingScope = EnvEditScope.Claude;
        vm.SelectedEntry = vm.AllEntries.First(e =>
            e.Name.Equals("PATH", StringComparison.OrdinalIgnoreCase));
        vm.EditValue = "/custom/bin";

        vm.SaveEditCommand.Execute(null);

        // Workspace should now have env.PATH = "/custom/bin" in the User doc
        LayeredValue layered = client.GetLayeredValueSnapshot("env");
        JsonObject? envObj = layered.GetValueAt(ConfigScope.User) as JsonObject;
        Assert.IsNotNull(envObj);
        Assert.AreEqual("/custom/bin", envObj["PATH"]?.GetValue<string>());
    }

    // -----------------------------------------------------------------------
    // RemoveFromScope → clears the variable from Claude env
    // -----------------------------------------------------------------------

    [TestMethod]
    public void RemoveFromScope_ClaudeScope_RemovesFromWorkspace()
    {
        FakeEnvironmentProvider provider = new();
        ClaudeConfigClientCore client = MakeClient("""{"env":{"MY_KEY":"existing-val"}}""");
        EnvironmentEditorViewModel vm = new(provider, client);

        vm.EditingScope = EnvEditScope.Claude;
        vm.SelectedEntry = vm.AllEntries.First(e =>
            e.Name.Equals("MY_KEY", StringComparison.OrdinalIgnoreCase));

        vm.RemoveFromScopeCommand.Execute(null);

        LayeredValue layered = client.GetLayeredValueSnapshot("env");
        // env object at user scope should be empty or gone
        JsonObject? envObj = layered.GetValueAt(ConfigScope.User) as JsonObject;
        Assert.IsTrue(envObj == null || !envObj.ContainsKey("MY_KEY"));
    }

    // -----------------------------------------------------------------------
    // AddNew — adds entry and selects it
    // -----------------------------------------------------------------------

    [TestMethod]
    public void AddNew_ClaudeScope_AddsEntryAndSelectsIt()
    {
        FakeEnvironmentProvider provider = new();
        ClaudeConfigClientCore client = MakeClient();
        EnvironmentEditorViewModel vm = new(provider, client)
        {
            EditingScope = EnvEditScope.Claude,
            NewVarName = "MY_NEW_VAR",
        };

        vm.AddNewCommand.Execute(null);

        Assert.IsNotNull(vm.SelectedEntry);
        Assert.AreEqual("MY_NEW_VAR", vm.SelectedEntry.Name, ignoreCase: true,
            message: "Newly added entry should be selected");

        LayeredValue layered = client.GetLayeredValueSnapshot("env");
        JsonObject? envObj = layered.GetValueAt(ConfigScope.User) as JsonObject;
        Assert.IsNotNull(envObj);
        Assert.IsTrue(envObj.ContainsKey("MY_NEW_VAR"));
    }

    // -----------------------------------------------------------------------
    // SyncEditValue — detail pane updates when selection changes
    // -----------------------------------------------------------------------

    [TestMethod]
    public void SyncEditValue_ShowsClaudeValueWhenScopeIsClaudeAndEntryHasClaudeValue()
    {
        FakeEnvironmentProvider provider = new();
        ClaudeConfigClientCore client = MakeClient("""{"env":{"ANTHROPIC_API_KEY":"sk-abc"}}""");
        EnvironmentEditorViewModel vm = new(provider, client);

        vm.EditingScope = EnvEditScope.Claude;
        vm.SelectedEntry = vm.AllEntries.First(e =>
            e.Name.Equals("ANTHROPIC_API_KEY", StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual("sk-abc", vm.EditValue);
    }

    [TestMethod]
    public void SyncEditValue_ClearsEditValueWhenNothingSelected()
    {
        FakeEnvironmentProvider provider = new();
        EnvironmentEditorViewModel vm = new(provider, null);

        vm.SelectedEntry = null;

        Assert.IsNull(vm.EditValue);
    }

    // ── Suggested environment variables ───────────────────────────────────────

    [TestMethod]
    public void SuggestedEnvVars_AppearInAllEntries_WhenNotAlreadyPresent()
    {
        FakeEnvironmentProvider provider = new();
        EnvironmentEditorViewModel vm = new(provider, null,
            suggestedEnvVarNames: ["CLAUDE_CODE_TIMEOUT_MS", "ANTHROPIC_BASE_URL"]);

        Assert.IsTrue(vm.AllEntries.Any(e => e.Name == "CLAUDE_CODE_TIMEOUT_MS"),
            "Suggested var must appear in AllEntries.");
        Assert.IsTrue(vm.AllEntries.Any(e => e.Name == "ANTHROPIC_BASE_URL"),
            "Suggested var must appear in AllEntries.");
    }

    [TestMethod]
    public void SuggestedEnvVars_AreMarked_IsFromSuggestion()
    {
        FakeEnvironmentProvider provider = new();
        EnvironmentEditorViewModel vm = new(provider, null,
            suggestedEnvVarNames: ["CLAUDE_CODE_TIMEOUT_MS"]);

        EnvVarEntry entry = vm.AllEntries.Single(e => e.Name == "CLAUDE_CODE_TIMEOUT_MS");
        Assert.IsTrue(entry.IsFromSuggestion);
        Assert.IsNull(entry.EffectiveValue, "Suggested-only var has no effective value.");
    }

    [TestMethod]
    public void SuggestedEnvVars_NotDuplicated_WhenAlreadyInEnvironment()
    {
        FakeEnvironmentProvider provider = new();
        provider.Process["CLAUDE_CODE_TIMEOUT_MS"] = "5000";
        EnvironmentEditorViewModel vm = new(provider, null,
            suggestedEnvVarNames: ["CLAUDE_CODE_TIMEOUT_MS"]);

        // Should appear exactly once, and IsFromSuggestion should be false
        // (the real value from Process takes precedence).
        List<EnvVarEntry> entries = vm.AllEntries.Where(e => e.Name == "CLAUDE_CODE_TIMEOUT_MS").ToList();
        Assert.AreEqual(1, entries.Count, "Must not be duplicated.");
        Assert.IsFalse(entries[0].IsFromSuggestion,
            "Entry already present in environment must not be flagged as suggestion.");
    }

    [TestMethod]
    public void SuggestedEnvVars_ShownInFilteredEntries_EvenWhenShowAllFalse()
    {
        FakeEnvironmentProvider provider = new();
        EnvironmentEditorViewModel vm = new(provider, null,
            suggestedEnvVarNames: ["CLAUDE_CODE_TIMEOUT_MS"]);

        vm.ShowAll = false;

        // Suggested vars should be visible regardless of the ShowAll flag.
        Assert.IsTrue(vm.FilteredEntries.Any(e => e.Name == "CLAUDE_CODE_TIMEOUT_MS"),
            "Suggested vars must appear in FilteredEntries even when ShowAll=false.");
    }

    // ── New-var-name validation ──────────────────────────────────────────────

    private sealed class ThrowingEnvironmentProvider : IEnvironmentProvider
    {
        private readonly FakeEnvironmentProvider _inner = new();
        public Dictionary<string, string> Process => _inner.Process;

        public IDictionary GetVariables(EnvironmentVariableTarget t)
        {
            return _inner.GetVariables(t);
        }

        public void SetVariable(string name, string? value, EnvironmentVariableTarget target)
        {
            throw new UnauthorizedAccessException("Access is denied.");
        }
    }

    [TestMethod]
    public void ApplyValue_WhenSetVariableThrowsUnauthorized_SetsStatusMessageAndDoesNotThrow()
    {
        ThrowingEnvironmentProvider provider = new();
        provider.Process["MY_VAR"] = "val";

        EnvironmentEditorViewModel vm = new(provider, null);
        vm.EditingScope = EnvEditScope.User;
        vm.SelectedEntry = vm.AllEntries.First(e => e.Name == "MY_VAR");
        vm.EditValue = "new-val";

        vm.SaveEditCommand.Execute(null);

        Assert.IsNotNull(vm.StatusMessage, "StatusMessage should be set after access denied.");
        Assert.IsTrue(vm.StatusMessage!.Contains("Access denied"),
            $"StatusMessage should contain 'Access denied' but was: {vm.StatusMessage}");
    }

    [TestMethod]
    public void NewEnvKey_WithSpacesInKey_SetsNewVarNameIsValidFalse()
    {
        FakeEnvironmentProvider provider = new();
        EnvironmentEditorViewModel vm = new(provider, null);

        vm.NewVarName = "MY VAR";

        Assert.IsFalse(vm.NewVarNameIsValid,
            "A key containing spaces should be flagged as invalid.");
    }

    [TestMethod]
    public void NewEnvKey_ValidName_SetsNewVarNameIsValidTrue()
    {
        FakeEnvironmentProvider provider = new();
        EnvironmentEditorViewModel vm = new(provider, null);

        vm.NewVarName = "VALID_VAR_123";

        Assert.IsTrue(vm.NewVarNameIsValid,
            "A well-formed env-var name should be flagged as valid.");
    }

    [TestMethod]
    public void NewEnvKey_EmptyString_NewVarNameIsValidTrue()
    {
        FakeEnvironmentProvider provider = new();
        EnvironmentEditorViewModel vm = new(provider, null);

        vm.NewVarName = string.Empty;

        Assert.IsTrue(vm.NewVarNameIsValid,
            "An empty name (not yet typed) should be treated as valid/unset.");
    }
}