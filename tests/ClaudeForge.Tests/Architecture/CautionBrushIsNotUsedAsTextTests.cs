using System.Globalization;
using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// The caution palette is split by ROLE, and the roles have different contrast floors.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>One token cannot clear both floors, and pretending it can was finding F4.</b>
/// <c>AppCautionBrush</c> was chosen as a border and glyph accent, where the floor is 3.0:1. It
/// was then used as a <c>Foreground</c> at FIVE sites, where the floor is 4.5:1, and every one of
/// them measured 3.07–3.19:1 in the light theme. Nothing failed, because contrast is not something
/// a compiler or a rendering test can notice.
/// </para>
/// <para>
/// ⭐ <b>The ratios below are COMPUTED from the hexes in <c>App.axaml</c>, not quoted.</b> A test
/// asserting "the token equals #9A3412" would pass forever while saying nothing about whether
/// #9A3412 is readable, and would have to be edited — by hand, to a number nobody re-derives —
/// every time the palette moved. Recomputing means a future colour change is checked rather than
/// merely recorded.
/// </para>
/// <para>
/// ⚠ <b>The repo already had the right pattern before it had this test.</b>
/// <c>EssentialsView</c> draws its caution panel as tint background + caution BORDER + body text
/// in <c>AppPrimaryTextBrush</c>; <c>AboutEditorView</c> and <c>MemoryEditorView</c> built the
/// same panel and coloured the header with the border token. The fix was to make the others match
/// the one that was already correct.
/// </para>
/// </remarks>
[TestClass]
public sealed class CautionBrushIsNotUsedAsTextTests
{
    private const double TextFloor = 4.5;
    private const double NonTextFloor = 3.0;

    /// <summary>How many border uses must exist for the foreground scan to mean anything.</summary>
    private const int MinimumAccentUses = 6;

    /// <summary>The surfaces a caution colour is actually drawn on, per variant.</summary>
    private static readonly string[] SurfaceKeys =
        ["AppCardBackgroundBrush", "AppCautionBackgroundBrush"];

    private static readonly string[] Variants = ["Light", "Dark"];

    /// <summary>⛔ The rule the palette exists to express: which key may carry which role.</summary>
    private static readonly (string Key, double Floor, string Role)[] Roles =
    [
        ("AppCautionTextBrush", TextFloor, "caution-coloured body text"),
        ("AppCautionBrush", NonTextFloor, "the caution border and glyph accent"),
        ("AppSeverityCautionBrush", NonTextFloor, "the Caution severity glyph and banner border"),
    ];

    [TestMethod]
    public void TheCautionAccentIsNeverUsedAsAForeground()
    {
        string repoRoot = FindRepoRoot();
        List<string> offenders = [];
        int accentUses = 0;

        foreach ((string relative, string text) in AxamlFiles(repoRoot))
        {
            accentUses += Regex.Matches(text, @"\{DynamicResource\s+AppCautionBrush\}").Count;

            foreach (Match m in Regex.Matches(
                         text, @"Foreground\s*=\s*""\{DynamicResource\s+AppCautionBrush\}"""))
            {
                offenders.Add($"{relative} (offset {m.Index})");
            }
        }

        Assert.IsTrue(accentUses >= MinimumAccentUses,
            $"only {accentUses} use(s) of AppCautionBrush found, expected at least "
            + $"{MinimumAccentUses}. The token has been renamed or removed, so this scan is no "
            + "longer checking anything and would pass over a reintroduced text use.");

        Assert.IsTrue(offenders.Count == 0,
            "AppCautionBrush is an ACCENT, chosen to clear the 3.0:1 non-text floor. Used as text "
            + "it measures about 3.1–3.2:1 against a 4.5:1 floor — the F4 defect, reintroduced:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nUse AppCautionTextBrush for caution-coloured text, or AppPrimaryTextBrush for "
            + "body text inside a bordered caution panel (see EssentialsView).");
    }

    [TestMethod]
    public void EveryCautionRoleClearsItsContrastFloorOnEverySurface()
    {
        string appAxaml = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "src", "ClaudeForge", "App.axaml"));

        List<string> failures = [];
        int checkedPairs = 0;

        foreach (string variant in Variants)
        {
            string block = VariantBlock(appAxaml, variant);

            foreach ((string key, double floor, string role) in Roles)
            {
                string fg = HexFor(block, key, variant);

                foreach (string surfaceKey in SurfaceKeys)
                {
                    string bg = HexFor(block, surfaceKey, variant);
                    double ratio = Contrast(fg, bg);
                    checkedPairs++;

                    if (ratio < floor)
                    {
                        failures.Add(
                            $"{variant}: {key} {fg} on {surfaceKey} {bg} = {ratio:F2}:1, "
                            + $"under the {floor:F1}:1 floor for {role}");
                    }
                }
            }
        }

        // 2 variants x 3 roles x 2 surfaces. A miscount means a block or key stopped being found,
        // and every ratio after that would be computed against the wrong thing.
        Assert.AreEqual(Variants.Length * Roles.Length * SurfaceKeys.Length, checkedPairs,
            "the variant/token scan lost some of its subjects");

        Assert.IsTrue(failures.Count == 0,
            $"{failures.Count} caution colour(s) are unreadable on a surface they are drawn on:\n  "
            + string.Join("\n  ", failures));
    }

    /// <summary>
    /// ⚠ A themed key looked up with a null variant resolves to NOTHING, so a token present in one
    /// variant only is a total miss for whichever theme is active — not a half-fix.
    /// </summary>
    [TestMethod]
    public void TheCautionTextTokenIsDeclaredInBothVariants()
    {
        string appAxaml = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "src", "ClaudeForge", "App.axaml"));

        foreach (string variant in Variants)
        {
            Assert.IsTrue(
                Regex.IsMatch(
                    VariantBlock(appAxaml, variant),
                    @"x:Key\s*=\s*""AppCautionTextBrush"""),
                $"AppCautionTextBrush is not declared in the {variant} variant, so every site "
                + "using it renders with no brush in that theme");
        }
    }

    /// <summary>The body of one <c>ThemeDictionaries</c> entry, up to the next variant's key.</summary>
    private static string VariantBlock(string axaml, string variant)
    {
        Match start = Regex.Match(axaml, $@"<ResourceDictionary\s+x:Key\s*=\s*""{variant}""");
        Assert.IsTrue(start.Success, $"no {variant} ThemeDictionaries entry in App.axaml");

        Match next = Regex.Match(
            axaml[(start.Index + start.Length)..],
            @"<ResourceDictionary\s+x:Key\s*=\s*""(?:Light|Dark)""");

        return next.Success
            ? axaml.Substring(start.Index, start.Length + next.Index)
            : axaml[start.Index..];
    }

    private static string HexFor(string block, string key, string variant)
    {
        Match m = Regex.Match(
            block, $@"x:Key\s*=\s*""{Regex.Escape(key)}""\s+Color\s*=\s*""(?<hex>#[0-9A-Fa-f]{{6,8}})""");

        Assert.IsTrue(m.Success, $"{key} is not declared in the {variant} variant of App.axaml");
        return m.Groups["hex"].Value;
    }

    private static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>WCAG 2.x relative luminance.</summary>
    private static double Luminance(string hex)
    {
        string h = hex.TrimStart('#');

        // An 8-digit value is #AARRGGBB in Avalonia; the alpha pair is not part of the colour.
        if (h.Length == 8)
        {
            h = h[2..];
        }

        static double Channel(double c) =>
            c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

        double Part(int at) =>
            Channel(int.Parse(h.Substring(at, 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture) / 255.0);

        return (0.2126 * Part(0)) + (0.7152 * Part(2)) + (0.0722 * Part(4));
    }

    private static IEnumerable<(string Relative, string Text)> AxamlFiles(string repoRoot)
    {
        foreach (string path in Directory.EnumerateFiles(
                     Path.Combine(repoRoot, "src"), "*.axaml", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (
                Path.GetRelativePath(repoRoot, path).Replace('\\', '/'),
                File.ReadAllText(path));
        }
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClaudeForge.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.IsNotNull(dir, "could not locate the repository root (ClaudeForge.slnx)");
        return dir.FullName;
    }
}
