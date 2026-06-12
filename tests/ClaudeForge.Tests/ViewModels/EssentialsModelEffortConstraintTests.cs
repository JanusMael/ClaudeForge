using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Pins the model ↔ effort inter-relationship on the Essentials page. The key
/// contract: <b>loading never writes</b> (no phantom dirty state on app open /
/// reload) — it filters the dropdown + shows an advisory; coercion (and the
/// editing-scope override write) happens ONLY on a genuine user model change.
/// </summary>
[TestClass]
public sealed class EssentialsModelEffortConstraintTests
{
    private static ClaudeConfigClientCore MakeClient(string userJson)
    {
        JsonObject root = (JsonObject)JsonNode.Parse(userJson)!;
        SettingsDocument doc = new(ConfigScope.User, "user.json", root, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        return ClaudeCodeClient.FromExistingWorkspace(ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
    }

    private static async Task<(EssentialsViewModel vm, ClaudeConfigClientCore client)> OpenAsync(string userJson)
    {
        ClaudeConfigClientCore client = MakeClient(userJson);
        EssentialsViewModel vm = new(client, new FakeEnvironmentProvider());
        await vm.RefreshAsync();
        return (vm, client);
    }

    private static EssentialsCardViewModel Effort(EssentialsViewModel vm)
        => vm.GetCardById(EssentialsViewModel.CardIdEffortLevel)!;

    private static void ChangeModel(EssentialsViewModel vm, string model)
        => vm.GetCardById(EssentialsViewModel.CardIdModel)!.EnumValue = model;

    // ── Load never writes (the phantom-dirty regression) ──────────────────

    [TestMethod]
    public async Task Load_ValidCombo_StaysClean()
    {
        (_, ClaudeConfigClientCore client) = await OpenAsync("""{"model":"claude-opus-4-8","effortLevel":"high"}""");
        Assert.IsFalse(client.HasUnsavedChanges, "A valid persisted combo must not dirty the workspace on load.");
    }

    [TestMethod]
    public async Task Load_InvalidCombo_DoesNotWriteOrDirty_ButAdvises()
    {
        (EssentialsViewModel vm, ClaudeConfigClientCore client) =
            await OpenAsync("""{"model":"claude-sonnet-4-6","effortLevel":"xhigh"}""");

        Assert.IsFalse(client.HasUnsavedChanges, "Load must not write a coercion — no phantom dirty state.");
        Assert.AreEqual("xhigh", client.GetEffective<string>("effortLevel"), "On-disk effort is untouched on load.");
        Assert.AreEqual("xhigh", Effort(vm).EnumValue, "The persisted (now-unsupported) value stays visible.");
        Assert.IsTrue(Effort(vm).ShowConstraintNotice, "An advisory explains the unsupported value.");
        CollectionAssert.Contains(Effort(vm).FilteredOptions.ToList(), "xhigh", "Current value remains selectable on load.");
        CollectionAssert.DoesNotContain(Effort(vm).FilteredOptions.ToList(), "max", "Session-only max is still omitted.");
    }

    [TestMethod]
    public async Task Load_HaikuPlusEffort_DisablesAndAdvises_DoesNotDrop_NorDirty()
    {
        (EssentialsViewModel vm, ClaudeConfigClientCore client) =
            await OpenAsync("""{"model":"claude-haiku-4-5","effortLevel":"high"}""");

        Assert.IsTrue(Effort(vm).EnumDisabled, "Haiku exposes no effort → control disabled.");
        Assert.AreEqual(0, Effort(vm).FilteredOptions.Count);
        Assert.IsTrue(Effort(vm).ShowConstraintNotice);
        Assert.AreEqual("high", client.GetEffective<string>("effortLevel"),
            "Load must NOT drop the persisted effort (only a user model change does).");
        Assert.IsFalse(client.HasUnsavedChanges, "No phantom dirty state on load.");
    }

    // ── User model change DOES coerce + write ─────────────────────────────

    [TestMethod]
    public async Task UserChange_InvalidEffort_CoercesToNearestAnalog_AndDirties()
    {
        (EssentialsViewModel vm, ClaudeConfigClientCore client) =
            await OpenAsync("""{"model":"claude-opus-4-8","effortLevel":"xhigh"}""");
        Assert.IsFalse(client.HasUnsavedChanges);

        ChangeModel(vm, "claude-sonnet-4-6"); // drops xhigh

        Assert.AreEqual("high", Effort(vm).EnumValue, "xhigh coerces to the nearest analog (high).");
        Assert.AreEqual("high", client.GetEffective<string>("effortLevel"), "Coercion persists as an editing-scope override.");
        Assert.IsTrue(Effort(vm).ShowConstraintNotice, "A notice explains the auto-change.");
        Assert.IsTrue(client.HasUnsavedChanges, "A user-driven coercion is a real, savable change.");
    }

    [TestMethod]
    public async Task UserChange_ToHaiku_DropsExplicitEffort()
    {
        (EssentialsViewModel vm, ClaudeConfigClientCore client) =
            await OpenAsync("""{"model":"claude-opus-4-8","effortLevel":"high"}""");

        ChangeModel(vm, "claude-haiku-4-5");

        Assert.IsTrue(Effort(vm).EnumDisabled);
        Assert.IsTrue(string.IsNullOrEmpty(client.GetEffective<string>("effortLevel")),
            "A user switch to a no-effort model drops the explicit effort.");
        Assert.IsTrue(client.HasUnsavedChanges);
    }

    [TestMethod]
    public async Task UserChange_StillValidEffort_NoCoercionNoNotice()
    {
        (EssentialsViewModel vm, ClaudeConfigClientCore client) =
            await OpenAsync("""{"model":"claude-opus-4-8","effortLevel":"high"}""");

        ChangeModel(vm, "claude-sonnet-4-6"); // still supports high

        Assert.AreEqual("high", Effort(vm).EnumValue);
        Assert.AreEqual("high", client.GetEffective<string>("effortLevel"));
        Assert.IsFalse(Effort(vm).ShowConstraintNotice);
    }

    [TestMethod]
    public async Task EffortOptions_NarrowToEffectiveModel_OnUserChange()
    {
        (EssentialsViewModel vm, _) = await OpenAsync("""{"model":"claude-opus-4-8","effortLevel":"high"}""");
        CollectionAssert.Contains(Effort(vm).FilteredOptions.ToList(), "xhigh", "Opus 4.8 supports xhigh.");

        ChangeModel(vm, "claude-sonnet-4-6");
        CollectionAssert.DoesNotContain(Effort(vm).FilteredOptions.ToList(), "xhigh", "Sonnet 4.6 drops xhigh.");
    }

    [TestMethod]
    public async Task ModelIndicator_IsPopulated_ForKnownModel()
    {
        (EssentialsViewModel vm, _) = await OpenAsync("""{"model":"claude-opus-4-8"}""");
        string summary = Effort(vm).ModelSupportSummary;
        Assert.IsFalse(string.IsNullOrWhiteSpace(summary), "Indicator must be populated.");
        StringAssert.Contains(summary, "Opus 4.8", "Indicator shows the model's brand label.");
    }
}
