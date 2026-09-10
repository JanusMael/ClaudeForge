using System.Globalization;
using System.Xml.Linq;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Theming;

/// <summary>
/// Holds the status pills to the contrast contract <c>App.axaml</c> states in prose
/// beside them: <b>every pair is &gt;= 4.5:1 on the pill and &gt;= 7.3:1 on the page</b>,
/// with the instruction "recheck both numbers if you retint either half".
///
/// <para>
/// That recheck was a manual step, and the margin is thin enough to lose by accident —
/// the worst pill pair clears its floor by 0.07 and the worst page pair by 0.05. This
/// test is the recheck, run on every build.
/// </para>
///
/// <para>
/// The brush values are <b>read out of `App.axaml`</b> rather than copied here, so a
/// retint is measured rather than assumed. The only values written down are Semi's own
/// window grounds, which live in Semi.Avalonia rather than this repository — see
/// <see cref="PageBackgrounds"/>.
/// </para>
/// </summary>
[TestClass]
public sealed class StatusPaletteContrastTests
{
    /// <summary>WCAG AA for text: the floor a pill's foreground owes its own fill.</summary>
    private const double PillFloor = 4.5;

    /// <summary>
    /// The floor a pill's foreground owes the page. Higher than AA because these same
    /// four brushes are reused as plain text on the window ground by
    /// PermissionTesterView, GuidedRuleBuilderView and AgentsSkillsEditorView, where
    /// there is no pill behind them — and 7.3 is what `App.axaml` committed to.
    /// </summary>
    private const double PageFloor = 7.3;

    private static readonly string[] Kinds = ["Success", "Warning", "Failure", "Active"];

    /// <summary>
    /// Semi.Avalonia's <c>SemiColorBackground0</c> — the colour it paints the window —
    /// per theme variant. Written down rather than resolved because it belongs to
    /// Semi.Avalonia, not to this repository, and resolving it would mean standing up a
    /// headless Avalonia session to read one colour per variant.
    /// </summary>
    /// <remarks>
    /// The one thing that invalidates these is Semi retinting its window ground, which a
    /// package bump would carry. If this test starts failing right after a Semi upgrade,
    /// check here first — the palette may be innocent.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> PageBackgrounds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Light"] = "#FFFFFF",
            ["Dark"] = "#16161A",
        };

    [TestMethod]
    public void EveryStatusPill_ClearsItsFillAndThePage()
    {
        List<string> failures = [];

        foreach ((string variant, string page) in PageBackgrounds.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            IReadOnlyDictionary<string, string> brushes = ReadStatusBrushes(variant);

            // A parse that silently found nothing would pass every assertion below by
            // having none to make. Eight brushes per variant, or the structure moved.
            Assert.AreEqual(8, brushes.Count,
                $"Expected 8 AppStatus* brushes in App.axaml's '{variant}' theme dictionary, found " +
                $"{brushes.Count} ({string.Join(", ", brushes.Keys.OrderBy(k => k, StringComparer.Ordinal))}). " +
                "The dictionary was renamed or restructured — fix this test before trusting it.");

            foreach (string kind in Kinds)
            {
                string foreground = brushes[$"AppStatus{kind}ForegroundBrush"];
                string fill = brushes[$"AppStatus{kind}BackgroundBrush"];

                double onFill = ContrastRatio(foreground, fill);
                if (onFill < PillFloor)
                {
                    failures.Add(
                        $"  • {variant} {kind}: {foreground} on its pill {fill} is {onFill:F2}:1, " +
                        $"below the {PillFloor}:1 the pill owes (WCAG AA for text).");
                }

                double onPage = ContrastRatio(foreground, page);
                if (onPage < PageFloor)
                {
                    failures.Add(
                        $"  • {variant} {kind}: {foreground} on the page {page} is {onPage:F2}:1, " +
                        $"below the {PageFloor}:1 App.axaml commits to. These brushes are reused as " +
                        "plain text on the window ground, with no pill behind them.");
                }
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                "Status palette contrast regression — App.axaml states \"every pair here is >= 4.5:1 " +
                "on the pill and >= 7.3:1 on the page\":\n\n" +
                string.Join('\n', failures) +
                "\n\nRetinting one half of a pair moves both numbers. Adjust the foreground and the " +
                "fill together, and update the note above the palette in App.axaml if the floors change.\n");
        }
    }

    [TestMethod]
    public void TheMarginIsReported_SoATightPairIsVisibleBeforeItBreaks()
    {
        // Diagnostic only. The palette clears its floors by 0.07 and 0.05 at the worst
        // pair, so "it passes" and "it is about to stop passing" look identical from a
        // green test run. This prints the whole table.
        foreach ((string variant, string page) in PageBackgrounds.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            IReadOnlyDictionary<string, string> brushes = ReadStatusBrushes(variant);
            Console.WriteLine($"[StatusPaletteContrast] {variant} (page {page})");
            foreach (string kind in Kinds)
            {
                string foreground = brushes[$"AppStatus{kind}ForegroundBrush"];
                string fill = brushes[$"AppStatus{kind}BackgroundBrush"];
                Console.WriteLine(
                    $"[StatusPaletteContrast]   {kind,-8} {foreground} on pill {fill} " +
                    $"{ContrastRatio(foreground, fill),5:F2}:1 (floor {PillFloor})   " +
                    $"on page {ContrastRatio(foreground, page),5:F2}:1 (floor {PageFloor})");
            }
        }

        Assert.IsTrue(PageBackgrounds.Count == 2);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The <c>AppStatus*</c> brushes declared in <c>App.axaml</c>'s theme dictionary for
    /// <paramref name="variant"/>, keyed by resource key and valued as hex colours.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadStatusBrushes(string variant)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindAppAxaml());

        XElement themeDictionaries = document.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "ResourceDictionary.ThemeDictionaries")
            ?? throw new InvalidOperationException(
                "App.axaml has no ResourceDictionary.ThemeDictionaries element.");

        XElement dictionary = themeDictionaries.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "ResourceDictionary"
                                 && (string?)e.Attribute(x + "Key") == variant)
            ?? throw new InvalidOperationException(
                $"App.axaml has no '{variant}' theme dictionary.");

        return dictionary.Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .Select(e => (Key: (string?)e.Attribute(x + "Key"), Colour: (string?)e.Attribute("Color")))
            .Where(b => b.Key is not null
                        && b.Colour is not null
                        && b.Key.StartsWith("AppStatus", StringComparison.Ordinal))
            .ToDictionary(b => b.Key!, b => b.Colour!, StringComparer.Ordinal);
    }

    /// <summary>WCAG 2.x relative luminance.</summary>
    private static double RelativeLuminance(string hex)
    {
        string value = hex.TrimStart('#');
        if (value.Length == 8)
        {
            value = value[2..]; // AARRGGBB — the alpha plays no part in the ratio.
        }

        if (value.Length != 6)
        {
            throw new InvalidOperationException($"Not a 6- or 8-digit hex colour: '{hex}'.");
        }

        double Channel(int offset)
        {
            double c = int.Parse(value.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(0)) + (0.7152 * Channel(2)) + (0.0722 * Channel(4));
    }

    private static double ContrastRatio(string foreground, string background)
    {
        double a = RelativeLuminance(foreground);
        double b = RelativeLuminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>
    /// Walks up from the test's runtime base directory to the repo root — the directory
    /// holding <c>ClaudeForge.slnx</c> — and down to <c>src/ClaudeForge/App.axaml</c>.
    /// </summary>
    private static string FindAppAxaml()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "ClaudeForge.slnx")))
            {
                return Path.Combine(dir, "src", "ClaudeForge", "App.axaml");
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not locate the repo root (the directory holding ClaudeForge.slnx) by walking up " +
            $"from AppContext.BaseDirectory = '{AppContext.BaseDirectory}'.");
    }
}
