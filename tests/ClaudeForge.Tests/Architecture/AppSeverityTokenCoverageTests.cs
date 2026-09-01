using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Every <see cref="AppSeverity"/> member must have an <c>AppSeverity*Brush</c> declared in BOTH
/// theme variants of BOTH apps.
///
/// <para>
/// ⛔⛔ <b>The failure this guards against is silent twice over.</b> A missing
/// <c>DynamicResource</c> is not a build error and logs nothing — established already by
/// <c>LE.DangerText</c>, which was referenced nine times and declared zero. On top of that,
/// <c>AppSeverityToBrushConverter</c> carries a fallback hex per member so a template never
/// renders a null brush. Together those mean a missing token produces a <i>plausible colour</i>
/// and no diagnostic anywhere: the app looks fine, and the single hardcoded literal that the
/// whole token migration existed to delete is quietly back in force.
/// </para>
///
/// <para>
/// ⚠ <b>Per variant, because a themed key looked up with a null variant resolves to NOTHING.</b>
/// The <c>LE.*</c> tokens are flat and one value serves both themes; every <c>App*Brush</c> lives
/// inside <c>ResourceDictionary.ThemeDictionaries</c>. Declaring a severity token in only one
/// variant is therefore not a half-fix — it is a total miss for whichever theme is active, and
/// the app that gets it wrong is whichever one the developer was not looking at.
/// </para>
///
/// <para>
/// ⚠ <b>Both apps, because these tokens are declared per app and consumed from a shared library.</b>
/// That combination has already shipped a defect once: <c>AppPropertyHeading*Brush</c> was declared
/// in <c>ClaudeForge/App.axaml</c> and referenced by <c>LayeredEditors.Avalonia</c>'s shared
/// <c>PropertyEditorWrapper</c>, so every property heading in OpenCodeForge rendered unstyled until
/// the token was added there too. OpenCodeForge has no Essentials page yet — Phase 12 — so nothing
/// consumes these there today; the tokens and this test are in place so that page cannot arrive
/// unstyled.
/// </para>
/// </summary>
[TestClass]
public sealed class AppSeverityTokenCoverageTests
{
    /// <summary>The <c>App.axaml</c> of each app that must declare the full set.</summary>
    private static readonly string[] AppFiles =
    [
        "src/ClaudeForge/App.axaml",
        "src/OpenCodeForge/App.axaml",
    ];

    private static readonly string[] Variants = ["Light", "Dark"];

    [TestMethod]
    public void EverySeverityHasABrushInBothVariantsOfBothApps()
    {
        string repoRoot = FindRepoRoot();
        AppSeverity[] severities = Enum.GetValues<AppSeverity>();

        Assert.IsTrue(severities.Length >= 4,
            $"expected at least 4 severities, found {severities.Length} — the scan below would "
            + "under-assert if the enum were emptied");

        List<string> missing = [];

        foreach (string relative in AppFiles)
        {
            string path = Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(path), $"missing app file: {relative}");

            string text = File.ReadAllText(path);

            foreach (string variant in Variants)
            {
                string block = ExtractVariantBlock(text, variant, relative);

                foreach (AppSeverity severity in severities)
                {
                    // Ask the converter for the key so this asserts against the string the running
                    // app actually looks up, not against a second copy of the naming convention
                    // that could drift from it.
                    string key = AppSeverityToBrushConverter.KeyFor(severity);
                    if (!Regex.IsMatch(block, $@"x:Key\s*=\s*""{Regex.Escape(key)}""\s"))
                    {
                        missing.Add($"  {relative} [{variant}] is missing {key}");
                    }
                }
            }
        }

        if (missing.Count > 0)
        {
            Assert.Fail(
                $"{missing.Count} AppSeverity brush declaration(s) missing:\n"
                + string.Join('\n', missing)
                + "\n\nA missing themed DynamicResource is not a build error and logs nothing; the "
                + "converter's per-member fallback hex takes over, so the app renders a plausible "
                + "colour and the hardcoded literal is silently back. Declare the key inside the "
                + "matching <ResourceDictionary x:Key=\"Light\"> / \"Dark\" block.");
        }
    }

    /// <summary>
    /// The declared colours differ between Light and Dark for every non-neutral severity.
    /// </summary>
    /// <remarks>
    /// ⛔ The whole reason severity moved off a hex string is that ONE literal was serving both
    /// themes — the light-theme red shipped into dark mode. Declaring the same value in both
    /// variant blocks satisfies the coverage test above while reproducing exactly that bug, so it
    /// is asserted against separately.
    /// <para>
    /// <see cref="AppSeverity.Neutral"/> is included: its light/dark pair comes from
    /// <c>AppSecondaryTextBrush</c>, which genuinely differs per theme.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EverySeveritysLightAndDarkColoursDiffer()
    {
        string repoRoot = FindRepoRoot();
        List<string> same = [];

        foreach (string relative in AppFiles)
        {
            string text = File.ReadAllText(
                Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

            string light = ExtractVariantBlock(text, "Light", relative);
            string dark = ExtractVariantBlock(text, "Dark", relative);

            foreach (AppSeverity severity in Enum.GetValues<AppSeverity>())
            {
                string key = AppSeverityToBrushConverter.KeyFor(severity);
                string? l = ColourFor(light, key);
                string? d = ColourFor(dark, key);

                if (l is not null && d is not null
                    && string.Equals(l, d, StringComparison.OrdinalIgnoreCase))
                {
                    same.Add($"  {relative}: {key} is {l} in both variants");
                }
            }
        }

        Assert.AreEqual(0, same.Count,
            "a severity colour that is identical in both theme variants is the single-literal "
            + "problem this token replaced:\n" + string.Join('\n', same));
    }

    /// <summary>
    /// The converter maps every member — including one that is not a declared member at all.
    /// </summary>
    /// <remarks>
    /// A converter that throws inside an <c>ItemsControl</c> template takes the whole page down,
    /// so an unrecognised value must degrade to Neutral rather than raise. Cast an undeclared int
    /// to prove that rather than trusting the <c>switch</c>'s default arm by reading it.
    /// </remarks>
    [TestMethod]
    public void TheConverterReturnsABrushForEveryMemberAndForGarbage()
    {
        AppSeverityToBrushConverter converter = new();

        foreach (AppSeverity severity in Enum.GetValues<AppSeverity>())
        {
            object result = converter.Convert(
                severity, typeof(IBrush), null, CultureInfo.InvariantCulture);
            Assert.IsInstanceOfType<IBrush>(result, $"{severity} produced {result?.GetType().Name}");
        }

        object garbage = converter.Convert(
            (AppSeverity)9999, typeof(IBrush), null, CultureInfo.InvariantCulture);
        Assert.IsInstanceOfType<IBrush>(garbage, "an undeclared severity must not throw");

        object wrongType = converter.Convert(
            "#FF0000", typeof(IBrush), null, CultureInfo.InvariantCulture);
        Assert.IsInstanceOfType<IBrush>(wrongType, "a non-severity value must not throw");
    }

    /// <summary>
    /// The <c>Light</c> / <c>Dark</c> <c>ResourceDictionary</c> body inside an app's
    /// <c>ThemeDictionaries</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ Fails loudly rather than returning empty when the block is absent. An empty string would
    /// make every key "missing" — a confusing cascade — or, worse, if the helper were ever changed
    /// to return the whole file on miss, would make every key "present" in both variants and the
    /// guard vacuous.
    /// </remarks>
    private static string ExtractVariantBlock(string text, string variant, string relative)
    {
        Match m = Regex.Match(
            text,
            $@"<ResourceDictionary\s+x:Key\s*=\s*""{variant}""\s*>(?<body>.*?)</ResourceDictionary>",
            RegexOptions.Singleline);

        Assert.IsTrue(m.Success,
            $"{relative} has no <ResourceDictionary x:Key=\"{variant}\"> block — the App*Brush "
            + "tokens must live inside ResourceDictionary.ThemeDictionaries");

        return m.Groups["body"].Value;
    }

    private static string? ColourFor(string block, string key)
    {
        Match m = Regex.Match(
            block, $@"x:Key\s*=\s*""{Regex.Escape(key)}""\s+Color\s*=\s*""(?<c>#[0-9A-Fa-f]+)""");
        return m.Success ? m.Groups["c"].Value : null;
    }

    /// <summary>Matches <c>BuildFilePathIntegrityTests.FindRepoRoot()</c>.</summary>
    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
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
}
