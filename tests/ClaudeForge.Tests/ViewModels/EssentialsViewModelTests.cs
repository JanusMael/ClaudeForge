using System.Collections;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Env;
using Bennewitz.Ninja.ClaudeForge.Services;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Pins the Essentials page contract:
/// curated card list shape, value bindings round-trip through the SDK
/// client, danger-banner predicates, env-var source attribution, and the
/// amber-callout deep-link surface.
/// <para>
/// Uses a real <see cref="Bennewitz.Ninja.ClaudeForge.Sdk.ClaudeConfigClientCore"/> over an
/// in-memory <see cref="Bennewitz.Ninja.ClaudeForge.Core.Settings.SettingsWorkspace"/> via
/// <c>FromExistingWorkspace</c> — same pattern as
/// <c>EnvironmentEditorViewModelTests</c> — to avoid disk I/O while still
/// exercising the production SetValue / GetEffective / Env path.
/// </para>
/// </summary>
[TestClass]
public sealed class EssentialsViewModelTests
{
    /// <summary>Builds a ClaudeCodeClient over an in-memory User workspace.</summary>
    private static ClaudeConfigClientCore MakeClient(string userJson = "{}")
    {
        JsonObject root = (JsonObject)JsonNode.Parse(userJson)!;
        SettingsDocument doc = new(ConfigScope.User, "user.json", root, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        return ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
    }

    private static EssentialsViewModel MakeVm(
        ClaudeConfigClientCore? client = null,
        FakeEnvironmentProvider? envProvider = null)
    {
        return new EssentialsViewModel(client ?? MakeClient(), envProvider ?? new FakeEnvironmentProvider());
    }

    // ── Card list shape ──────────────────────────────────────────────────

    [TestMethod]
    public void Cards_PinnedSet_HasElevenCards()
    {
        EssentialsViewModel vm = MakeVm();
        Assert.AreEqual(11, vm.Cards.Count,
            "Pinned set is hand-curated; promoting / demoting a card must update both " +
            "EssentialsViewModel.BuildCards and this test in lock-step.");
    }

    [TestMethod]
    public void Cards_AllIdsAreUnique()
    {
        EssentialsViewModel vm = MakeVm();
        List<string> ids = vm.Cards.Select(c => c.Id).ToList();
        int unique = ids.Distinct().Count();
        Assert.AreEqual(ids.Count, unique, "Each card Id must be unique — search deep-links use Id as key.");
    }

    [TestMethod]
    public void Cards_AllHaveSeverityBrush()
    {
        EssentialsViewModel vm = MakeVm();
        foreach (EssentialsCardViewModel c in vm.Cards)
        {
            Assert.IsNotNull(c.SeverityBrush, $"Card {c.Id} must have a non-null severity brush.");
        }
    }

    /// <summary>
    /// pin the user-specified display order so a future
    /// reordering of the BuildCards list doesn't silently regress UX.
    /// Order groups cards by user mental model: day-to-day tunables
    /// first, security knobs in the middle, rarely-touched update knob
    /// at the end.
    /// </summary>
    [TestMethod]
    public void Cards_AreInUserSpecifiedOrder()
    {
        EssentialsViewModel vm = MakeVm();
        List<string> ids = vm.Cards.Select(c => c.Id).ToList();

        string[] expected =
        [
            EssentialsViewModel.CardIdAutoMemoryEnabled, // 1
            EssentialsViewModel.CardIdMaxOutputTokens, // 2
            EssentialsViewModel.CardIdMaxThinkingTokens, // 3
            EssentialsViewModel.CardIdEffortLevel, // 4
            EssentialsViewModel.CardIdFastMode, // 5
            EssentialsViewModel.CardIdModel, // 6
            EssentialsViewModel.CardIdDisableBypass, // 7
            EssentialsViewModel.CardIdEnableAllProjectMcp, // 8
            EssentialsViewModel.CardIdSandboxEnabled, // 9
            EssentialsViewModel.CardIdSandboxDomains, // 10
            EssentialsViewModel.CardIdAutoUpdatesChannel, // 11
        ];

        CollectionAssert.AreEqual(expected, ids,
            "Display order is part of the design contract — BuildCards must produce these card ids in this exact sequence.");
    }

    // ── Round-trip through the SDK ───────────────────────────────────────

    [TestMethod]
    public async Task BoolCard_WriteThroughSdk_PersistsToWorkspace()
    {
        ClaudeConfigClientCore client = MakeClient();
        EssentialsViewModel vm = MakeVm(client);
        await vm.RefreshAsync();

        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdEnableAllProjectMcp)!;

        // Set true → SDK SetValue is invoked via the auto-write partial OnBoolValueChanged.
        card.BoolValue = true;

        // Read back via the typed accessor we'd expect the GUI editor pages to use.
        Assert.IsTrue(client.GetEffective<bool?>("enableAllProjectMcpServers"),
            "Setting BoolValue=true must persist through SetValue to settings.json.");
    }

    [TestMethod]
    public async Task BoolCard_NullValue_RemovesProperty()
    {
        ClaudeConfigClientCore client = MakeClient("""{"enableAllProjectMcpServers": true}""");
        EssentialsViewModel vm = MakeVm(client);
        await vm.RefreshAsync();

        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdEnableAllProjectMcp)!;
        Assert.IsTrue(card.BoolValue, "Initial read should pick up the User-scope value.");

        // null = inherit / remove — auto-write fires RemoveValue path.
        card.BoolValue = null;

        // After removal, the effective value falls back to default(bool?) = null.
        Assert.IsNull(client.GetEffective<bool?>("enableAllProjectMcpServers"),
            "BoolValue=null must remove the key from the User document.");
    }

    [TestMethod]
    public async Task EnumCard_WriteThroughSdk_PersistsModel()
    {
        ClaudeConfigClientCore client = MakeClient();
        EssentialsViewModel vm = MakeVm(client);
        await vm.RefreshAsync();

        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdModel)!;
        card.EnumValue = "opus";

        Assert.AreEqual("opus", client.GetEffective<string>("model"),
            "EnumValue write must round-trip through the SDK.");
    }

    [TestMethod]
    public async Task IntCard_EnvVarRoundTrip_PersistsToEnvMap()
    {
        ClaudeConfigClientCore client = MakeClient();
        EssentialsViewModel vm = MakeVm(client);
        await vm.RefreshAsync();

        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdMaxThinkingTokens)!;
        card.IntValue = 32000;

        Assert.AreEqual("32000", client.Env.Get(EnvVarKey.MaxThinkingTokens),
            "IntValue=32000 on the MAX_THINKING_TOKENS card must write to env.MAX_THINKING_TOKENS.");
    }

    [TestMethod]
    public async Task IntCard_NullValue_RemovesEnvKey()
    {
        ClaudeConfigClientCore client = MakeClient("""{"env": {"MAX_THINKING_TOKENS": "32000"}}""");
        EssentialsViewModel vm = MakeVm(client);
        await vm.RefreshAsync();

        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdMaxThinkingTokens)!;
        Assert.AreEqual(32000, card.IntValue, "Initial read should parse the on-disk env value.");

        card.IntValue = null;

        Assert.IsNull(client.Env.Get(EnvVarKey.MaxThinkingTokens),
            "IntValue=null must remove the env-map key.");
    }

    [TestMethod]
    public async Task StringListCard_AddEntry_PersistsArray()
    {
        ClaudeConfigClientCore client = MakeClient();
        EssentialsViewModel vm = MakeVm(client);
        await vm.RefreshAsync();

        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdSandboxDomains)!;
        card.NewStringListEntry = "github.com";
        card.AddStringListEntryCommand.Execute(null);

        JsonArray? arr = client.GetEffective<JsonArray>("sandbox.network.allowedDomains");
        Assert.IsNotNull(arr);
        CollectionAssert.AreEqual(
            new[] { "github.com" },
            arr.Select(n => n!.GetValue<string>()).ToArray());
    }

    [TestMethod]
    public async Task StringListCard_RemoveEntry_PersistsRemoval()
    {
        ClaudeConfigClientCore client = MakeClient(
            """{"sandbox": {"network": {"allowedDomains": ["github.com", "registry.npmjs.org"]}}}""");
        EssentialsViewModel vm = MakeVm(client);
        await vm.RefreshAsync();

        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdSandboxDomains)!;
        Assert.AreEqual(2, card.StringListValues.Count, "Initial read should pick up both domains.");

        card.RemoveStringListEntryCommand.Execute("github.com");

        JsonArray? arr = client.GetEffective<JsonArray>("sandbox.network.allowedDomains");
        Assert.IsNotNull(arr);
        CollectionAssert.AreEqual(
            new[] { "registry.npmjs.org" },
            arr.Select(n => n!.GetValue<string>()).ToArray());
    }

    // ── Danger banner predicates ─────────────────────────────────────────

    [TestMethod]
    public async Task DangerBanner_EnableAllMcp_FiresOnTrue()
    {
        EssentialsViewModel vm = MakeVm();
        await vm.RefreshAsync();
        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdEnableAllProjectMcp)!;

        Assert.IsFalse(card.IsDanger, "Default (null) value must not show the danger banner.");

        card.BoolValue = true;
        Assert.IsTrue(card.IsDanger, "BoolValue=true on the auto-trust-MCP card MUST trigger the danger banner.");

        card.BoolValue = false;
        Assert.IsFalse(card.IsDanger, "BoolValue=false must clear the danger banner.");
    }

    [TestMethod]
    public async Task DangerBanner_SandboxEnabled_FiresOnFalse()
    {
        EssentialsViewModel vm = MakeVm();
        await vm.RefreshAsync();
        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdSandboxEnabled)!;

        Assert.IsFalse(card.IsDanger, "Default (null = inherit) must not show the banner.");

        card.BoolValue = false;
        Assert.IsTrue(card.IsDanger, "Sandbox disabled MUST trigger the danger banner.");

        card.BoolValue = true;
        Assert.IsFalse(card.IsDanger, "Sandbox enabled clears the banner.");
    }

    // ── Env-var source attribution ───────────────────────────────────────

    [TestMethod]
    public async Task EnvSource_PrefersSettingsJson_OverOsUser()
    {
        ClaudeConfigClientCore client = MakeClient("""{"env": {"MAX_THINKING_TOKENS": "1000"}}""");
        FakeEnvironmentProvider env = new();
        env.User["MAX_THINKING_TOKENS"] = "9999";

        EssentialsViewModel vm = MakeVm(client, env);
        await vm.RefreshAsync();

        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdMaxThinkingTokens)!;
        Assert.IsTrue(card.HasSettingsJsonSource);
        Assert.IsTrue(card.HasOsUserSource);
        Assert.AreEqual(
            Strings.LabelEssentialsEnvSourceSettings,
            card.EffectiveEnvSourceLabel,
            "settings.json must win over OS user-scope env.");
    }

    [TestMethod]
    public async Task EnvSource_OsUser_WhenSettingsJsonIsEmpty()
    {
        ClaudeConfigClientCore client = MakeClient();
        FakeEnvironmentProvider env = new();
        env.User["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = "8000";

        EssentialsViewModel vm = MakeVm(client, env);
        await vm.RefreshAsync();

        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdMaxOutputTokens)!;
        Assert.IsFalse(card.HasSettingsJsonSource);
        Assert.IsTrue(card.HasOsUserSource);
        Assert.AreEqual(
            Strings.LabelEssentialsEnvSourceUser,
            card.EffectiveEnvSourceLabel);
    }

    [TestMethod]
    public async Task EnvSource_None_WhenNoneSet()
    {
        EssentialsViewModel vm = MakeVm();
        await vm.RefreshAsync();

        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdMaxThinkingTokens)!;
        Assert.IsFalse(card.HasSettingsJsonSource);
        Assert.IsFalse(card.HasOsUserSource);
        Assert.IsFalse(card.HasOsMachineSource);
        Assert.AreEqual(
            Strings.LabelEssentialsEnvSourceNone,
            card.EffectiveEnvSourceLabel);
    }

    /// <summary>
    /// Regression for the 2026-05-13 Bug B fix.  The OS env-var probe in
    /// <c>UpdateEnvSourceLabelsAsync</c> reads from
    /// <c>IEnvironmentProvider.GetVariables(EnvironmentVariableTarget.User|Machine)</c>
    /// — synchronous IO that, on Windows Machine scope, hits the registry.
    /// To keep the dispatcher responsive on every workspace reload, the
    /// reads must run on a thread-pool worker via Task.Run.  This test
    /// fails if anyone removes the wrap.
    /// </summary>
    [TestMethod]
    public async Task RefreshAsync_RunsEnvProbeOnThreadPool()
    {
        ThreadCapturingEnvProvider env = new();
        ClaudeConfigClientCore client = MakeClient();

        // Run RefreshAsync from a dedicated non-thread-pool thread so the
        // "caller is NOT the thread pool" baseline isn't accidentally
        // satisfied (a normal test method already runs on a thread-pool
        // worker, which would make any Task.Run-vs-no-Task.Run comparison
        // unreliable).
        Exception? threadFault = null;
        Thread callerThread = new(() =>
        {
            try
            {
                EssentialsViewModel vm = new(client, env);
                vm.RefreshAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                threadFault = ex;
            }
        });
        callerThread.Start();
        callerThread.Join();
        if (threadFault is not null)
        {
            throw threadFault;
        }

        Assert.IsTrue(env.SawProbe,
            "Test invariant: at least one env-var card must have called GetVariables.");
        Assert.IsTrue(env.AllProbesFromThreadPool,
            "GetVariables must run on a thread-pool thread — " +
            "did someone remove the Task.Run(...) wrap in EssentialsViewModel.UpdateEnvSourceLabelsAsync?");
    }

    /// <summary>
    /// Regression: The <c>IsLoading</c> guard on env-int cards prevents
    /// the read-path IntValue assignment from recursing through
    /// <c>OnIntValueChanged</c> → <c>WriteAsync</c>.  Before 89bdb68 the
    /// read body was synchronous so the guard's scope was nanoseconds;
    /// after the Task.Run wrap for the registry probe the body became
    /// truly async, and if the guard spans the await, a user typing into
    /// the NumericUpDown during the still-pending registry probe gets
    /// their <c>OnIntValueChanged</c> short-circuited.  Reported by user
    /// typed 60000 into MaxOutputTokens, value didn't appear
    /// in the Save dialog, was gone on return.  This test uses a gated
    /// env provider to keep the read in its async phase, then writes to
    /// the card and asserts the SDK reflects it — fails if anyone widens
    /// the IsLoading scope back over the await.
    /// </summary>
    [TestMethod]
    public void IntValueWrite_NotSuppressed_WhileReadIsInAsyncPhase()
    {
        // Gated env provider: GetVariables blocks the thread-pool worker
        // until we release the gate, keeping ReadEnvIntAsync suspended at
        // its `await UpdateEnvSourceLabelsAsync` — i.e. exactly the window
        // where the bug used to manifest.
        ManualResetEventSlim gate = new(initialState: false);
        GatedEnvProvider env = new(gate);
        ClaudeConfigClientCore client = MakeClient();

        // ctor fires RefreshAsync fire-and-forget; the synchronous portion
        // of ReadEnvIntAsync runs to completion (IsLoading=true → IntValue
        // assignment → IsLoading=false in the new fix's tight finally),
        // then the await yields and the worker thread blocks on the gate.
        EssentialsViewModel vm = new(client, env);

        // Simulate a user typing into the NumericUpDown.  With the bug,
        // IsLoading would still be true here and OnIntValueChanged would
        // short-circuit.  With the fix, the write propagates synchronously
        // through OnIntValueChanged → _ = WriteAsync() → WriteEnvIntAsync's
        // sync portion → _client.Env.Set.
        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdMaxOutputTokens)!;
        card.IntValue = 60000;

        Assert.AreEqual("60000", client.Env.Get(EnvVarKey.MaxOutputTokens),
            "User write into IntValue was silently suppressed.  " +
            "Either IsLoading guard scope widened back over the await, or " +
            "OnIntValueChanged short-circuit ran while IsLoading was still set.");

        // Release the gate so RefreshAsync's pending tasks can complete.
        // Without this, the test process leaves a dangling worker thread
        // blocked indefinitely (manageable, but unclean).
        gate.Set();
    }

    /// <summary>
    /// repro for the user-reported "Save dialog doesn't show
    /// my MaxOutputTokens change" bug.  Mimics the real reload flow:
    /// construct VM with client A, wait for initial refresh, call
    /// RefreshAsync(B) to simulate a profile switch, then write to
    /// IntValue and assert the new client picks it up.
    /// </summary>
    [TestMethod]
    public async Task IntValueWrite_AfterRefreshWithNewClient_PropagatesToNewClient()
    {
        ClaudeConfigClientCore clientA = MakeClient();
        FakeEnvironmentProvider env = new();
        EssentialsViewModel vm = new(clientA, env);
        await vm.RefreshAsync(); // first refresh completes

        // Simulate a profile switch — new SDK client over a different
        // in-memory workspace.
        ClaudeConfigClientCore clientB = MakeClient();
        await vm.RefreshAsync(clientB);

        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdMaxOutputTokens)!;
        Assert.IsNull(card.IntValue, "Precondition: both clients have empty env, so card reads null.");

        // Simulate user typing 60000 into the spinner.
        card.IntValue = 60000;

        Assert.AreEqual("60000", clientB.Env.Get(EnvVarKey.MaxOutputTokens),
            "After RefreshAsync(B), writes from the card should land on client B (the current one).");
        Assert.IsNull(clientA.Env.Get(EnvVarKey.MaxOutputTokens),
            "Client A should NOT receive the write (it was superseded by client B).");
        Assert.IsTrue(clientB.HasUnsavedChanges,
            "Client B's workspace should be marked dirty by the env write.");
    }

    /// <summary>
    /// full Save-dialog-roundtrip simulation: write IntValue,
    /// then ask SaveDialogBuilder to render its preview.  Asserts the env
    /// change appears in the diff.  This is what the user's "Save dialog
    /// doesn't show my change" symptom is testing — if this test passes
    /// but the user still sees the symptom, the bug is upstream of the
    /// SDK / SaveDialogBuilder (binding, MWVM wiring, etc.).
    /// </summary>
    [TestMethod]
    public async Task IntValueWrite_AppearsInSaveDialogDiff()
    {
        ClaudeConfigClientCore client = MakeClient();
        FakeEnvironmentProvider env = new();
        EssentialsViewModel vm = new(client, env);
        await vm.RefreshAsync();

        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdMaxOutputTokens)!;
        card.IntValue = 60000;

        SaveChangesDialogViewModel? summary = SaveDialogBuilder.Build(client, claudeDesktopSdk: null);
        Assert.IsNotNull(summary,
            "SaveDialogBuilder returned null even though HasUnsavedChanges should be true — " +
            "this means JsonDiff didn't pick up the env change.");
        List<SaveChangeEntryViewModel> allEntries = summary.Sections.SelectMany(s => s.Entries).ToList();
        // JsonDiff for a newly-added top-level object emits ONE entry at the
        // key's own path with the whole subtree as NewValue (no recursion
        // for added objects).  When env was already present at baseline,
        // the diff recurses and emits "Added env.MAX_OUTPUT_TOKENS" instead.
        // Either form proves the env change is visible to the save dialog.
        bool envChangeVisible =
            allEntries.Any(e => e.Key.Contains("MAX_OUTPUT_TOKENS", StringComparison.Ordinal))
            || allEntries.Any(e => e.Key == "env"
                                   && (e.NewValue ?? "").Contains("MAX_OUTPUT_TOKENS", StringComparison.Ordinal));
        Assert.IsTrue(envChangeVisible,
            "Save dialog summary does not surface MAX_OUTPUT_TOKENS.  Entries: " +
            string.Join(", ", allEntries.Select(e => $"{e.Kind} {e.Key} → {e.NewValue}")));
    }

    /// <summary>
    /// IEnvironmentProvider that blocks <c>GetVariables</c> on a
    /// <see cref="ManualResetEventSlim"/> so a test can deterministically
    /// pause <c>UpdateEnvSourceLabelsAsync</c> at its async phase.
    /// </summary>
    private sealed class GatedEnvProvider : IEnvironmentProvider
    {
        private readonly ManualResetEventSlim _gate;
        private readonly ManualResetEventSlim? _entered;

        public GatedEnvProvider(ManualResetEventSlim gate, ManualResetEventSlim? entered = null)
        {
            _gate = gate;
            _entered = entered;
        }

        public IDictionary GetVariables(EnvironmentVariableTarget target)
        {
            _entered?.Set(); // signal the probe was reached, BEFORE blocking on the gate
            _gate.Wait();
            return new Dictionary<string, string>();
        }

        public void SetVariable(string name, string? value, EnvironmentVariableTarget target)
        {
        }
    }

    /// <summary>
    /// IEnvironmentProvider that records whether every GetVariables call
    /// happened on a thread-pool thread.  Mirrors the
    /// <c>ThreadCapturingFakeClient</c> pattern in MemoryEditorViewModelTests.
    /// </summary>
    private sealed class ThreadCapturingEnvProvider : IEnvironmentProvider
    {
        public bool SawProbe { get; private set; }
        public bool AllProbesFromThreadPool { get; private set; } = true;

        public IDictionary GetVariables(EnvironmentVariableTarget target)
        {
            SawProbe = true;
            if (!Thread.CurrentThread.IsThreadPoolThread)
            {
                AllProbesFromThreadPool = false;
            }

            return new Dictionary<string, string>();
        }

        public void SetVariable(string name, string? value, EnvironmentVariableTarget target)
        {
        }
    }

    // ── Amber callout (search deep-link target) ──────────────────────────

    [TestMethod]
    public async Task ActivateAmberCalloutFor_SetsFlagOnTargetCard()
    {
        EssentialsViewModel vm = MakeVm();
        await vm.RefreshAsync();

        Assert.IsTrue(vm.Cards.All(c => !c.ShowAmberCallout),
            "Precondition: no callout active before deep-link.");

        vm.ActivateAmberCalloutFor(EssentialsViewModel.CardIdSandboxEnabled);

        EssentialsCardViewModel sandbox = vm.GetCardById(EssentialsViewModel.CardIdSandboxEnabled)!;
        Assert.IsTrue(sandbox.ShowAmberCallout);
        // No spillage onto other cards.
        Assert.AreEqual(1, vm.Cards.Count(c => c.ShowAmberCallout));
    }

    [TestMethod]
    public async Task ActivateAmberCalloutFor_UnknownId_NoOp()
    {
        EssentialsViewModel vm = MakeVm();
        await vm.RefreshAsync();

        // Should not throw, should not flip any flag.
        vm.ActivateAmberCalloutFor("not-a-card-id");

        Assert.IsTrue(vm.Cards.All(c => !c.ShowAmberCallout));
    }

    // ── Deep-link target resolution ──────────────────────────────────────

    [TestMethod]
    public void Cards_AllHaveViewInGroupTitle()
    {
        // Every card must carry a non-empty home-group title so the
        // "View in <group>" deep-link button has a meaningful target.
        EssentialsViewModel vm = MakeVm();
        foreach (EssentialsCardViewModel c in vm.Cards)
        {
            Assert.IsFalse(string.IsNullOrEmpty(c.ViewInGroupTitle),
                $"Card {c.Id} must declare a home group.");
        }
    }

    // ── Reload contract ──────────────────────────────────────────────────

    [TestMethod]
    public async Task RefreshAsync_WithNewClient_RebindsValues()
    {
        ClaudeConfigClientCore firstClient = MakeClient("""{"model": "haiku"}""");
        EssentialsViewModel vm = MakeVm(firstClient);
        await vm.RefreshAsync();

        EssentialsCardViewModel modelCard = vm.GetCardById(EssentialsViewModel.CardIdModel)!;
        Assert.AreEqual("haiku", modelCard.EnumValue, "Initial read.");

        // Simulate workspace reload: swap in a fresh client and re-bind.
        ClaudeConfigClientCore secondClient = MakeClient("""{"model": "opus"}""");
        await vm.RefreshAsync(secondClient);

        Assert.AreEqual("opus", modelCard.EnumValue,
            "RefreshAsync(newClient) must rebind every card to the new client and re-read values.");
    }

    [TestMethod]
    public async Task DisableBypassCard_ReadsAndWritesDisableString_NotBoolean()
    {
        // permissions.disableBypassPermissionsMode is a STRING enum ["disable"], not a
        // bool. The Bool card must read "disable" -> checked and write checked ->
        // "disable" / unchecked -> remove. Writing a raw bool fails schema validation
        // (the reported bug).
        ClaudeConfigClientCore client =
            MakeClient("""{"permissions":{"disableBypassPermissionsMode":"disable"}}""");
        EssentialsViewModel vm = MakeVm(client);
        await vm.RefreshAsync();

        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdDisableBypass)!;
        Assert.IsTrue(card.BoolValue, "\"disable\" on disk must read as checked.");

        card.BoolValue = false; // unchecking removes the key (no on-disk "false")
        Assert.IsNull(client.GetScopeValue("permissions.disableBypassPermissionsMode", ConfigScope.User),
            "Unchecking must remove the key, never write a boolean.");

        card.BoolValue = true; // checking writes the string "disable"
        Assert.AreEqual("disable",
            client.GetScopeValue("permissions.disableBypassPermissionsMode", ConfigScope.User)?.GetValue<string>(),
            "Checking must persist the string \"disable\", not a boolean.");
    }

    [TestMethod]
    public async Task ModelCard_WhitespaceValue_IsNotPinned()
    {
        // The free-form model combo, left as whitespace, must be treated as "unset" —
        // never pinned as model=" " (the Essentials sibling of the model="" ghost).
        ClaudeConfigClientCore client = MakeClient("""{"model":"opus"}""");
        EssentialsViewModel vm = MakeVm(client);
        await vm.RefreshAsync();

        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdModel)!;
        card.EnumValue = "   "; // user types only whitespace

        Assert.IsNull(client.GetScopeValue("model", ConfigScope.User),
            "A whitespace-only model value must be treated as unset, not pinned as model=\" \".");
    }

    [TestMethod]
    public void Dispose_IsIdempotent()
    {
        EssentialsViewModel vm = MakeVm();
        vm.Dispose();
        vm.Dispose(); // second call must not throw
    }

    [TestMethod]
    public async Task RefreshAsync_AfterDispose_DoesNotThrow()
    {
        // The XML doc promises a refresh requested after disposal is a safe no-op.
        // The pre-fix gate regressed this: WaitAsync on the disposed gate threw
        // ObjectDisposedException. The top _disposed guard now short-circuits.
        EssentialsViewModel vm = MakeVm();
        vm.Dispose();

        await vm.RefreshAsync();
        await vm.RefreshAsync(MakeClient()); // also with a (would-be) rebind client
    }

    [TestMethod]
    public async Task Dispose_WhileRefreshSuspendedAtEnvProbe_DoesNotFault()
    {
        // Gate OPEN during construction: the ctor's fire-and-forget RefreshAsync
        // acquires the refresh gate synchronously (uncontended WaitAsync) and runs
        // through. Awaiting an explicit refresh then deterministically drains it —
        // the explicit refresh cannot acquire the gate until the ctor refresh
        // releases it — leaving nothing in flight.
        using ManualResetEventSlim gate = new(initialState: true);
        using ManualResetEventSlim entered = new(initialState: false);
        EssentialsViewModel vm = new(MakeClient(), new GatedEnvProvider(gate, entered));
        await vm.RefreshAsync();

        // Now park a *captured* refresh at the env probe: close the gate, start a
        // refresh that synchronously re-acquires the now-free gate and suspends at the
        // env-var registry probe (a Task.Run that blocks on the closed gate), holding
        // the refresh gate.
        gate.Reset();
        entered.Reset();
        Task parked = vm.RefreshAsync();

        // Deterministically wait until the probe is actually executing (inside
        // GetVariables, about to block on the closed gate) before disposing — so the
        // refresh is provably suspended at the probe, holding the refresh gate, when
        // Dispose runs.
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)),
            "The parked refresh never reached the env-var probe.");

        vm.Dispose();

        // Release the probe; the parked refresh resumes and runs its finally{ Release() }.
        // It must complete cleanly: the gate is intentionally NOT disposed, so Release
        // succeeds. (Re-introducing _refreshGate.Dispose() in Dispose would make this
        // Release throw ObjectDisposedException and fault `parked`, failing this test —
        // which is exactly the regression this guards.)
        gate.Set();
        await parked; // must complete without faulting
    }
}