using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Essentials;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Pins the model ↔ effort inter-relationship on the Essentials page. The key
/// contract: <b>loading never writes</b> (no phantom dirty state on app open /
/// reload) — it filters the dropdown + shows an advisory; coercion (and the
/// editing-scope override write) happens ONLY on a genuine user model change.
/// </summary>
public sealed class EssentialsModelEffortConstraintTests
{
    private static ClaudeConfigClientBase MakeClient(string userJson)
    {
        JsonObject root = (JsonObject)JsonNode.Parse(userJson)!;
        SettingsDocument doc = new(ConfigScope.User, "user.json", root, isReadOnly: false);
        SettingsWorkspace ws = new([doc], ClaudeMergePolicy.Instance);
        return ClaudeCodeClient.FromExistingWorkspace(ClaudeEnvironment.Empty, ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
    }

    private static async Task<(EssentialsViewModel vm, ClaudeConfigClientBase client)> OpenAsync(string userJson)
    {
        ClaudeConfigClientBase client = MakeClient(userJson);
        EssentialsViewModel vm = new(client, new FakeEnvironmentProvider());
        await vm.RefreshAsync();
        return (vm, client);
    }

    private static EssentialsCardViewModel Effort(EssentialsViewModel vm)
        => vm.GetCardById(EssentialsViewModel.CardIdEffortLevel)!;

    private static void ChangeModel(EssentialsViewModel vm, string model)
        => vm.GetCardById(EssentialsViewModel.CardIdModel)!.EnumValue = model;

    // ── Load never writes (the phantom-dirty regression) ──────────────────

    [Fact]
    public async Task Load_ValidCombo_StaysClean()
    {
        (_, ClaudeConfigClientBase client) = await OpenAsync("""{"model":"claude-opus-4-8","effortLevel":"high"}""");
        Assert.False(client.HasUnsavedChanges, "A valid persisted combo must not dirty the workspace on load.");
    }

    [Fact]
    public async Task Load_InvalidCombo_DoesNotWriteOrDirty_ButAdvises()
    {
        (EssentialsViewModel vm, ClaudeConfigClientBase client) =
            await OpenAsync("""{"model":"claude-sonnet-4-6","effortLevel":"xhigh"}""");

        Assert.False(client.HasUnsavedChanges, "Load must not write a coercion — no phantom dirty state.");
        MessageAssert.Equal("xhigh", client.GetEffective<string>("effortLevel"), "On-disk effort is untouched on load.");
        MessageAssert.Equal("xhigh", Effort(vm).EnumValue, "The persisted (now-unsupported) value stays visible.");
        Assert.True(Effort(vm).ShowConstraintNotice, "An advisory explains the unsupported value.");
        MessageAssert.Contains("xhigh", Effort(vm).FilteredOptions.ToList(), "Current value remains selectable on load.");
        MessageAssert.DoesNotContain("max", Effort(vm).FilteredOptions.ToList(), "Session-only max is still omitted.");
    }

    [Fact]
    public async Task Load_HaikuPlusEffort_DisablesAndAdvises_DoesNotDrop_NorDirty()
    {
        (EssentialsViewModel vm, ClaudeConfigClientBase client) =
            await OpenAsync("""{"model":"claude-haiku-4-5","effortLevel":"high"}""");

        Assert.True(Effort(vm).EnumDisabled, "Haiku exposes no effort → control disabled.");
        Assert.Empty(Effort(vm).FilteredOptions);
        Assert.True(Effort(vm).ShowConstraintNotice);
        MessageAssert.Equal("high", client.GetEffective<string>("effortLevel"),
            "Load must NOT drop the persisted effort (only a user model change does).");
        Assert.False(client.HasUnsavedChanges, "No phantom dirty state on load.");
    }

    // ── User model change DOES coerce + write ─────────────────────────────

    [Fact]
    public async Task UserChange_InvalidEffort_CoercesToNearestAnalog_AndDirties()
    {
        (EssentialsViewModel vm, ClaudeConfigClientBase client) =
            await OpenAsync("""{"model":"claude-opus-4-8","effortLevel":"xhigh"}""");
        Assert.False(client.HasUnsavedChanges);

        ChangeModel(vm, "claude-sonnet-4-6"); // drops xhigh

        MessageAssert.Equal("high", Effort(vm).EnumValue, "xhigh coerces to the nearest analog (high).");
        MessageAssert.Equal("high", client.GetEffective<string>("effortLevel"), "Coercion persists as an editing-scope override.");
        Assert.True(Effort(vm).ShowConstraintNotice, "A notice explains the auto-change.");
        Assert.True(client.HasUnsavedChanges, "A user-driven coercion is a real, savable change.");
    }

    [Fact]
    public async Task UserChange_ToHaiku_DropsExplicitEffort()
    {
        (EssentialsViewModel vm, ClaudeConfigClientBase client) =
            await OpenAsync("""{"model":"claude-opus-4-8","effortLevel":"high"}""");

        ChangeModel(vm, "claude-haiku-4-5");

        Assert.True(Effort(vm).EnumDisabled);
        Assert.True(string.IsNullOrEmpty(client.GetEffective<string>("effortLevel")),
            "A user switch to a no-effort model drops the explicit effort.");
        Assert.True(client.HasUnsavedChanges);
    }

    [Fact]
    public async Task UserChange_StillValidEffort_NoCoercionNoNotice()
    {
        (EssentialsViewModel vm, ClaudeConfigClientBase client) =
            await OpenAsync("""{"model":"claude-opus-4-8","effortLevel":"high"}""");

        ChangeModel(vm, "claude-sonnet-4-6"); // still supports high

        Assert.Equal("high", Effort(vm).EnumValue);
        Assert.Equal("high", client.GetEffective<string>("effortLevel"));
        Assert.False(Effort(vm).ShowConstraintNotice);
    }

    [Fact]
    public async Task EffortOptions_NarrowToEffectiveModel_OnUserChange()
    {
        (EssentialsViewModel vm, _) = await OpenAsync("""{"model":"claude-opus-4-8","effortLevel":"high"}""");
        MessageAssert.Contains("xhigh", Effort(vm).FilteredOptions.ToList(), "Opus 4.8 supports xhigh.");

        ChangeModel(vm, "claude-sonnet-4-6");
        MessageAssert.DoesNotContain("xhigh", Effort(vm).FilteredOptions.ToList(), "Sonnet 4.6 drops xhigh.");
    }

    [Fact]
    public async Task ModelIndicator_IsPopulated_ForKnownModel()
    {
        (EssentialsViewModel vm, _) = await OpenAsync("""{"model":"claude-opus-4-8"}""");
        string summary = Effort(vm).ModelSupportSummary;
        Assert.False(string.IsNullOrWhiteSpace(summary), "Indicator must be populated.");
        MessageAssert.Contains("Opus 4.8", summary, "Indicator shows the model's brand label.");
    }
}
