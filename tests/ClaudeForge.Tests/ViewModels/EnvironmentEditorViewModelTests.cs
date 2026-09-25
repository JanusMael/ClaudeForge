using Bennewitz.Ninja.AgentForge.Core.Platform;
using System.Collections;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.AppServices.Abstractions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

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

public class EnvironmentEditorViewModelTests
{
    private static SettingsWorkspace MakeWorkspace(string userJson = "{}")
    {
        JsonObject root = (JsonObject)JsonNode.Parse(userJson)!;
        SettingsDocument doc = new(ConfigScope.User, "user.json", root, isReadOnly: false);
        return new SettingsWorkspace([doc], ClaudeMergePolicy.Instance);
    }

    /// <summary>
    /// Wrap an in-memory <see cref="SettingsWorkspace"/> in a ClaudeCodeClient
    /// via the internal <c>FromExistingWorkspace</c> overload. Avoids disk I/O
    /// while exercising the SDK-backed Environment editor migrated in
    /// 4.3.7 step 11. The grant lives in <c>AgentForge.Sdk.csproj</c>'s
    /// InternalsVisibleTo for ClaudeForge.Tests.
    /// </summary>
    private static AgentConfigClientCore MakeClient(string userJson = "{}")
    {
        SettingsWorkspace ws = MakeWorkspace(userJson);
        return ClaudeCodeClient.FromExistingWorkspace(ClaudeEnvironment.Empty, 
            ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
    }

    // -----------------------------------------------------------------------
    // Refresh / AllEntries
    // -----------------------------------------------------------------------

    [Fact]
    public void Refresh_PopulatesEntriesFromAllLayers()
    {
        // EnvironmentEditorViewModel.Refresh only consults the provider's
        // Machine scope on Windows (see the OperatingSystem.IsWindows()
        // guard at EnvironmentEditorViewModel.cs ~line 509).  On Linux /
        // macOS, Machine-scope env vars don't exist as an OS concept, so
        // the FakeEnvironmentProvider.Machine dict is intentionally
        // ignored — making MACHINE_VAR absent from AllEntries on those
        // platforms.  Skip rather than assert.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Machine-scope env vars are Windows-only — production code skips them on macOS/Linux.");
        }

        FakeEnvironmentProvider provider = new();
        provider.Machine["MACHINE_VAR"] = "machine-val";
        provider.User["USER_VAR"] = "user-val";
        provider.Process["PATH"] = "/usr/bin";

        EnvironmentEditorViewModel vm = new(provider, null);

        HashSet<string> names = vm.AllEntries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(names.Contains("MACHINE_VAR"), "MACHINE_VAR expected");
        Assert.True(names.Contains("USER_VAR"), "USER_VAR expected");
        Assert.True(names.Contains("PATH"), "PATH expected");
    }

    [Fact]
    public void Refresh_MergesClaudeEnvFromWorkspace()
    {
        FakeEnvironmentProvider provider = new();
        AgentConfigClientCore client = MakeClient("""{"env":{"ANTHROPIC_API_KEY":"sk-test"}}""");

        EnvironmentEditorViewModel vm = new(provider, client);

        EnvVarEntry? entry = vm.AllEntries.FirstOrDefault(e =>
            string.Equals(e.Name, "ANTHROPIC_API_KEY", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);
        Assert.Equal("sk-test", entry.ClaudeValue);
    }

    // -----------------------------------------------------------------------
    // Priority: Process > Claude > User > Machine
    // -----------------------------------------------------------------------

    [Fact]
    public void EffectiveValue_ProcessWinsOverAll()
    {
        FakeEnvironmentProvider provider = new();
        provider.Machine["PATH"] = "machine-path";
        provider.User["PATH"] = "user-path";
        provider.Process["PATH"] = "process-path";

        AgentConfigClientCore client = MakeClient("""{"env":{"PATH":"claude-path"}}""");
        EnvironmentEditorViewModel vm = new(provider, client);

        EnvVarEntry entry = vm.AllEntries.First(e => e.Name.Equals("PATH", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("process-path", entry.EffectiveValue);
        Assert.Equal("Process", entry.EffectiveSource);
    }

    [Fact]
    public void EffectiveValue_ClaudeWinsOverUserAndMachine()
    {
        FakeEnvironmentProvider provider = new();
        provider.Machine["MY_VAR"] = "machine-val";
        provider.User["MY_VAR"] = "user-val";

        AgentConfigClientCore client = MakeClient("""{"env":{"MY_VAR":"claude-val"}}""");
        EnvironmentEditorViewModel vm = new(provider, client);

        EnvVarEntry entry = vm.AllEntries.First(e => e.Name.Equals("MY_VAR", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("claude-val", entry.EffectiveValue);
        Assert.Equal("Claude", entry.EffectiveSource);
    }

    // -----------------------------------------------------------------------
    // IsOverridden
    // -----------------------------------------------------------------------

    [Fact]
    public void IsOverridden_TrueWhenMultipleLayersDefineVar()
    {
        // Machine + User layer overlap requires Machine to be readable.
        // EnvironmentEditorViewModel skips the Machine scope on non-Windows
        // (see OS guard at EnvironmentEditorViewModel.cs ~line 509), so the
        // override condition can't be observed on Linux / macOS.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Machine-scope env vars are Windows-only — production code skips them on macOS/Linux.");
        }

        FakeEnvironmentProvider provider = new();
        provider.Machine["CLAUDE_MODEL"] = "m1";
        provider.User["CLAUDE_MODEL"] = "m2";

        EnvironmentEditorViewModel vm = new(provider, null);
        EnvVarEntry entry = vm.AllEntries.First(e => e.Name.Equals("CLAUDE_MODEL", StringComparison.OrdinalIgnoreCase));
        Assert.True(entry.IsOverridden);
    }

    [Fact]
    public void IsOverridden_FalseWhenOnlyOneLayer()
    {
        FakeEnvironmentProvider provider = new();
        provider.Process["UNIQUE_VAR"] = "only-here";

        EnvironmentEditorViewModel vm = new(provider, null);
        EnvVarEntry entry = vm.AllEntries.First(e => e.Name.Equals("UNIQUE_VAR", StringComparison.OrdinalIgnoreCase));
        Assert.False(entry.IsOverridden);
    }

    // -----------------------------------------------------------------------
    // FilteredEntries — allowlist + ShowAll + text filter
    // -----------------------------------------------------------------------

    [Fact]
    public void FilteredEntries_AllowlistFiltersOutObscureVars()
    {
        FakeEnvironmentProvider provider = new();
        provider.Process["PATH"] = "/usr/bin";
        provider.Process["OBSCURE_ZZZZ"] = "hidden";

        EnvironmentEditorViewModel vm = new(provider, null);
        // ShowAll is false by default
        List<string> names = vm.FilteredEntries.Select(e => e.Name).ToList();

        Assert.True(names.Any(n => n.Equals("PATH", StringComparison.OrdinalIgnoreCase)),
            "PATH should be visible in allowlist mode");
        Assert.False(names.Any(n => n.Equals("OBSCURE_ZZZZ", StringComparison.OrdinalIgnoreCase)),
            "OBSCURE_ZZZZ should be hidden in allowlist mode");
    }

    [Fact]
    public void FilteredEntries_ShowAllExposesEverything()
    {
        FakeEnvironmentProvider provider = new();
        provider.Process["OBSCURE_ZZZZ"] = "visible-now";

        EnvironmentEditorViewModel vm = new(provider, null) { ShowAll = true };

        List<string> names = vm.FilteredEntries.Select(e => e.Name).ToList();
        Assert.Contains(names, n => n.Equals("OBSCURE_ZZZZ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
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
        Assert.Contains(names, n => n.Contains("CLAUDE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Equals("ANTHROPIC_KEY", StringComparison.OrdinalIgnoreCase));
    }

    // -----------------------------------------------------------------------
    // SaveEdit → writes to Claude workspace
    // -----------------------------------------------------------------------

    [Fact]
    public void SaveEdit_ConfigScopeAdapter_WritesToWorkspace()
    {
        FakeEnvironmentProvider provider = new();
        provider.Process["PATH"] = "/usr/bin"; // so PATH is in AllEntries
        AgentConfigClientCore client = MakeClient();
        EnvironmentEditorViewModel vm = new(provider, client);

        vm.EditingScope = EnvEditScope.Claude;
        vm.SelectedEntry = vm.AllEntries.First(e =>
            e.Name.Equals("PATH", StringComparison.OrdinalIgnoreCase));
        vm.EditValue = "/custom/bin";

        vm.SaveEditCommand.Execute(null);

        // Workspace should now have env.PATH = "/custom/bin" in the User doc
        LayeredValue layered = client.GetLayeredValueSnapshot("env");
        JsonObject? envObj = layered.GetValueAt(ConfigScope.User) as JsonObject;
        Assert.NotNull(envObj);
        Assert.Equal("/custom/bin", envObj["PATH"]?.GetValue<string>());
    }

    // -----------------------------------------------------------------------
    // RemoveFromScope → clears the variable from Claude env
    // -----------------------------------------------------------------------

    [Fact]
    public void RemoveFromScope_ConfigScopeAdapter_RemovesFromWorkspace()
    {
        FakeEnvironmentProvider provider = new();
        AgentConfigClientCore client = MakeClient("""{"env":{"MY_KEY":"existing-val"}}""");
        EnvironmentEditorViewModel vm = new(provider, client);

        vm.EditingScope = EnvEditScope.Claude;
        vm.SelectedEntry = vm.AllEntries.First(e =>
            e.Name.Equals("MY_KEY", StringComparison.OrdinalIgnoreCase));

        vm.RemoveFromScopeCommand.Execute(null);

        LayeredValue layered = client.GetLayeredValueSnapshot("env");
        // env object at user scope should be empty or gone
        JsonObject? envObj = layered.GetValueAt(ConfigScope.User) as JsonObject;
        Assert.True(envObj == null || !envObj.ContainsKey("MY_KEY"));
    }

    // -----------------------------------------------------------------------
    // AddNew — adds entry and selects it
    // -----------------------------------------------------------------------

    [Fact]
    public void AddNew_ConfigScopeAdapter_AddsEntryAndSelectsIt()
    {
        FakeEnvironmentProvider provider = new();
        AgentConfigClientCore client = MakeClient();
        EnvironmentEditorViewModel vm = new(provider, client)
        {
            EditingScope = EnvEditScope.Claude,
            NewVarName = "MY_NEW_VAR",
        };

        vm.AddNewCommand.Execute(null);

        Assert.NotNull(vm.SelectedEntry);
        MessageAssert.Equal("MY_NEW_VAR", vm.SelectedEntry.Name, true,
            "Newly added entry should be selected");

        LayeredValue layered = client.GetLayeredValueSnapshot("env");
        JsonObject? envObj = layered.GetValueAt(ConfigScope.User) as JsonObject;
        Assert.NotNull(envObj);
        Assert.True(envObj.ContainsKey("MY_NEW_VAR"));
    }

    // -----------------------------------------------------------------------
    // SyncEditValue — detail pane updates when selection changes
    // -----------------------------------------------------------------------

    [Fact]
    public void SyncEditValue_ShowsClaudeValueWhenScopeIsClaudeAndEntryHasClaudeValue()
    {
        FakeEnvironmentProvider provider = new();
        AgentConfigClientCore client = MakeClient("""{"env":{"ANTHROPIC_API_KEY":"sk-abc"}}""");
        EnvironmentEditorViewModel vm = new(provider, client);

        vm.EditingScope = EnvEditScope.Claude;
        vm.SelectedEntry = vm.AllEntries.First(e =>
            e.Name.Equals("ANTHROPIC_API_KEY", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("sk-abc", vm.EditValue);
    }

    [Fact]
    public void SyncEditValue_ClearsEditValueWhenNothingSelected()
    {
        FakeEnvironmentProvider provider = new();
        EnvironmentEditorViewModel vm = new(provider, null);

        vm.SelectedEntry = null;

        Assert.Null(vm.EditValue);
    }

    // ── Suggested environment variables ───────────────────────────────────────

    [Fact]
    public void SuggestedEnvVars_AppearInAllEntries_WhenNotAlreadyPresent()
    {
        FakeEnvironmentProvider provider = new();
        EnvironmentEditorViewModel vm = new(provider, null,
            suggestedEnvVarNames: ["CLAUDE_CODE_TIMEOUT_MS", "ANTHROPIC_BASE_URL"]);

        Assert.True(vm.AllEntries.Any(e => e.Name == "CLAUDE_CODE_TIMEOUT_MS"),
            "Suggested var must appear in AllEntries.");
        Assert.True(vm.AllEntries.Any(e => e.Name == "ANTHROPIC_BASE_URL"),
            "Suggested var must appear in AllEntries.");
    }

    [Fact]
    public void SuggestedEnvVars_AreMarked_IsFromSuggestion()
    {
        FakeEnvironmentProvider provider = new();
        EnvironmentEditorViewModel vm = new(provider, null,
            suggestedEnvVarNames: ["CLAUDE_CODE_TIMEOUT_MS"]);

        EnvVarEntry entry = vm.AllEntries.Single(e => e.Name == "CLAUDE_CODE_TIMEOUT_MS");
        Assert.True(entry.IsFromSuggestion);
        MessageAssert.Null(entry.EffectiveValue, "Suggested-only var has no effective value.");
    }

    [Fact]
    public void SuggestedEnvVars_NotDuplicated_WhenAlreadyInEnvironment()
    {
        FakeEnvironmentProvider provider = new();
        provider.Process["CLAUDE_CODE_TIMEOUT_MS"] = "5000";
        EnvironmentEditorViewModel vm = new(provider, null,
            suggestedEnvVarNames: ["CLAUDE_CODE_TIMEOUT_MS"]);

        // Should appear exactly once, and IsFromSuggestion should be false
        // (the real value from Process takes precedence).
        List<EnvVarEntry> entries = vm.AllEntries.Where(e => e.Name == "CLAUDE_CODE_TIMEOUT_MS").ToList();
        MessageAssert.Equal(1, entries.Count, "Must not be duplicated.");
        Assert.False(entries[0].IsFromSuggestion,
            "Entry already present in environment must not be flagged as suggestion.");
    }

    [Fact]
    public void SuggestedEnvVars_ShownInFilteredEntries_EvenWhenShowAllFalse()
    {
        FakeEnvironmentProvider provider = new();
        EnvironmentEditorViewModel vm = new(provider, null,
            suggestedEnvVarNames: ["CLAUDE_CODE_TIMEOUT_MS"]);

        vm.ShowAll = false;

        // Suggested vars should be visible regardless of the ShowAll flag.
        Assert.True(vm.FilteredEntries.Any(e => e.Name == "CLAUDE_CODE_TIMEOUT_MS"),
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

    [Fact]
    public void ApplyValue_WhenSetVariableThrowsUnauthorized_SetsStatusMessageAndDoesNotThrow()
    {
        ThrowingEnvironmentProvider provider = new();
        provider.Process["MY_VAR"] = "val";

        EnvironmentEditorViewModel vm = new(provider, null);
        vm.EditingScope = EnvEditScope.User;
        vm.SelectedEntry = vm.AllEntries.First(e => e.Name == "MY_VAR");
        vm.EditValue = "new-val";

        vm.SaveEditCommand.Execute(null);

        MessageAssert.NotNull(vm.StatusMessage, "StatusMessage should be set after access denied.");
        Assert.True(vm.StatusMessage!.Contains("Access denied"),
            $"StatusMessage should contain 'Access denied' but was: {vm.StatusMessage}");
    }

    [Fact]
    public void NewEnvKey_WithSpacesInKey_SetsNewVarNameIsValidFalse()
    {
        FakeEnvironmentProvider provider = new();
        EnvironmentEditorViewModel vm = new(provider, null);

        vm.NewVarName = "MY VAR";

        Assert.False(vm.NewVarNameIsValid,
            "A key containing spaces should be flagged as invalid.");
    }

    [Fact]
    public void NewEnvKey_ValidName_SetsNewVarNameIsValidTrue()
    {
        FakeEnvironmentProvider provider = new();
        EnvironmentEditorViewModel vm = new(provider, null);

        vm.NewVarName = "VALID_VAR_123";

        Assert.True(vm.NewVarNameIsValid,
            "A well-formed env-var name should be flagged as valid.");
    }

    [Fact]
    public void NewEnvKey_EmptyString_NewVarNameIsValidTrue()
    {
        FakeEnvironmentProvider provider = new();
        EnvironmentEditorViewModel vm = new(provider, null);

        vm.NewVarName = string.Empty;

        Assert.True(vm.NewVarNameIsValid,
            "An empty name (not yet typed) should be treated as valid/unset.");
    }
}