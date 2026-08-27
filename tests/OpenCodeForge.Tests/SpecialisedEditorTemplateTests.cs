using System.Xml.Linq;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Adapters;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// Every specialised editor this app can produce has a view registered for it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>Two halves that fail silently apart from each other.</b> An editor is wired up in
/// <see cref="OpenCodeEditorFactory"/> <i>and</i> given a view by an
/// <c>Application.DataTemplates</c> entry in <c>App.axaml</c>. Neither half references the other,
/// so registering an editor and forgetting its template compiles, runs, and renders the
/// view-model's <b>type name</b> where the editor should be. That reads as a broken page, gets
/// reported as one, and nothing in a Debug test run or a green build says otherwise.
/// </para>
/// <para>
/// So this drives the real bundled schemas through the real factory, and for every editor it
/// produces from an <c>OpenCode.*</c> assembly, requires a template. New specialised editors are
/// covered automatically as Phase 9 adds them — <c>mcp</c>, <c>agent</c>, <c>keybinds</c> — with no
/// registration here, which is the only way a per-editor guard stays honest.
/// </para>
/// <para>
/// Asserts over parsed <b>attribute values</b>, never over file text: a type named in an XML
/// comment describing what the templates used to hold would otherwise count as registration. That
/// exact mistake was made once already in this repo, by a test that searched raw text and matched
/// its own author's comment.
/// </para>
/// </remarks>
[TestClass]
public sealed class SpecialisedEditorTemplateTests
{
    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "tests")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root by walking up from '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    /// The type names <c>App.axaml</c> declares templates for, stripped of their xmlns prefix.
    /// </summary>
    private static HashSet<string> TemplatedTypeNames()
    {
        string appAxaml = Path.Combine(RepoRoot(), "src", "OpenCodeForge", "App.axaml");
        Assert.IsTrue(File.Exists(appAxaml), $"'{appAxaml}' not found.");

        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        return XDocument
            .Load(appAxaml)
            .Descendants(avalonia + "DataTemplate")
            .Select(t => (string?)t.Attribute(xaml + "DataType") ?? (string?)t.Attribute("DataType"))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Split(':')[^1].Trim())
            .ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<IReadOnlyList<SchemaNode>> TopLevelNodesAsync(
        SchemaRegistry registry,
        ProductDescriptor product)
    {
        var root = await registry.GetSettingsNodeAsync(product, CancellationToken.None)
            .ConfigureAwait(false);
        return SchemaTreeBuilder.BuildTopLevel(root);
    }

    [TestMethod]
    public async Task EverySpecialisedEditorTheFactoryProduces_HasADataTemplate()
    {
        SchemaRegistry registry = new();
        OpenCodeEditorFactory factory = new();
        HashSet<string> templated = TemplatedTypeNames();

        Assert.IsTrue(
            templated.Count > 0,
            "Parsed no DataTemplate DataType attributes out of App.axaml. Either the file no "
            + "longer declares them or this test stopped reading it — either way it guards nothing.");

        List<string> missing = [];
        List<string> specialised = [];
        int nodesChecked = 0;

        foreach (ProductDescriptor product in new[] { OpenCodeProducts.Config, OpenCodeProducts.Tui })
        {
            foreach (SchemaNode node in await TopLevelNodesAsync(registry, product))
            {
                nodesChecked++;
                PropertyEditorViewModel editor = factory.Create(node, ConfigScope.User);
                Type type = editor.GetType();

                // The library's generic editors are found by the library's own wrapper. Only this
                // product's own editors depend on an App.axaml entry.
                string? assembly = type.Assembly.GetName().Name;
                if (assembly is null || !assembly.StartsWith("OpenCode", StringComparison.Ordinal))
                {
                    continue;
                }

                specialised.Add($"{node.Name} -> {type.Name}");
                if (!templated.Contains(type.Name))
                {
                    missing.Add(
                        $"'{node.Name}' produces {type.Name}, which has no DataTemplate in App.axaml");
                }
            }
        }

        Assert.IsTrue(
            nodesChecked > 0,
            "No schema nodes were produced for either product, so no editor was ever created. "
            + "This test would pass without checking anything.");

        Assert.IsTrue(
            specialised.Count > 0,
            "The factory produced no product-specific editor for any property of either bundled "
            + "schema. Since Phase 9 registers the permission grid, that means the registration is "
            + "gone — and every complex OpenCode shape is silently back to raw JSON.");

        Assert.IsTrue(
            missing.Count == 0,
            $"{missing.Count} specialised editor(s) would render as their type name:\n  "
            + string.Join("\n  ", missing));
    }

    /// <remarks>
    /// The other half, asserted directly so a failure says which half broke. The test above would
    /// also go red if the factory stopped returning the editor at all, but it would report
    /// "no specialised editors" rather than naming <c>permission</c>.
    /// </remarks>
    [TestMethod]
    public async Task ThePermissionProperty_GetsThePermissionGrid_NotTheGenericObjectEditor()
    {
        SchemaRegistry registry = new();
        OpenCodeEditorFactory factory = new();

        SchemaNode permission = (await TopLevelNodesAsync(registry, OpenCodeProducts.Config))
            .SingleOrDefault(n => n.Name == "permission")
            ?? throw new AssertFailedException(
                "The bundled config schema has no 'permission' property. If the schema was "
                + "refreshed and the key moved or was renamed, the grid is now unreachable.");

        PropertyEditorViewModel editor = factory.Create(permission, ConfigScope.User);

        Assert.AreEqual(
            "OpenCodePermissionEditorViewModel",
            editor.GetType().Name,
            "The generic dispatch renders this faithfully as an object of strings, which hides the "
            + "only thing that decides the outcome: within a tool the LAST matching rule wins, so "
            + "the key order is the policy.");
    }
}
