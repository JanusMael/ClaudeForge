using System.Globalization;
using System.Text.RegularExpressions;
using Bennewitz.Ninja.ScopedEditors.Abstractions;
using Bennewitz.Ninja.ScopedEditors.AvaloniaUI.Converters;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// The danger banner's tint is derived from the severity colour, and the alpha that derives it is
/// bounded by contrast rather than by taste.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>The banner draws its BORDER in the same colour the tint is made from</b>, so raising
/// alpha pulls the two toward each other. That is the constraint nobody would guess from looking
/// at the markup: a tint chosen to be "a bit more visible" silently erases the border it sits
/// inside. At 15% Caution's border measures 2.96:1 against its own tint — under the 3.0:1
/// non-text floor — while the tint still looks perfectly reasonable.
/// </para>
/// <para>
/// ⭐ <b>Everything here is COMPUTED from the hexes in <c>App.axaml</c> and the alpha in the
/// converter.</b> Quoting the numbers would make this a record of one measurement rather than a
/// guard on the next change, and the next change is the one that breaks it.
/// </para>
/// <para>
/// ⚠ <b>The body-text figure uses ClaudeForge's <c>AppPrimaryTextBrush</c> as a stand-in.</b> The
/// banner's explanation deliberately sets no <c>Foreground</c> and inherits, because
/// OpenCodeForge declares no such token; what it inherits is the theme's ordinary text colour,
/// which is what this approximates. The margin is large enough — 14:1 and up — that the
/// approximation cannot flip the verdict.
/// </para>
/// </remarks>
public sealed class SeverityTintStaysLegibleTests
{
    private const double TextFloor = 4.5;
    private const double NonTextFloor = 3.0;

    private static readonly (string Variant, string SurfaceKey, string TextKey)[] Cases =
    [
        ("Light", "AppCardBackgroundBrush", "AppPrimaryTextBrush"),
        ("Dark", "AppCardBackgroundBrush", "AppPrimaryTextBrush"),
    ];

    /// <summary>
    /// The premise: the tint is actually a wash, not an opaque fill or nothing at all. A converter
    /// returning alpha 0 would satisfy every contrast assertion below by making the tint invisible.
    /// </summary>
    [Fact]
    public void ThePremiseHolds_TheTintIsTranslucentAndVisible()
    {
        Assert.True(
            AppSeverityToTintBrushConverter.TintAlpha is > 0.02 and < 0.5,
            $"TintAlpha is {AppSeverityToTintBrushConverter.TintAlpha}. At zero the banner has no "
            + "background and every contrast test below passes vacuously; near one it is an opaque "
            + "fill and the body text is unreadable.");

        foreach ((string variant, string surfaceKey, _) in Cases)
        {
            string surface = HexFor(variant, surfaceKey);

            foreach (AppSeverity severity in Enum.GetValues<AppSeverity>())
            {
                string tint = Composite(SeverityHex(variant, severity), surface);

                MessageAssert.NotEqual(surface, tint, StringComparer.OrdinalIgnoreCase,
                    $"{variant}/{severity}: the tint composites to the surface colour exactly, so "
                    + "the banner has no visible background");
            }
        }
    }

    /// <summary>⛔ The ceiling. This is the assertion the alpha is actually chosen against.</summary>
    [Fact]
    public void TheBorderStaysClearOfItsFloorAgainstItsOwnTint()
    {
        List<string> failures = [];

        foreach ((string variant, string surfaceKey, _) in Cases)
        {
            string surface = HexFor(variant, surfaceKey);

            foreach (AppSeverity severity in Enum.GetValues<AppSeverity>())
            {
                string border = SeverityHex(variant, severity);
                double ratio = Contrast(border, Composite(border, surface));

                if (ratio < NonTextFloor)
                {
                    failures.Add(
                        $"{variant}: {severity} border {border} against its own "
                        + $"{AppSeverityToTintBrushConverter.TintAlpha:P0} tint = {ratio:F2}:1, "
                        + $"under {NonTextFloor:F1}:1");
                }
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} banner border(s) disappear into their own tint:\n  "
            + string.Join("\n  ", failures)
            + "\n\nLower AppSeverityToTintBrushConverter.TintAlpha. Raising it is what causes "
            + "this — the border and the tint are the same colour.");
    }

    [Fact]
    public void BodyTextOnTheTintClearsTheTextFloor()
    {
        List<string> failures = [];

        foreach ((string variant, string surfaceKey, string textKey) in Cases)
        {
            string surface = HexFor(variant, surfaceKey);
            string body = HexFor(variant, textKey);

            foreach (AppSeverity severity in Enum.GetValues<AppSeverity>())
            {
                string tint = Composite(SeverityHex(variant, severity), surface);
                double ratio = Contrast(body, tint);

                if (ratio < TextFloor)
                {
                    failures.Add(
                        $"{variant}: body text {body} on the {severity} tint {tint} = "
                        + $"{ratio:F2}:1, under {TextFloor:F1}:1");
                }
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} banner(s) render unreadable body text:\n  "
            + string.Join("\n  ", failures));
    }

    /// <summary>
    /// ⭐ Derives the ceiling instead of asserting a remembered number, and proves the guard above
    /// discriminates: if every alpha passed, that test would be vacuous.
    /// </summary>
    [Fact]
    public void TheChosenAlphaSitsBelowTheMeasuredCeiling()
    {
        double ceiling = 1.0;

        for (double alpha = 0.01; alpha <= 1.0; alpha += 0.01)
        {
            bool ok = true;

            foreach ((string variant, string surfaceKey, _) in Cases)
            {
                string surface = HexFor(variant, surfaceKey);

                foreach (AppSeverity severity in Enum.GetValues<AppSeverity>())
                {
                    string border = SeverityHex(variant, severity);
                    if (Contrast(border, Composite(border, surface, alpha)) < NonTextFloor)
                    {
                        ok = false;
                        break;
                    }
                }

                if (!ok)
                {
                    break;
                }
            }

            if (!ok)
            {
                ceiling = alpha;
                break;
            }
        }

        Assert.True(ceiling < 1.0,
            "no alpha between 1% and 100% breaks the border floor, so "
            + $"{nameof(TheBorderStaysClearOfItsFloorAgainstItsOwnTint)} cannot fail and is not "
            + "guarding anything");

        Assert.True(AppSeverityToTintBrushConverter.TintAlpha < ceiling,
            $"TintAlpha is {AppSeverityToTintBrushConverter.TintAlpha:P0} but the border floor "
            + $"breaks at {ceiling:P0}");
    }

    private static string SeverityHex(string variant, AppSeverity severity) =>
        HexFor(variant, AppSeverityToBrushConverter.KeyFor(severity));

    /// <summary>src-over compositing of <paramref name="fg"/> at alpha onto an opaque background.</summary>
    private static string Composite(string fg, string bg, double? alpha = null)
    {
        double a = alpha ?? AppSeverityToTintBrushConverter.TintAlpha;
        (int fr, int fg2, int fb) = Rgb(fg);
        (int br, int bg2, int bb) = Rgb(bg);

        int Mix(int f, int b) => (int)Math.Round((f * a) + (b * (1 - a)));

        return string.Create(CultureInfo.InvariantCulture,
            $"#{Mix(fr, br):X2}{Mix(fg2, bg2):X2}{Mix(fb, bb):X2}");
    }

    private static (int R, int G, int B) Rgb(string hex)
    {
        string h = hex.TrimStart('#');
        if (h.Length == 8)
        {
            h = h[2..];
        }

        int Part(int at) => int.Parse(
            h.Substring(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return (Part(0), Part(2), Part(4));
    }

    private static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        (int r, int g, int b) = Rgb(hex);

        static double Channel(double c) =>
            c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

        return (0.2126 * Channel(r / 255.0))
             + (0.7152 * Channel(g / 255.0))
             + (0.0722 * Channel(b / 255.0));
    }

    private static string HexFor(string variant, string key)
    {
        string axaml = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "src", "ClaudeForge", "App.axaml"));

        Match start = Regex.Match(axaml, $@"<ResourceDictionary\s+x:Key\s*=\s*""{variant}""");
        Assert.True(start.Success, $"no {variant} ThemeDictionaries entry in App.axaml");

        Match next = Regex.Match(
            axaml[(start.Index + start.Length)..],
            @"<ResourceDictionary\s+x:Key\s*=\s*""(?:Light|Dark)""");

        string block = next.Success
            ? axaml.Substring(start.Index, start.Length + next.Index)
            : axaml[start.Index..];

        Match m = Regex.Match(
            block,
            $@"x:Key\s*=\s*""{Regex.Escape(key)}""\s+Color\s*=\s*""(?<hex>#[0-9A-Fa-f]{{6,8}})""");

        Assert.True(m.Success, $"{key} is not declared in the {variant} variant of App.axaml");
        return m.Groups["hex"].Value;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClaudeForge.slnx")))
        {
            dir = dir.Parent;
        }

        MessageAssert.NotNull(dir, "could not locate the repository root (ClaudeForge.slnx)");
        return dir.FullName;
    }
}
