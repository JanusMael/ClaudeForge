using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Danger;

/// <summary>
/// Every editor the Claude factory produces must carry the product's danger classifier.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>The failure mode this guards is SILENT, and it looks like good news.</b> An editor built
/// without a classifier renders with no severity dot and no banner — indistinguishable from "this
/// product says nothing here is dangerous". Nobody files a bug about a page that looks calm.
/// </para>
/// <para>
/// ⛔ <b>The specialised arms are the ones that matter.</b>
/// <c>CompositeEditorFactory.Create</c> returns early on a registration match without calling
/// <c>base.Create</c>, so an attach placed in the base would have covered the generic editors and
/// missed every specialised one — hooks, permissions, MCP servers, plugins, marketplaces. Those
/// are precisely the pages holding the dangerous keys, so the plausible-looking placement is the
/// one that would have shipped a blank Permissions page. This test drives the real registrations,
/// by name, to pin that.
/// </para>
/// <para>
/// ⭐ The production code makes it hard to break: the classifier is attached at ONE site in
/// <c>Create</c> rather than in each arm. This test proves that site still covers every arm,
/// including any added later.
/// </para>
/// </remarks>
[TestClass]
public sealed class ClaudeEditorDangerWiringTests
{
    private static async Task<IReadOnlyList<SchemaNode>> TopLevelNodesAsync()
    {
        SchemaRegistry registry = new();
        var root = await registry
            .GetSettingsNodeAsync(SchemaRegistry.ClaudeCodeProduct, CancellationToken.None)
            .ConfigureAwait(false);
        return SchemaTreeBuilder.BuildTopLevel(root);
    }

    [TestMethod]
    public async Task EveryEditorTheFactoryProducesCarriesTheDangerTable()
    {
        CompositeEditorFactory factory =
            ClaudeEditorFactoryConfig.CreateDefault(danger: ClaudeDangerTable.Settings);
        IReadOnlyList<SchemaNode> nodes = await TopLevelNodesAsync().ConfigureAwait(false);

        List<string> unwired = [];
        List<string> wrongTable = [];

        foreach (SchemaNode node in nodes)
        {
            LibVm.PropertyEditorViewModel editor = factory.Create(node, ConfigScope.User);

            if (editor.DangerClassifier is null)
            {
                unwired.Add($"{node.Name} -> {editor.GetType().Name}");
            }
            else if (!ReferenceEquals(editor.DangerClassifier, ClaudeDangerTable.Settings))
            {
                wrongTable.Add($"{node.Name} got a classifier that is not ClaudeDangerTable.Settings");
            }
        }

        // A scan that creates no editors proves nothing. The schema had 142 top-level keys when
        // this was written; the floor is deliberately far below that so an upstream trim does not
        // fail the build, but far above zero.
        Assert.IsTrue(nodes.Count >= 100,
            $"only {nodes.Count} editors were created from claude-code-settings.json; the schema "
            + "reader or the factory is broken, and this test would otherwise pass having checked "
            + "almost nothing.");

        Assert.IsTrue(unwired.Count == 0,
            $"{unwired.Count} editor(s) came back with NO danger classifier, so their rows show "
            + $"no severity at all:\n  {string.Join("\n  ", unwired)}\n\n"
            + "Attach it at the single site in CompositeEditorFactory.Create, not in the "
            + "individual registrations.");

        Assert.IsTrue(wrongTable.Count == 0,
            $"{wrongTable.Count} editor(s) carry the wrong table:\n  {string.Join("\n  ", wrongTable)}");
    }

    /// <summary>
    /// The specialised registrations, named individually — the arms that bypass
    /// <c>base.Create</c>.
    /// </summary>
    /// <remarks>
    /// ⓘ <c>mcpServers</c> is deliberately absent from this list even though it IS a registered
    /// specialised editor. It is a top-level key of <c>claude-desktop-config.json</c>, not of
    /// <c>claude-code-settings.json</c> — the one factory type serves both products — so it never
    /// appears in the node list this test walks. Measured, after the first draft asserted it here
    /// and failed.
    /// </remarks>
    [TestMethod]
    [DataRow("hooks")]
    [DataRow("permissions")]
    [DataRow("enabledPlugins")]
    [DataRow("extraKnownMarketplaces")]
    public async Task ASpecialisedEditorCarriesTheDangerTableToo(string key)
    {
        CompositeEditorFactory factory =
            ClaudeEditorFactoryConfig.CreateDefault(danger: ClaudeDangerTable.Settings);
        IReadOnlyList<SchemaNode> nodes = await TopLevelNodesAsync().ConfigureAwait(false);

        SchemaNode? node = nodes.FirstOrDefault(n => n.Name == key);
        Assert.IsNotNull(node,
            $"'{key}' is not a top-level schema node any more, so this registration may be dead — "
            + "check ClaudeEditorFactoryConfig.Register before deleting the row.");

        LibVm.PropertyEditorViewModel editor = factory.Create(node, ConfigScope.User);

        Assert.AreSame(ClaudeDangerTable.Settings, editor.DangerClassifier,
            $"The specialised editor for '{key}' ({editor.GetType().Name}) came back without the "
            + "danger table. Its registration returns before base.Create, so an attach in the "
            + "base class does not reach it.");
    }

    /// <summary>
    /// A factory built with no classifier must still produce working editors — a caller that
    /// declares no danger policy is a supported state, not a crash.
    /// </summary>
    [TestMethod]
    public async Task AFactoryWithNoClassifierStillProducesEditorsThatReportUnremarkable()
    {
        CompositeEditorFactory factory = ClaudeEditorFactoryConfig.CreateDefault(danger: null);
        IReadOnlyList<SchemaNode> nodes = await TopLevelNodesAsync().ConfigureAwait(false);

        // ⛔ This is the CLAUDE DESKTOP path, not a hypothetical. `BuildGroups` is called once per
        // product section and Desktop passes no table, so "no policy" has to keep working — and
        // it must stay the DEFAULT, so that a future third section cannot silently inherit Claude
        // Code's policy just by forgetting the argument.
        foreach (SchemaNode node in nodes.Take(20))
        {
            LibVm.PropertyEditorViewModel editor = factory.Create(node, ConfigScope.User);

            Assert.IsNull(editor.DangerClassifier,
                $"'{node.Name}' got a classifier from a factory explicitly built without one.");
            Assert.AreEqual(DangerAssessment.Unremarkable, editor.Danger,
                $"'{node.Name}' must fall back to Unremarkable rather than throwing.");
            Assert.IsFalse(editor.HasDangerSeverity);
        }
    }

    /// <summary>
    /// The dot the user sees on a real page, through the real factory and the real table.
    /// </summary>
    [TestMethod]
    public async Task ThePermissionsRowReportsCriticalThroughTheRealFactory()
    {
        CompositeEditorFactory factory =
            ClaudeEditorFactoryConfig.CreateDefault(danger: ClaudeDangerTable.Settings);
        IReadOnlyList<SchemaNode> nodes = await TopLevelNodesAsync().ConfigureAwait(false);

        SchemaNode node = nodes.First(n => n.Name == "permissions");
        LibVm.PropertyEditorViewModel editor = factory.Create(node, ConfigScope.User);

        Assert.AreEqual(AppSeverity.Critical, editor.Danger.Severity);
        Assert.IsTrue(editor.HasDangerSeverity, "The wrapper renders the dot on this flag.");
        Assert.IsTrue(editor.DangerAccessibleText.StartsWith("Critical:", StringComparison.Ordinal),
            $"A screen reader must hear the tier and the reason, got '{editor.DangerAccessibleText}'.");
    }
}
