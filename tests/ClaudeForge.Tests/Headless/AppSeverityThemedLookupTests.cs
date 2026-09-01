using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// <c>AppSeverityToBrushConverter</c> must resolve the brush for the ACTIVE theme variant, not
/// fall back to its own hardcoded hex.
///
/// <para>
/// ⛔⛔ <b>Why this needs a headless app when the other severity tests do not.</b>
/// <c>AppSeverityTokenCoverageTests</c> proves the tokens are declared, and it does so by reading
/// the AXAML as text — no Avalonia required. But the declaration existing is only half the
/// contract: the converter has to ask for it with a theme variant. <c>BrushHelper</c> has two
/// lookups, and the flat one (<c>Resolve</c>, correct for the variant-less <c>LE.*</c> tokens)
/// returns NOTHING for a key declared inside <c>ThemeDictionaries</c> — the caller then silently
/// takes the fallback hex, which is a single literal for both themes and therefore precisely the
/// bug the whole token migration removed.
/// </para>
/// <para>
/// ⭐ <b>Without this test that swap is undetectable.</b> Every other test in the suite runs with
/// <c>Application.Current</c> either null or resource-less, so <c>Resolve</c> and
/// <c>ResolveThemed</c> both land on the fallback and both look correct. The sentinel colours
/// below are deliberately different from the converter's fallbacks, so taking the fallback path
/// fails the assertion instead of passing it.
/// </para>
/// <para>
/// ⚠ The resource dictionary is installed on the SHARED headless application and removed in a
/// <c>finally</c>. Leaving it behind would re-theme every later headless test in the assembly.
/// </para>
/// </summary>
[TestClass]
public sealed class AppSeverityThemedLookupTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    /// <summary>Sentinels chosen to match none of the converter's fallback hexes.</summary>
    private const string LightSentinel = "#010203";
    private const string DarkSentinel = "#0A0B0C";

    [TestMethod]
    public Task TheConverterResolvesThePerVariantToken_NotItsFallback() => Session.Dispatch(() =>
    {
        Application app = Application.Current
            ?? throw new InvalidOperationException("no Application in the headless session");

        ResourceDictionary themed = BuildThemedDictionary();
        ThemeVariant original = app.RequestedThemeVariant ?? ThemeVariant.Default;
        app.Resources.MergedDictionaries.Add(themed);

        try
        {
            AppSeverityToBrushConverter converter = new();

            foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                app.RequestedThemeVariant = variant;
                string expected = variant == ThemeVariant.Light ? LightSentinel : DarkSentinel;

                foreach (AppSeverity severity in Enum.GetValues<AppSeverity>())
                {
                    object result = converter.Convert(
                        severity, typeof(IBrush), null, CultureInfo.InvariantCulture);

                    Color actual = ((ISolidColorBrush)result).Color;

                    Assert.AreEqual(Color.Parse(expected), actual,
                        $"{severity} under {variant}: expected the declared token "
                        + $"{AppSeverityToBrushConverter.KeyFor(severity)} ({expected}) but got "
                        + $"{actual}. A themed key looked up without a variant resolves to nothing "
                        + "and the converter's fallback hex takes over — check that it calls "
                        + "BrushHelper.ResolveThemed and not the flat Resolve.");
                }
            }
        }
        finally
        {
            app.Resources.MergedDictionaries.Remove(themed);
            app.RequestedThemeVariant = original;
        }
    }, CancellationToken.None);

    /// <summary>
    /// Switching variant changes the answer — the brush is not captured once and reused.
    /// </summary>
    /// <remarks>
    /// The property this replaced was an <c>IBrush</c> frozen in the view-model's constructor, so
    /// a theme change never reached it. Resolving at Convert()-time is what fixes that, and this
    /// asserts the fix rather than the intent.
    /// </remarks>
    [TestMethod]
    public Task SwitchingThemeVariantChangesTheResolvedBrush() => Session.Dispatch(() =>
    {
        Application app = Application.Current!;
        ResourceDictionary themed = BuildThemedDictionary();
        ThemeVariant original = app.RequestedThemeVariant ?? ThemeVariant.Default;
        app.Resources.MergedDictionaries.Add(themed);

        try
        {
            AppSeverityToBrushConverter converter = new();

            app.RequestedThemeVariant = ThemeVariant.Light;
            Color light = ((ISolidColorBrush)converter.Convert(
                AppSeverity.Critical, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;

            app.RequestedThemeVariant = ThemeVariant.Dark;
            Color dark = ((ISolidColorBrush)converter.Convert(
                AppSeverity.Critical, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;

            Assert.AreNotEqual(light, dark,
                "the same severity resolved to the same colour in both variants, so the lookup is "
                + "either variant-blind or cached");
        }
        finally
        {
            app.Resources.MergedDictionaries.Remove(themed);
            app.RequestedThemeVariant = original;
        }
    }, CancellationToken.None);

    /// <summary>
    /// A dictionary shaped like the apps' real one: every severity key declared under both
    /// <c>Light</c> and <c>Dark</c> inside <c>ThemeDictionaries</c>.
    /// </summary>
    private static ResourceDictionary BuildThemedDictionary()
    {
        ResourceDictionary light = [];
        ResourceDictionary dark = [];

        foreach (AppSeverity severity in Enum.GetValues<AppSeverity>())
        {
            string key = AppSeverityToBrushConverter.KeyFor(severity);
            light[key] = new SolidColorBrush(Color.Parse(LightSentinel));
            dark[key] = new SolidColorBrush(Color.Parse(DarkSentinel));
        }

        ResourceDictionary root = [];
        root.ThemeDictionaries[ThemeVariant.Light] = light;
        root.ThemeDictionaries[ThemeVariant.Dark] = dark;
        return root;
    }
}
