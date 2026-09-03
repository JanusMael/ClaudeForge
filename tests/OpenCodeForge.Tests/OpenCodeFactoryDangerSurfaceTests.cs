using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Danger;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCodeForge.Adapters;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// This app's factory must surface the danger table it stamps onto its editors.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The shared group view-model reads <see cref="ISchemaEditorFactory.Danger"/> to classify
/// its effective-value rows</b>, so a factory that accepts a table but never exposes one produces
/// a page whose Properties tab shows severity and whose Effective tab shows none. Both halves
/// compile, both halves are individually correct, and nothing else goes red — the same
/// two-independent-halves shape the markup guard exists for.
/// </para>
/// <para>
/// ⚠ Lives here rather than beside the effective-row tests because
/// <c>ClaudeForge.Tests</c> does not reference this app; the Claude half of the same invariant is
/// asserted in <c>EffectiveRowDangerTests</c>.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeFactoryDangerSurfaceTests
{
    private static IDangerClassifier Table() => new TableDangerClassifier(
        new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["share"] = new() { Tier = AppSeverity.Critical, Why = "Uploads the session." },
        });

    [TestMethod]
    public void TheFactorySurfacesTheTableItWasGiven()
    {
        IDangerClassifier table = Table();
        ISchemaEditorFactory factory = new OpenCodeEditorFactory(table);

        Assert.AreSame(table, factory.Danger,
            "the effective-value view classifies through this property; a factory that keeps its "
            + "table private renders a page with severity on one tab and none on the other");
    }

    [TestMethod]
    public void AFactoryWithNoTableReportsNull()
    {
        // Silence, not a claim of safety: a section that declares no policy renders no dots.
        Assert.IsNull(((ISchemaEditorFactory)new OpenCodeEditorFactory()).Danger);
    }

    /// <summary>
    /// The surfaced table is the same instance the editors are stamped with, not a copy or a
    /// second lookup.
    /// </summary>
    [TestMethod]
    public void TheSurfacedTableIsTheOneAttachedToEditors()
    {
        IDangerClassifier table = Table();
        OpenCodeEditorFactory factory = new(table);

        PropertyEditorViewModel editor = factory.Create(
            new SchemaNode("share", "share") { ValueType = SchemaValueType.String },
            ConfigScope.User);

        // ⛔ Assert non-null FIRST. `AreSame(null, null)` passes, so without this the whole
        // assertion holds for a factory that simply dropped the table on the floor — which is
        // precisely the regression it exists to catch. Found by a canary that set the table to
        // null and left this test green.
        Assert.IsNotNull(factory.Danger);
        Assert.IsNotNull(editor.DangerClassifier);
        Assert.AreSame(factory.Danger, editor.DangerClassifier,
            "the two surfaces of one page must be driven by one table, by construction rather "
            + "than by convention");
    }
}
