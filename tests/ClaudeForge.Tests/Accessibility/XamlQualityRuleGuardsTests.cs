using System.Reflection;
using Bennewitz.Ninja.XamlQuality;
using Bennewitz.Ninja.XamlQuality.Rules;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Accessibility;

/// <summary>
/// Two XamlQuality rules adopted as guards because the tree already satisfies them: a regression
/// is the only way either can fail.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>BNXQ1008</c>, <see cref="ZeroSizeSlotRule"/>:</b> a control in a zero-size <c>Grid</c>
/// slot must be hidden by <c>IsVisible</c>, not by the slot alone. A slot of no size hides a
/// control from people but not from the automation tree: its peer stays, reporting an empty
/// rectangle and <c>IsOffscreen</c> false, so a screen reader or a test finds a control nobody can
/// see.
/// </para>
/// <para>
/// <b><c>BNXQ1009</c>, <see cref="ItemContainerNameRule"/>:</b> every item container generated
/// from <c>ItemsSource</c> is named by what it shows. Unnamed, a row falls back to its item's
/// <c>ToString()</c>, which for a view model that writes none is the type's full name. This repo
/// had exactly that on eight controls across three container types (fixed in <c>5500453</c> and
/// <c>5ad0e31</c>); the rule reads compiled types, so it is given the assemblies the markup binds.
/// </para>
/// <para>
/// ⛔ <b>Each asserts its premise</b>, <see cref="XamlRuleResult.Inspected"/> above zero, as
/// <see cref="ExpanderAutomationNameTests"/> does: a rule whose selector stops matching returns
/// no findings and would otherwise read as a pass.
/// </para>
/// </remarks>
public sealed class XamlQualityRuleGuardsTests
{
    [Fact]
    public void NoControlInAZeroSizeGridSlot_IsHiddenBySizeAlone()
    {
        XamlScanContext context = XamlScanContext.Load(SrcRoot());
        XamlRuleResult result = new ZeroSizeSlotRule().Analyze(context);

        Assert.True(
            result.Inspected > 0,
            "BNXQ1008 inspected no control in a Grid slot under src/. Either the markup moved or "
            + "the scan is looking in the wrong place; a green result here would mean nothing.");

        MessageAssert.Equal(
            0, result.Findings.Count,
            $"{result.Findings.Count} of {result.Inspected} control(s) sit in a zero-size Grid slot "
            + "without IsVisible saying they are hidden. Each stays in the automation tree with an "
            + $"empty rectangle. Offenders:{Environment.NewLine}"
            + string.Join(Environment.NewLine, result.Findings));
    }

    [Fact]
    public void EveryGeneratedItemContainer_IsNamedByWhatItShows()
    {
        XamlScanContext context = XamlScanContext.Load(SrcRoot()).WithAssemblies(MarkupAssemblies());
        XamlRuleResult result = new ItemContainerNameRule().Analyze(context);

        Assert.True(
            result.Inspected > 0,
            "BNXQ1009 inspected no ItemsSource host under src/. Either the markup moved, or the "
            + "assemblies it binds were not found beside the test; a green result here would mean "
            + "nothing.");

        MessageAssert.Equal(
            0, result.Findings.Count,
            $"{result.Findings.Count} of {result.Inspected} items control(s) generate rows a screen "
            + "reader names by a fallback, typically the item type's full name. Offenders:"
            + Environment.NewLine + string.Join(Environment.NewLine, result.Findings));
    }

    /// <summary>
    /// The assemblies the markup's <c>x:DataType</c>s resolve into: the app, its product-specific
    /// half, and the shared libraries whose view models it binds, loaded from the test output
    /// where the build already copied them.
    /// </summary>
    private static Assembly[] MarkupAssemblies()
    {
        string[] patterns = ["ClaudeForge.dll", "ClaudeForge.*.dll", "AgentForge.*.dll", "ScopedEditors.*.dll", "AppServices*.dll"];
        Assembly[] assemblies =
        [
            .. patterns
                .SelectMany(p => Directory.GetFiles(AppContext.BaseDirectory, p))
                .Where(path => !Path.GetFileName(path).Contains(".Tests", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => Assembly.Load(AssemblyName.GetAssemblyName(path))),
        ];

        Assert.Contains(assemblies, a => a.GetName().Name == "ClaudeForge");
        return assemblies;
    }

    private static string SrcRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "tests")))
            {
                return Path.Combine(dir, "src");
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root by walking up from '{AppContext.BaseDirectory}'.");
    }
}
