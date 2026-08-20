using System.Reflection;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Localization;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Localization;

/// <summary>
/// The editor library's fallback chrome strings must not name any product.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="WrapperStrings.Resolver"/> is what a host overrides to supply its own localized
/// text. Until it does — and a host can simply forget — every wrapper renders these English
/// defaults. One of them read <i>"Undocumented setting — not in official Claude documentation"</i>,
/// so any second app built on this library would have shipped a competitor's brand in its own
/// tooltips.
/// </para>
/// <para>
/// It is the perfect silent defect: the string only appears on hover, over one small badge, in a
/// fallback path the primary app never exercises because it wires a resolver. No build breaks and
/// no test fails. Hence a test rather than a comment.
/// </para>
/// </remarks>
[TestClass]
public sealed class WrapperStringsNeutralityTests
{
    /// <summary>
    /// Brand names that must never appear in a shared library's default strings. Deliberately
    /// includes this repo's own first product: the point is that the <i>library</i> stays
    /// neutral, not that it avoids some other vendor.
    /// </summary>
    private static readonly string[] ProductBrands =
        ["Claude", "Anthropic", "OpenCode", "ClaudeForge", "OpenCodeForge"];

    private static IEnumerable<(string Name, string Value)> DefaultStrings()
    {
        // Read through the public static properties rather than a hand-kept key list, so a
        // newly added string is covered the moment it exists. A hardcoded list would leave
        // every future addition unguarded — which is how the original string survived.
        foreach (PropertyInfo p in typeof(WrapperStrings)
                     .GetProperties(BindingFlags.Public | BindingFlags.Static)
                     .Where(p => p.PropertyType == typeof(string)))
        {
            yield return (p.Name, (string?)p.GetValue(null) ?? string.Empty);
        }
    }

    [TestMethod]
    public void ScanFindsStrings_SoThisTestIsNotVacuous()
    {
        Assert.IsTrue(
            DefaultStrings().Count() >= 5,
            "Expected several default chrome strings. If the reflection no longer finds them, "
            + "this test passes without checking anything.");
    }

    [TestMethod]
    public void NoDefaultChromeStringNamesAProduct()
    {
        List<string> offenders =
        [
            .. from s in DefaultStrings()
               from brand in ProductBrands
               where s.Value.Contains(brand, StringComparison.OrdinalIgnoreCase)
               select $"{s.Name} = \"{s.Value}\""
        ];

        Assert.IsTrue(
            offenders.Count == 0,
            "A shared library's fallback strings must not name a product. Any host that has not "
            + "wired WrapperStrings.Resolver renders these verbatim, so a branded default ships "
            + "the wrong product's name in a tooltip — visible only on hover:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// A host that <i>does</i> wire a resolver must still be able to override every key, or the
    /// neutral default is the only text some strings can ever have.
    /// </summary>
    [TestMethod]
    public void EveryKeyIsOverridable()
    {
        Func<string, string> original = WrapperStrings.Resolver;
        try
        {
            WrapperStrings.Resolver = key => $"HOST:{key}";

            foreach ((string name, string value) in DefaultStrings())
            {
                Assert.AreEqual($"HOST:{name}", value,
                    $"'{name}' ignored the host's resolver, so a host cannot localize it.");
            }
        }
        finally
        {
            WrapperStrings.Resolver = original;
        }
    }
}
