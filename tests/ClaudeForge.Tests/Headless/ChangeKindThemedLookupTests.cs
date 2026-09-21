using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Bennewitz.Ninja.AgentForge.Sdk.Diagnostics;
using Bennewitz.Ninja.ClaudeForge.Converters;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// <c>ChangeKindToBrushConverter</c> must resolve the brush for the ACTIVE theme variant, not fall
/// back to its own hardcoded one.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>This converter's fallback is INDISTINGUISHABLE from success by inspection, which is
/// exactly why it needs a runtime test.</b> Its per-kind fallbacks deliberately mirror the
/// light-variant token values, so a broken lookup renders a pill of the correct colour on the
/// light theme and the wrong one on dark — and no screenshot, markup scan or coverage test can
/// tell the two apart. <c>AppChangeKindTokenCoverageTests</c> proves the tokens are DECLARED by
/// reading AXAML as text; this proves they are REACHED.
/// </para>
/// <para>
/// ⚠ The sentinels below are chosen to match none of the converter's fallbacks, so taking the
/// fallback path fails the assertion instead of quietly passing it. Same construction as
/// <see cref="AppSeverityThemedLookupTests"/> — see its remarks for why a themed key looked up
/// without a variant resolves to nothing.
/// </para>
/// <para>
/// ⚠ The dictionary is installed on the SHARED headless application and removed in a
/// <c>finally</c>; leaving it behind would re-theme every later headless test in the assembly.
/// </para>
/// </remarks>
[TestClass]
public sealed class ChangeKindThemedLookupTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    /// <summary>Sentinels chosen to match none of the converter's fallback colours.</summary>
    private const string LightSentinel = "#040506";
    private const string DarkSentinel = "#0D0E0F";

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
            ChangeKindToBrushConverter converter = new();

            foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                app.RequestedThemeVariant = variant;
                string expected = variant == ThemeVariant.Light ? LightSentinel : DarkSentinel;

                foreach (ChangeKind kind in Enum.GetValues<ChangeKind>())
                {
                    object result = converter.Convert(
                        kind, typeof(IBrush), null, CultureInfo.InvariantCulture);

                    Color actual = ((ISolidColorBrush)result).Color;

                    Assert.AreEqual(Color.Parse(expected), actual,
                        $"{kind} under {variant}: expected the declared token "
                        + $"{ChangeKindToBrushConverter.KeyFor(kind)} ({expected}) but got "
                        + $"{actual}. A themed key looked up without a variant resolves to nothing "
                        + "and the converter's fallback takes over — check that it passes "
                        + "app.ActualThemeVariant to TryGetResource.");
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
    /// The colour this replaced was a hex string on the view-model, frozen for both themes at once.
    /// Resolving at <c>Convert()</c>-time is what fixes that, and this asserts the fix rather than
    /// the intent.
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
            ChangeKindToBrushConverter converter = new();

            app.RequestedThemeVariant = ThemeVariant.Light;
            Color light = ((ISolidColorBrush)converter.Convert(
                ChangeKind.Modified, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;

            app.RequestedThemeVariant = ThemeVariant.Dark;
            Color dark = ((ISolidColorBrush)converter.Convert(
                ChangeKind.Modified, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;

            Assert.AreNotEqual(light, dark,
                "the same change kind resolved to the same colour in both variants, so the lookup "
                + "is either variant-blind or cached. ⚠ Note the REAL tokens are intentionally "
                + "identical across variants (see AppChangeKindTokenCoverageTests) — this test "
                + "installs differing sentinels precisely so the lookup's variant-sensitivity is "
                + "still observable.");
        }
        finally
        {
            app.Resources.MergedDictionaries.Remove(themed);
            app.RequestedThemeVariant = original;
        }
    }, CancellationToken.None);

    /// <summary>
    /// A dictionary shaped like the apps' real one: every change-kind key declared under both
    /// <c>Light</c> and <c>Dark</c> inside <c>ThemeDictionaries</c>.
    /// </summary>
    private static ResourceDictionary BuildThemedDictionary()
    {
        ResourceDictionary light = [];
        ResourceDictionary dark = [];

        foreach (ChangeKind kind in Enum.GetValues<ChangeKind>())
        {
            string key = ChangeKindToBrushConverter.KeyFor(kind);
            light[key] = new SolidColorBrush(Color.Parse(LightSentinel));
            dark[key] = new SolidColorBrush(Color.Parse(DarkSentinel));
        }

        ResourceDictionary root = [];
        root.ThemeDictionaries[ThemeVariant.Light] = light;
        root.ThemeDictionaries[ThemeVariant.Dark] = dark;
        return root;
    }
}
