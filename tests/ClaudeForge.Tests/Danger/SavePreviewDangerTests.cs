using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Danger;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Save;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.AgentForge.Sdk.Diagnostics;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Danger;

/// <summary>
/// The save-confirmation dialog's severity: the last screen before a value reaches disk.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ Like the effective view and unlike search, this surface <b>classifies</b>: a pending change
/// is a (path, target scope, new value) triple. There is also no editor to ask — the dialog is
/// built from document snapshots, and the change may have come from a page that is no longer open.
/// </para>
/// <para>
/// Built against a purpose-made table rather than <see cref="Adapters.ClaudeDangerTable"/>: a
/// product's real table is whatever policy needs, and pinning mechanism to it makes the test rot
/// — or pass vacuously — whenever policy changes.
/// </para>
/// </remarks>
[TestClass]
public sealed class SavePreviewDangerTests
{
    private const string ArrayKey = "permissions";
    private const string ToggleKey = "dangerouslySkipPermissions";
    private const string SecretKey = "apiKeyHelper";

    private static SaveDialogText Text => ClaudeSaveDialogText.Create();

    private static IDangerClassifier Table() => new TableDangerClassifier(
        new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            [ToggleKey] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Skips the permission prompt for every tool call.",
                Unsafe = v => v is true,
            },
            // A rule written for a LIST — the shape that catches the fragment bug below.
            [ArrayKey] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Decides which tools may run without asking.",
                Unsafe = v => v is IReadOnlyList<object?> items
                              && items.Any(i => i is string s && s.Contains('*', StringComparison.Ordinal)),
            },
            [SecretKey] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "A credential helper, stored in this file.",
                EscalatesAt = id => string.Equals(id, ConfigScope.Project.Id, StringComparison.OrdinalIgnoreCase),
                EscalatedTier = AppSeverity.Critical,
            },
        });

    /// <summary>A client whose in-memory state differs from its baseline by the given writes.</summary>
    /// <param name="baseline">
    /// What the file already holds. ⚠ Not decoration: <c>JsonDiff</c> only emits the
    /// element-wise array diff — the one that carries a FRAGMENT as its value — when the key is
    /// an array on <b>both</b> sides. A key absent from the baseline produces an <c>Added</c> row
    /// carrying the whole array instead, which would let
    /// <see cref="ArrayChange_IsClassifiedAgainstTheWholeArray_NotTheChangedElement"/> pass
    /// against the buggy implementation.
    /// </param>
    private static AgentConfigClientCore Dirty(
        ConfigScope scope,
        JsonObject? baseline = null,
        params (string Key, JsonNode? Value)[] writes)
    {
        SettingsDocument doc = new(scope, $"{scope}.json", baseline ?? new JsonObject(), isReadOnly: false);
        SettingsWorkspace ws = new([doc], ClaudeMergePolicy.Instance);
        AgentConfigClientCore client = ClaudeCodeClient.FromExistingWorkspace(
            ws, scope, schemaRegistry: new SchemaRegistry());

        foreach ((string key, JsonNode? value) in writes)
        {
            ws.SetValue(key, value, scope);
        }

        return client;
    }

    private static SaveChangeEntryViewModel Entry(SaveChangesDialogViewModel dlg, string key) =>
        dlg.Sections.SelectMany(s => s.Entries).Single(e => e.Key == key);

    // ── The fragment bug: the reason the value does not come from the diff ────

    /// <summary>
    /// An array change is classified against the WHOLE array, not the one element the diff
    /// carries.
    /// </summary>
    /// <remarks>
    /// ⛔⛔ <b>This is the test doing the work in this file.</b> <c>JsonDiff</c> emits the array's
    /// path as the key but only the added element as <see cref="PropertyDiff.NewValue"/>. A rule
    /// written for the list — which is how a permissions rule must be written — would be handed a
    /// bare string, match no type pattern, and answer "nothing wrong right now". That is a silent
    /// false negative on exactly the keys this dialog exists to catch, and every other assertion
    /// here passes under it.
    /// </remarks>
    [TestMethod]
    public void ArrayChange_IsClassifiedAgainstTheWholeArray_NotTheChangedElement()
    {
        // The key must already BE an array for the element-wise diff to fire — see Dirty().
        JsonObject baseline = new() { [ArrayKey] = new JsonArray("Bash(git status)") };
        AgentConfigClientCore client = Dirty(ConfigScope.User, baseline,
            (ArrayKey, new JsonArray("Bash(git status)", "Bash(rm -rf *)")));

        SaveChangesDialogViewModel? dlg =
            SaveDialogBuilder.Build([new DirtySource(client, "Claude Code", Table())], Text);

        Assert.IsNotNull(dlg);
        SaveChangeEntryViewModel entry = Entry(dlg!, ArrayKey);

        // Premise: this really is the fragment-carrying diff shape, not a whole-value Added row.
        Assert.AreEqual(ChangeKind.Added, entry.Kind);
        Assert.AreEqual("\"Bash(rm -rf *)\"", entry.FullNewValue,
            "precondition: the diff carries ONE ELEMENT, not the array. If this ever becomes the "
            + "whole array, the assertion below stops testing anything.");

        Assert.AreEqual(AppSeverity.Critical, entry.Danger.Severity);
        Assert.IsTrue(entry.Danger.IsDangerNow,
            "the predicate must receive the whole array (IReadOnlyList) as it will exist on disk. "
            + "Passing PropertyDiff.NewValue hands it a single element's string, which matches no "
            + "list pattern and reports the change safe.");
    }

    // ── The ordinary cases ───────────────────────────────────────────────────

    [TestMethod]
    public void UnsafeScalarChange_IsFlaggedAsDangerNow()
    {
        AgentConfigClientCore client = Dirty(ConfigScope.User, null, (ToggleKey, JsonValue.Create(true)));

        SaveChangesDialogViewModel? dlg =
            SaveDialogBuilder.Build([new DirtySource(client, "Claude Code", Table())], Text);

        SaveChangeEntryViewModel entry = Entry(dlg!, ToggleKey);
        Assert.AreEqual(AppSeverity.Critical, entry.Danger.Severity);
        Assert.IsTrue(entry.Danger.IsDangerNow, "a JSON true must reach the predicate as a bool");
        Assert.IsTrue(entry.HasDangerSeverity);
        Assert.AreEqual(
            "Critical: Skips the permission prompt for every tool call.",
            entry.DangerAccessibleText);
    }

    [TestMethod]
    public void SafeValueAtADangerousKey_KeepsTheTierButIsNotDangerNow()
    {
        AgentConfigClientCore client = Dirty(ConfigScope.User, null, (ToggleKey, JsonValue.Create(false)));

        SaveChangesDialogViewModel? dlg =
            SaveDialogBuilder.Build([new DirtySource(client, "Claude Code", Table())], Text);

        SaveChangeEntryViewModel entry = Entry(dlg!, ToggleKey);
        Assert.AreEqual(AppSeverity.Critical, entry.Danger.Severity);
        Assert.IsFalse(entry.Danger.IsDangerNow);
        Assert.IsTrue(entry.HasDangerSeverity, "a triaged key still renders its dot");
    }

    /// <summary>
    /// The scope used is the DOCUMENT's — the file being written — which is what makes escalation
    /// meaningful on this surface.
    /// </summary>
    [TestMethod]
    public void EscalationUsesTheScopeOfTheFileBeingWritten()
    {
        SaveChangesDialogViewModel? atUser = SaveDialogBuilder.Build(
            [new DirtySource(Dirty(ConfigScope.User, null, (SecretKey, JsonValue.Create("helper"))),
                "Claude Code", Table())], Text);
        SaveChangesDialogViewModel? atProject = SaveDialogBuilder.Build(
            [new DirtySource(Dirty(ConfigScope.Project, null, (SecretKey, JsonValue.Create("helper"))),
                "Claude Code", Table())], Text);

        Assert.AreEqual(AppSeverity.Caution, Entry(atUser!, SecretKey).Danger.Severity);
        Assert.AreEqual(AppSeverity.Critical, Entry(atProject!, SecretKey).Danger.Severity,
            "writing the same credential into the git-committed project file is the case that "
            + "escalates, and this dialog is the last moment anyone can stop it");
    }

    [TestMethod]
    public void WithoutATable_EntriesAreUnremarkable()
    {
        AgentConfigClientCore client = Dirty(ConfigScope.User, null, (ToggleKey, JsonValue.Create(true)));

        SaveChangesDialogViewModel? dlg =
            SaveDialogBuilder.Build([new DirtySource(client, "Claude Code")], Text);

        SaveChangeEntryViewModel entry = Entry(dlg!, ToggleKey);
        Assert.AreSame(DangerAssessment.Unremarkable, entry.Danger);
        Assert.IsFalse(entry.HasDangerSeverity);
        Assert.IsFalse(dlg!.HasUnsafeChanges);
    }

    // ── The per-source policy: the reason DirtySource exists ─────────────────

    /// <summary>
    /// Each source is classified by ITS OWN table, so one product's pending writes are never
    /// labelled with another product's threat model.
    /// </summary>
    /// <remarks>
    /// ⛔⛔ ClaudeForge hands this dialog Claude Code AND Claude Desktop in one call. A single
    /// classifier parameter on the builder would apply Claude Code's policy to Desktop's changes —
    /// quiet for most keys, since the schemas barely overlap, and confidently wrong on any that
    /// collide.
    /// </remarks>
    [TestMethod]
    public void EachSourceIsClassifiedByItsOwnTable()
    {
        SaveChangesDialogViewModel? dlg = SaveDialogBuilder.Build(
            [
                new DirtySource(Dirty(ConfigScope.User, null, (ToggleKey, JsonValue.Create(true))),
                    "Has A Table", Table()),
                new DirtySource(Dirty(ConfigScope.User, null, (ToggleKey, JsonValue.Create(true))),
                    "Has No Table"),
            ],
            Text);

        Assert.IsNotNull(dlg);
        SaveChangeSectionViewModel withTable = dlg!.Sections.Single(s => s.WorkspaceName == "Has A Table");
        SaveChangeSectionViewModel without = dlg.Sections.Single(s => s.WorkspaceName == "Has No Table");

        Assert.AreEqual(AppSeverity.Critical,
            withTable.Entries.Single(e => e.Key == ToggleKey).Danger.Severity);
        Assert.IsFalse(without.Entries.Single(e => e.Key == ToggleKey).HasDangerSeverity,
            "the second product declares no policy, so the identical key must carry no severity — "
            + "borrowing the first product's table would be a false claim, not a convenience");
    }

    // ── The headline ─────────────────────────────────────────────────────────

    /// <summary>
    /// The headline counts values that ARE unsafe, not settings that merely matter.
    /// </summary>
    /// <remarks>
    /// ⚠ Counting the tier instead would fire on almost every real save — a banner nobody reads,
    /// which is the failure the row-level banner was designed around too.
    /// </remarks>
    [TestMethod]
    public void TheHeadlineCountsUnsafeValues_NotMerelyDangerousKeys()
    {
        AgentConfigClientCore client = Dirty(ConfigScope.User, null,
            (ToggleKey, JsonValue.Create(true)),          // Critical AND unsafe
            (ArrayKey, new JsonArray("Bash(git status)")), // Critical, safe value
            (SecretKey, JsonValue.Create("helper")));      // Caution, no predicate

        SaveChangesDialogViewModel? dlg =
            SaveDialogBuilder.Build([new DirtySource(client, "Claude Code", Table())], Text);

        Assert.IsNotNull(dlg);
        Assert.AreEqual(3, dlg!.Sections.SelectMany(s => s.Entries).Count(e => e.HasDangerSeverity),
            "precondition: all three keys are triaged and carry a dot");
        Assert.AreEqual(1, dlg.UnsafeChangeCount,
            "only the toggle holds an unsafe value; the array's value is safe and the credential "
            + "key has no predicate at all");
        Assert.IsTrue(dlg.HasUnsafeChanges);
        Assert.IsTrue(dlg.UnsafeChangeWarning.Contains('1', StringComparison.Ordinal));
    }

    // ── One statement of the policy, read by two consumers ───────────────────

    /// <summary>
    /// The danger table is stated once, on the product's section, and both consumers read it
    /// there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔⛔ <b>Two unrelated code paths consume this</b> — the settings pages via
    /// <c>NavigationTreeBuilder.BuildGroups</c> and the save dialog via <c>DirtySources()</c> —
    /// and before this slice each named <c>ClaudeDangerTable.Settings</c> as a literal at its own
    /// call site. Two literals agree only by vigilance, and the drift is silent: the wrong table
    /// shows nothing on most rows (the schemas barely overlap) and a confident mislabel on the few
    /// that collide.
    /// </para>
    /// <para>
    /// ⚠ Claude Desktop's <see langword="null"/> is asserted, not incidental. Nobody has triaged
    /// that product's keys, and inheriting Claude Code's would be a false claim rather than a
    /// convenient default — the same rule that keeps
    /// <c>ClaudeEditorFactoryConfig.CreateDefault</c> classifier-free.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EachProductSectionCarriesItsOwnPolicy_StatedOnce()
    {
        MainWindowViewModel vm = new(new SchemaRegistry(), new NullDialogService());

        ProductSection code = vm.Sections.Single(
            s => s.Product.Id == SchemaRegistry.ClaudeCodeProduct.Id);
        ProductSection desktop = vm.Sections.Single(
            s => s.Product.Id == SchemaRegistry.ClaudeDesktopProduct.Id);

        Assert.IsNotNull(code.Danger,
            "Claude Code's section must carry its table — this is the single place it is stated, "
            + "and both the settings pages and the save dialog read it from here");
        Assert.IsNull(desktop.Danger,
            "Claude Desktop has no triaged table. Reusing Claude Code's would label one product "
            + "with another's threat model.");
    }

    [TestMethod]
    public void TheHeadlineIsHiddenWhenNothingPendingIsUnsafe()
    {
        AgentConfigClientCore client = Dirty(ConfigScope.User, null,
            (ArrayKey, new JsonArray("Bash(git status)")));

        SaveChangesDialogViewModel? dlg =
            SaveDialogBuilder.Build([new DirtySource(client, "Claude Code", Table())], Text);

        Assert.AreEqual(0, dlg!.UnsafeChangeCount);
        Assert.IsFalse(dlg.HasUnsafeChanges);
        Assert.AreEqual(string.Empty, dlg.UnsafeChangeWarning);
    }

    /// <summary>
    /// Inert dialog service — this test only reads the section list the constructor builds, and
    /// never opens anything.
    /// </summary>
    private sealed class NullDialogService : IDialogService
    {
        public Task<string?> PickFolderAsync(string? title = null) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? title = null, IReadOnlyList<FilePickerFilter>? filters = null) =>
            Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string? title, string defaultFileName,
                                               IReadOnlyList<FilePickerFilter>? filters = null) =>
            Task.FromResult<string?>(null);

        public Task ShowAlertAsync(string title, string message) => Task.CompletedTask;

        public Task<string?> ShowInputAsync(string title, string prompt, string? placeholder = null) =>
            Task.FromResult<string?>(null);

        public Task<bool?> ShowConfirmAsync(string title, string message, string confirmLabel = "Confirm",
                                            string cancelLabel = "Cancel") => Task.FromResult<bool?>(false);

        public Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt) => Task.FromResult(false);
    }
}
