using System.Globalization;
using System.Text.RegularExpressions;
using Bennewitz.Ninja.AgentForge.Sdk.Diagnostics;
using Bennewitz.Ninja.ClaudeForge.Converters;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Every <see cref="ChangeKind"/> must have an <c>AppChangeKind*Brush</c> declared in BOTH theme
/// variants of BOTH apps, and every declared fill must keep its white glyph legible.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>The contrast half of this test is the half that matters, because the defect it locks
/// down actually shipped.</b> The Modified pill was <c>#F57C00</c>, giving its white <c>~</c>
/// glyph <b>2.70:1</b> — under the 4.5:1 text floor and under even the 3.0:1 non-text one. It was
/// chosen deliberately over <c>#E65100</c> by a source comment that reasoned entirely about hue
/// ("visually reads as red — easy to confuse with the removed pill") and measured nothing. A
/// colour living in a view-model is a colour nobody re-measures; now it lives in the theme and
/// this test measures it on every run.
/// </para>
/// <para>
/// ⚠ <b>Unlike <see cref="AppSeverityTokenCoverageTests"/>, this family does NOT assert that the
/// light and dark values differ — they are identical on purpose.</b> A severity token is a
/// FOREGROUND on a themed surface, so one literal serving both themes is precisely the bug that
/// moved it into the theme. A change-kind token is a FILL behind a white glyph: the pair that must
/// hold is glyph-vs-fill, and lightening the fill for dark mode would trade that away for
/// fill-vs-surface, which is redundant with the <c>+</c>/<c>-</c>/<c>~</c> glyph and its
/// accessible name. Declared per variant regardless, because a themed lookup finds nothing in a
/// flat dictionary.
/// </para>
/// </remarks>
[TestClass]
public sealed class AppChangeKindTokenCoverageTests
{
    private static readonly string[] AppFiles =
    [
        "src/ClaudeForge/App.axaml",
        "src/OpenCodeForge/App.axaml",
    ];

    private static readonly string[] Variants = ["Light", "Dark"];

    /// <summary>WCAG AA for normal text. The glyph is small and bold, so this is the right floor.</summary>
    private const double MinGlyphContrast = 4.5;

    [TestMethod]
    public void EveryChangeKindHasABrushInBothVariantsOfBothApps()
    {
        ChangeKind[] kinds = Enum.GetValues<ChangeKind>();
        Assert.IsTrue(kinds.Length >= 3,
            $"expected at least 3 change kinds, found {kinds.Length} — the scan below would "
            + "under-assert if the enum were emptied");

        List<string> missing = [];

        foreach (string relative in AppFiles)
        {
            string text = ReadApp(relative);

            foreach (string variant in Variants)
            {
                string block = ExtractVariantBlock(text, variant, relative);

                foreach (ChangeKind kind in kinds)
                {
                    // Ask the converter for the key, so this asserts against the string the running
                    // app looks up rather than a second copy of the naming convention.
                    string key = ChangeKindToBrushConverter.KeyFor(kind);
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
                $"{missing.Count} AppChangeKind brush declaration(s) missing:\n"
                + string.Join('\n', missing)
                + "\n\nA missing themed DynamicResource is not a build error and logs nothing; the "
                + "converter's per-kind fallback takes over, so the pill renders a plausible colour "
                + "and the hardcoded literal is silently back.");
        }
    }

    /// <summary>
    /// Every declared pill fill keeps a white glyph at or above <see cref="MinGlyphContrast"/>.
    /// </summary>
    [TestMethod]
    public void EveryDeclaredPillKeepsItsWhiteGlyphLegible()
    {
        List<string> failures = [];
        int measured = 0;

        foreach (string relative in AppFiles)
        {
            string text = ReadApp(relative);

            foreach (string variant in Variants)
            {
                string block = ExtractVariantBlock(text, variant, relative);

                foreach (ChangeKind kind in Enum.GetValues<ChangeKind>())
                {
                    string key = ChangeKindToBrushConverter.KeyFor(kind);
                    Match m = Regex.Match(
                        block,
                        $@"x:Key\s*=\s*""{Regex.Escape(key)}""\s+Color\s*=\s*""(#[0-9A-Fa-f]{{6,8}})""");
                    if (!m.Success)
                    {
                        continue; // absence is the other test's failure, not this one's
                    }

                    measured++;
                    double ratio = Contrast(m.Groups[1].Value, "#FFFFFF");
                    if (ratio < MinGlyphContrast)
                    {
                        failures.Add(
                            $"  {relative} [{variant}] {key} = {m.Groups[1].Value} gives its white "
                            + $"glyph {ratio:F2}:1, under {MinGlyphContrast}:1");
                    }
                }
            }
        }

        Assert.IsTrue(measured >= 12,
            $"only {measured} pill colour(s) were measured; 3 kinds x 2 variants x 2 apps = 12 are "
            + "expected, so the scan has lost its subjects and would pass without checking "
            + "anything.");

        if (failures.Count > 0)
        {
            Assert.Fail(
                $"{failures.Count} pill fill(s) fail the white-glyph contrast floor:\n"
                + string.Join('\n', failures)
                + "\n\nThe glyph is what a user reads; the fill is redundant with it. Darken the "
                + "fill rather than lightening it — #B45309 replaced #F57C00 (2.70:1) while keeping "
                + "a clearly non-red hue.");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string ReadApp(string relative)
    {
        string path = Path.Combine(FindRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.IsTrue(File.Exists(path), $"missing app file: {relative}");
        return File.ReadAllText(path);
    }

    /// <summary>Relative luminance, per WCAG 2.x.</summary>
    private static double Luminance(string hex)
    {
        string h = hex.TrimStart('#');
        if (h.Length == 8)
        {
            h = h[2..]; // #AARRGGBB — drop alpha
        }

        double Channel(int offset)
        {
            double c = int.Parse(h.Substring(offset, 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(0)) + (0.7152 * Channel(2)) + (0.0722 * Channel(4));
    }

    private static double Contrast(string a, string b)
    {
        double la = Luminance(a);
        double lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>The body of one <c>ResourceDictionary x:Key="Light|Dark"</c> block.</summary>
    private static string ExtractVariantBlock(string text, string variant, string relative)
    {
        int start = text.IndexOf($"x:Key=\"{variant}\"", StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"{relative} has no <ResourceDictionary x:Key=\"{variant}\"> block");

        // Up to the next variant block, or end of file — enough to scope the key search.
        string other = variant == "Light" ? "Dark" : "Light";
        int end = text.IndexOf($"x:Key=\"{other}\"", start, StringComparison.Ordinal);
        return end > start ? text[start..end] : text[start..];
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClaudeForge.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.IsNotNull(dir, "could not locate the repo root (no ClaudeForge.slnx above the test binary)");
        return dir!.FullName;
    }
}
