using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Adapters;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// Every editor the factory produces must carry the document's danger classifier.
///
/// <para>
/// ⛔⛔ <b>The failure mode this guards is SILENT, and it looks like good news.</b>
/// <c>OpenCodeEditorFactory</c> has a dozen arms. An editor built without a classifier renders
/// with no severity dot and no banner — which is indistinguishable from "this product says
/// nothing is dangerous here". Nobody files a bug about a page that looks calm.
/// </para>
/// <para>
/// This is the same shape as <see cref="SpecialisedEditorTemplateTests"/>'s two-independent-halves
/// problem (a factory branch and an <c>App.axaml</c> DataTemplate that never reference each
/// other), so it gets the same treatment: drive <b>every top-level node of both bundled
/// schemas</b> through the real factory and assert on what comes back.
/// </para>
/// <para>
/// ⭐ The production code makes this hard to break in the first place — the classifier is
/// attached at ONE choke point in <c>Create</c> rather than in each arm's object initializer.
/// This test is what proves that choke point still covers every arm, including any added later.
/// </para>
/// </summary>
[TestClass]
public sealed class OpenCodeEditorDangerWiringTests
{
    private static async Task<IReadOnlyList<SchemaNode>> TopLevelNodesAsync(
        SchemaRegistry registry, ProductDescriptor product)
    {
        var root = await registry.GetSettingsNodeAsync(product, CancellationToken.None)
            .ConfigureAwait(false);
        return SchemaTreeBuilder.BuildTopLevel(root);
    }

    [TestMethod]
    public async Task EveryEditorTheFactoryProduces_CarriesTheDocumentsDangerClassifier()
    {
        SchemaRegistry registry = new();

        (ProductDescriptor Product, IDangerClassifier Danger, string Label)[] documents =
        [
            (OpenCodeProducts.Config, OpenCodeDangerTable.Config, "opencode.json"),
            (OpenCodeProducts.Tui, OpenCodeDangerTable.Tui, "tui.json"),
        ];

        List<string> unwired = [];
        List<string> wrongTable = [];
        int checked_ = 0;

        foreach ((ProductDescriptor product, IDangerClassifier danger, string label) in documents)
        {
            OpenCodeEditorFactory factory = new(danger);

            foreach (SchemaNode node in await TopLevelNodesAsync(registry, product))
            {
                checked_++;
                PropertyEditorViewModel editor = factory.Create(node, ConfigScope.User);

                if (editor.DangerClassifier is null)
                {
                    unwired.Add($"{label}:{node.Name} -> {editor.GetType().Name}");
                }
                else if (!ReferenceEquals(editor.DangerClassifier, danger))
                {
                    // ⚠ Not paranoia: the two documents' tables barely overlap, so handing an
                    // editor the OTHER document's policy would mislabel rows rather than leave
                    // them blank — a worse failure, and a quieter one.
                    wrongTable.Add($"{label}:{node.Name} got a different classifier instance");
                }
            }
        }

        // A scan that creates no editors proves nothing.
        Assert.IsTrue(checked_ >= 13,
            $"only {checked_} editors were created across both bundled schemas; config.json has 36 "
            + "top-level keys and tui.json has 13, so the schema reader or the factory is broken "
            + "and this test would otherwise pass without checking anything.");

        Assert.IsTrue(unwired.Count == 0,
            $"{unwired.Count} editor(s) came back with NO danger classifier, so their rows will "
            + $"show no severity at all:\n  {string.Join("\n  ", unwired)}\n\n"
            + "Attach it at the single choke point in OpenCodeEditorFactory.Create, not in the "
            + "individual arms.");

        Assert.IsTrue(wrongTable.Count == 0,
            $"{wrongTable.Count} editor(s) carry the wrong document's table:\n  "
            + string.Join("\n  ", wrongTable));
    }

    /// <summary>
    /// A factory built with no classifier must still produce working editors — a product that
    /// declares no danger policy is a supported state, not a crash.
    /// </summary>
    [TestMethod]
    public async Task AFactoryWithNoClassifier_StillProducesEditorsThatReportUnremarkable()
    {
        SchemaRegistry registry = new();
        OpenCodeEditorFactory factory = new(danger: null);

        IReadOnlyList<SchemaNode> nodes = await TopLevelNodesAsync(registry, OpenCodeProducts.Tui);
        Assert.IsTrue(nodes.Count > 0, "no nodes read from tui.json");

        foreach (SchemaNode node in nodes)
        {
            PropertyEditorViewModel editor = factory.Create(node, ConfigScope.User);

            Assert.IsNull(editor.DangerClassifier);
            Assert.AreEqual(DangerAssessment.Unremarkable, editor.Danger,
                $"'{node.Name}' must fall back to Unremarkable rather than throwing");
            Assert.IsFalse(editor.HasDangerSeverity);
            Assert.AreEqual(string.Empty, editor.DangerAccessibleText);
        }
    }

    /// <summary>
    /// The real wiring the app uses: each <c>HostedSection</c> supplies its own table.
    /// </summary>
    /// <remarks>
    /// ⚠ Pins that the two documents get DIFFERENT tables. A single shared factory — which is what
    /// this code looked like before — compiles, runs, and labels <c>tui.json</c> with
    /// <c>opencode.json</c>'s policy.
    /// </remarks>
    [TestMethod]
    public void TheTwoDocumentsDeclareDifferentDangerTables()
    {
        Assert.IsFalse(
            ReferenceEquals(OpenCodeDangerTable.Config, OpenCodeDangerTable.Tui),
            "config.json and tui.json must not share one table — almost none of their keys agree");

        // `plugin` is Critical in both, and it is the ONLY overlap worth having. `share` exists
        // only in config; `theme` only in tui. If these ever start agreeing, one is wrong.
        Assert.IsTrue(OpenCodeDangerTable.Config.ClassifiedPaths.Contains("share"));
        Assert.IsFalse(OpenCodeDangerTable.Tui.ClassifiedPaths.Contains("share"));
        Assert.IsTrue(OpenCodeDangerTable.Tui.ClassifiedPaths.Contains("theme"));
        Assert.IsFalse(OpenCodeDangerTable.Config.ClassifiedPaths.Contains("theme"));
    }
}
