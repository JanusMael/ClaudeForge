using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Danger;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Danger;

/// <summary>
/// The effective-value view's severity column: assessed at the scope that WON, over the value
/// that won.
/// </summary>
/// <remarks>
/// <para>
/// ⭐⭐ <b>The load-bearing case is the one where this DISAGREES with the settings row.</b> The
/// obvious implementation — delegate to
/// <see cref="LayeredEditors.Avalonia.ViewModels.IDangerAnnotatedEditor.AssessDanger"/> the way a
/// search hit does — reports the assessment at the scope the user happens to be EDITING, which is
/// not the scope the runtime value came from. Every test here would still pass with that
/// implementation except <see cref="EffectiveRow_IsAssessedAtTheWinningScope_NotTheEditingScope"/>,
/// so that one is the test doing the work.
/// </para>
/// <para>
/// Built against a purpose-made table rather than <see cref="ClaudeDangerTable"/>: a product's
/// real table is whatever its policy needs, and pinning mechanism to it means the test starts
/// failing (or, worse, passing vacuously) whenever policy changes. That lesson was paid for once
/// already — a no-inherit test in this area stayed green through a broken mechanism because the
/// real key it chose had no predicate at all.
/// </para>
/// </remarks>
[TestClass]
public sealed class EffectiveRowDangerTests
{
    // A secret-shaped key: caution wherever you keep it, critical once it is in a file git
    // tracks. This is the shape that makes "which scope" observable at all — a rule with a flat
    // tier cannot tell the two implementations apart.
    private const string EscalatingKey = "apiKey";

    // A value-sensitive key, to prove the JSON value actually crosses into the editor currency
    // the predicates are written against.
    private const string ToggleKey = "dangerousSkipPermissions";

    private static IDangerClassifier Table() => new TableDangerClassifier(
        new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            [EscalatingKey] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "A credential in this file is readable by anything that reads the file.",
                EscalatesAt = id => string.Equals(id, ConfigScope.Project.Id, StringComparison.OrdinalIgnoreCase),
                EscalatedTier = AppSeverity.Critical,
            },
            [ToggleKey] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Skips the permission prompt for every tool call.",
                // long, not int, and bool stays bool: the editor value currency.
                Unsafe = v => v is true,
            },
            ["timeoutMs"] = new()
            {
                Tier = AppSeverity.Neutral,
                Why = "How long a request may run before it is abandoned.",
                Unsafe = v => v is long ms && ms > 60_000,
            },
        });

    private static SettingsWorkspace Workspace(params (ConfigScope Scope, string Json)[] entries) =>
        new(entries.Select(e => new SettingsDocument(
                e.Scope, $"{e.Scope}.json", (JsonObject)JsonNode.Parse(e.Json)!, isReadOnly: false)),
            ClaudeMergePolicy.Instance);

    private static SettingsGroupEditorViewModel Group(
        SettingsWorkspace workspace,
        IDangerClassifier? danger,
        ConfigScope editingScope,
        params SchemaNode[] nodes)
    {
        SettingsGroupEditorViewModel vm = new(
            "General",
            nodes,
            workspace,
            ClaudeEditorFactoryConfig.CreateDefault(danger: danger),
            ClaudeSettingsGroupText.Create(),
            initialScope: editingScope);
        return vm;
    }

    private static SchemaNode Node(string path, SchemaValueType type = SchemaValueType.String) =>
        new(path, path) { ValueType = type };

    private static EffectivePropertyRow Row(SettingsGroupEditorViewModel vm, string property) =>
        vm.EffectiveRows.Single(r => r.Property == property);

    // ── The case that separates the two possible implementations ──────────────

    /// <summary>
    /// The winning scope decides the tier, even when the user is editing somewhere else.
    /// </summary>
    /// <remarks>
    /// ⛔ Delegating to the editor would report Caution here — the editor is bound to
    /// <see cref="ConfigScope.User"/> — while the value the agent actually reads comes from the
    /// git-committed project file and is Critical. The whole column exists to say that.
    /// </remarks>
    [TestMethod]
    public void EffectiveRow_IsAssessedAtTheWinningScope_NotTheEditingScope()
    {
        SettingsWorkspace workspace = Workspace(
            (ConfigScope.User, $$"""{"{{EscalatingKey}}":"sk-user"}"""),
            (ConfigScope.Project, $$"""{"{{EscalatingKey}}":"sk-project"}"""));

        SettingsGroupEditorViewModel vm =
            Group(workspace, Table(), ConfigScope.User, Node(EscalatingKey));

        EffectivePropertyRow row = Row(vm, EscalatingKey);

        Assert.AreEqual(ConfigScope.Project, row.Scope,
            "precondition: the project file must be the one that wins, or this test proves nothing");
        Assert.AreEqual(AppSeverity.Critical, row.Danger.Severity,
            "the effective row must be assessed at the scope that WON (Project → escalated), not "
            + "at the scope being edited (User → Caution). Reporting the editing scope here is "
            + "what delegating to the editor would do, and it mislabels the runtime truth.");

        // …and the editor row, answering its own different question, still reads Caution. Both
        // are correct; only their questions differ.
        Assert.AreEqual(AppSeverity.Caution, vm.Editors.Single().Danger.Severity,
            "the settings row assesses the EDITING scope and must be unaffected by this column");
    }

    /// <summary>
    /// The same key, edited at the same scope that wins, reports the escalated tier too — so the
    /// divergence above is genuinely about the winner and not an off-by-one in the wiring.
    /// </summary>
    [TestMethod]
    public void EffectiveRow_AgreesWithTheEditorWhenTheEditedScopeIsTheWinner()
    {
        SettingsWorkspace workspace =
            Workspace((ConfigScope.Project, $$"""{"{{EscalatingKey}}":"sk-project"}"""));

        SettingsGroupEditorViewModel vm =
            Group(workspace, Table(), ConfigScope.Project, Node(EscalatingKey));

        Assert.AreEqual(AppSeverity.Critical, Row(vm, EscalatingKey).Danger.Severity);
        Assert.AreEqual(AppSeverity.Critical, vm.Editors.Single().Danger.Severity);
    }

    // ── Currency conversions: both are load-bearing, both fail silently ───────

    /// <summary>
    /// The effective value reaches the predicate as editor currency, not as a
    /// <c>JsonNode</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ A rule handed the raw node matches none of the type patterns it was written with and
    /// answers <c>false</c> — i.e. reports "nothing wrong right now" on a value that is wrong.
    /// Nothing else in the suite would notice.
    /// </remarks>
    [TestMethod]
    public void EffectiveRow_ValueReachesThePredicateInEditorCurrency()
    {
        SettingsWorkspace workspace = Workspace(
            (ConfigScope.User, $$"""{"{{ToggleKey}}":true,"timeoutMs":90000}"""));

        SettingsGroupEditorViewModel vm = Group(workspace, Table(), ConfigScope.User,
            Node(ToggleKey, SchemaValueType.Boolean),
            Node("timeoutMs", SchemaValueType.Integer));

        Assert.IsTrue(Row(vm, ToggleKey).Danger.IsDangerNow,
            "a JSON true must arrive as a bool");
        Assert.IsTrue(Row(vm, "timeoutMs").Danger.IsDangerNow,
            "a JSON integer must arrive as a long — an int-typed pattern would silently miss");
    }

    /// <summary>A safe value at a dangerous key keeps the tier and drops the banner.</summary>
    [TestMethod]
    public void EffectiveRow_SafeValueKeepsTheTierButIsNotDangerNow()
    {
        SettingsWorkspace workspace =
            Workspace((ConfigScope.User, $$"""{"{{ToggleKey}}":false}"""));

        SettingsGroupEditorViewModel vm =
            Group(workspace, Table(), ConfigScope.User, Node(ToggleKey, SchemaValueType.Boolean));

        EffectivePropertyRow row = Row(vm, ToggleKey);
        Assert.AreEqual(AppSeverity.Critical, row.Danger.Severity);
        Assert.IsFalse(row.Danger.IsDangerNow);
        Assert.IsTrue(row.HasDangerSeverity, "a triaged key still renders its dot");
    }

    // ── The path, not the display name ───────────────────────────────────────

    /// <summary>
    /// Classification uses <see cref="SchemaNode.JsonPath"/>, not the title shown in the
    /// Property column.
    /// </summary>
    /// <remarks>
    /// ⛔ The row's <c>Property</c> is <c>Title ?? Name</c> — a display string. Passing it to the
    /// classifier matches no rule, so every row silently reads unremarkable while the column
    /// still renders and every other test stays green. This is the cheapest way to break the
    /// feature and the hardest to notice.
    /// </remarks>
    [TestMethod]
    public void EffectiveRow_ClassifiesTheJsonPath_NotTheDisplayTitle()
    {
        SettingsWorkspace workspace =
            Workspace((ConfigScope.User, $$"""{"{{ToggleKey}}":true}"""));

        SchemaNode titled = new(ToggleKey, ToggleKey)
        {
            ValueType = SchemaValueType.Boolean,
            Title = "Skip Permission Prompts",
        };

        SettingsGroupEditorViewModel vm = Group(workspace, Table(), ConfigScope.User, titled);

        EffectivePropertyRow row = vm.EffectiveRows.Single();
        Assert.AreEqual("Skip Permission Prompts", row.Property,
            "precondition: the display column must differ from the path, or this proves nothing");
        Assert.AreEqual(AppSeverity.Critical, row.Danger.Severity,
            "the title matches no rule; classification must use JsonPath");
    }

    // ── Absence of a table is silence, not a claim of safety ─────────────────

    /// <summary>
    /// A section with no danger table renders rows with no dot, exactly as before this column
    /// existed.
    /// </summary>
    [TestMethod]
    public void EffectiveRow_WithoutATable_IsUnremarkable()
    {
        SettingsWorkspace workspace =
            Workspace((ConfigScope.User, $$"""{"{{ToggleKey}}":true}"""));

        SettingsGroupEditorViewModel vm = Group(workspace, danger: null, ConfigScope.User,
            Node(ToggleKey, SchemaValueType.Boolean));

        EffectivePropertyRow row = vm.EffectiveRows.Single();
        Assert.AreSame(DangerAssessment.Unremarkable, row.Danger);
        Assert.IsFalse(row.HasDangerSeverity);
        Assert.AreEqual(string.Empty, row.DangerAccessibleText);
    }

    /// <summary>
    /// A key the table has no opinion about renders no dot even when a table is present.
    /// </summary>
    [TestMethod]
    public void EffectiveRow_UnclassifiedKey_RendersNoDot()
    {
        SettingsWorkspace workspace = Workspace((ConfigScope.User, """{"model":"sonnet"}"""));

        SettingsGroupEditorViewModel vm =
            Group(workspace, Table(), ConfigScope.User, Node("model"));

        Assert.IsFalse(vm.EffectiveRows.Single().HasDangerSeverity);
    }

    // ── Accessible text ──────────────────────────────────────────────────────

    /// <summary>
    /// The dot is a coloured shape and conveys nothing to a screen reader on its own, so the row
    /// names the tier and the consequence.
    /// </summary>
    [TestMethod]
    public void EffectiveRow_AccessibleTextNamesTheTierAndTheConsequence()
    {
        SettingsWorkspace workspace =
            Workspace((ConfigScope.User, $$"""{"{{ToggleKey}}":true}"""));

        SettingsGroupEditorViewModel vm =
            Group(workspace, Table(), ConfigScope.User, Node(ToggleKey, SchemaValueType.Boolean));

        EffectivePropertyRow row = vm.EffectiveRows.Single();
        Assert.AreEqual(
            "Critical: Skips the permission prompt for every tool call.",
            row.DangerAccessibleText);
    }

    // ── The classifier comes from the factory, structurally ──────────────────

    /// <summary>
    /// The group view-model reads its classifier off the factory that built its editors, so the
    /// Effective tab and the Properties tab of one page cannot be driven by different tables.
    /// </summary>
    /// <remarks>
    /// ⭐ This is the reason <see cref="ISchemaEditorFactory.Danger"/> exists rather than a second
    /// constructor parameter: a separately-injected classifier would let one page contradict
    /// itself, and no test could reasonably be expected to catch a caller passing two different
    /// tables.
    /// </remarks>
    [TestMethod]
    public void TheGroupEditorTakesItsClassifierFromTheFactory()
    {
        IDangerClassifier table = Table();
        CompositeEditorFactory factory = ClaudeEditorFactoryConfig.CreateDefault(danger: table);

        Assert.AreSame(table, ((ISchemaEditorFactory)factory).Danger,
            "the factory must surface the very classifier it stamps onto its editors");

        SettingsWorkspace workspace =
            Workspace((ConfigScope.User, $$"""{"{{ToggleKey}}":true}"""));
        SettingsGroupEditorViewModel vm = new(
            "General",
            [Node(ToggleKey, SchemaValueType.Boolean)],
            workspace,
            factory,
            ClaudeSettingsGroupText.Create());

        Assert.AreEqual(AppSeverity.Critical, vm.EffectiveRows.Single().Danger.Severity,
            "the effective row must be classified by the factory's table without the view-model "
            + "being handed one separately");
    }

    // ── The OTHER producer of these rows ─────────────────────────────────────

    /// <summary>
    /// ClaudeForge renders effective values through <b>two unrelated producers</b>: the group
    /// editor's Effective tab (above) and the standalone Effective Settings page. They share the
    /// row type and nothing else — different view-models, different data sources, different
    /// classifier wiring — so a fix applied to one leaves the other silent.
    /// </summary>
    [TestClass]
    public sealed class StandalonePage
    {
        private static AgentConfigClientCore Client(params (ConfigScope Scope, string Json)[] entries)
        {
            SettingsWorkspace ws = new(
                entries.Select(e => new SettingsDocument(
                    e.Scope, $"{e.Scope}.json", (JsonObject)JsonNode.Parse(e.Json)!, isReadOnly: false)),
                ClaudeMergePolicy.Instance);

            return ClaudeCodeClient.FromExistingWorkspace(
                ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
        }

        private static EffectivePropertyRow Row(EffectiveSettingsViewModel vm, string property) =>
            vm.PropertyRows.Single(r => r.Property == property);

        /// <summary>
        /// This page has no editing scope of its own at all, so the winning scope is the only
        /// scope it could use — but it still has to look it up rather than pass
        /// <see langword="null"/>, or the escalation never fires.
        /// </summary>
        [TestMethod]
        public void Rows_AreAssessedAtTheWinningScope()
        {
            EffectiveSettingsViewModel vm = new(
                Client(
                    (ConfigScope.User, $$"""{"{{EscalatingKey}}":"sk-user"}"""),
                    (ConfigScope.Project, $$"""{"{{EscalatingKey}}":"sk-project"}""")),
                danger: Table());

            EffectivePropertyRow row = Row(vm, EscalatingKey);

            Assert.AreEqual(ConfigScope.Project, row.Scope,
                "precondition: the project file must win, or this test proves nothing");
            Assert.AreEqual(AppSeverity.Critical, row.Danger.Severity,
                "passing a null scope here would silently drop every escalation — an unknown "
                + "scope must never raise severity, so the omission reads as 'safe'");
        }

        /// <summary>
        /// The value crosses into editor currency here too. This page's own conversion is a
        /// separate line of code from the group editor's, so it needs its own proof.
        /// </summary>
        [TestMethod]
        public void Rows_ValueReachesThePredicateInEditorCurrency()
        {
            EffectiveSettingsViewModel vm = new(
                Client((ConfigScope.User, $$"""{"{{ToggleKey}}":true,"timeoutMs":90000}""")),
                danger: Table());

            Assert.IsTrue(Row(vm, ToggleKey).Danger.IsDangerNow, "a JSON true must arrive as a bool");
            Assert.IsTrue(Row(vm, "timeoutMs").Danger.IsDangerNow, "a JSON integer must arrive as a long");
        }

        [TestMethod]
        public void Rows_WithoutATable_AreUnremarkable()
        {
            EffectiveSettingsViewModel vm = new(
                Client((ConfigScope.User, $$"""{"{{ToggleKey}}":true}""")));

            Assert.AreSame(DangerAssessment.Unremarkable, Row(vm, ToggleKey).Danger);
        }

        /// <summary>
        /// The keys this page classifies are the ones the classifier matches exactly — so its
        /// rules' value predicates and scope escalation genuinely run, rather than the tier-only
        /// answer an inherited (ancestor) match would give.
        /// </summary>
        [TestMethod]
        public void Rows_AreExactMatches_SoPredicatesActuallyRun()
        {
            EffectiveSettingsViewModel vm = new(
                Client((ConfigScope.User, $$"""{"{{ToggleKey}}":false}""")),
                danger: Table());

            EffectivePropertyRow row = Row(vm, ToggleKey);
            Assert.AreEqual(AppSeverity.Critical, row.Danger.Severity);
            Assert.IsFalse(row.Danger.IsDangerNow,
                "an inherited match reports IsDangerNow false unconditionally, so this assertion "
                + "only means something alongside the true case above");
        }
    }
}
