using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

/// <summary>
/// Maps an <see cref="AppSeverity"/> to a distinct geometric glyph, so severity is legible
/// without relying on colour.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The companion to <see cref="AppSeverityToBrushConverter"/>, and the reason the pair
/// exists.</b> <c>UI-STYLE-GUIDE.md</c>'s status-pill principle is that every indicator is
/// dual-coded: colour alone excludes colour-blind users, and roughly 8% of men have some form of
/// red-green deficiency — exactly the axis Critical-vs-Caution sits on. The SHAPE carries the
/// same information the colour does.
/// </para>
/// <para>
/// ⚠ <b>Geometric shapes, deliberately not emoji.</b> Emoji glyphs need a system emoji font to
/// fall back to and silently render as tofu without one — a documented problem on Linux in
/// <c>AVALONIA-GOTCHAS.md</c>. <c>⊗ ⚠ ● ○</c> live in ordinary text fonts.
/// </para>
/// <para>
/// ⛔ <b>The shapes do NOT escalate in drawn size, and assuming they did was defect F1.</b>
/// Measured through Skia at 14pt, the ink heights run <c>⚠</c> 10.70, <c>○</c> 10.14,
/// <c>⊗</c> 8.52, <c>●</c> 6.02 — so at one shared point size the loudest tier drew smaller than
/// the quietest, and Critical drew smaller than the hollow "noted, nothing to do" circle. Rank is
/// carried by <see cref="AppSeverityToFontSizeConverter"/>, whose per-severity scales exist to
/// overcome exactly that disparity.
/// </para>
/// <para>
/// ⚠ <b>An earlier version of this remark claimed the escalation as a property of the shapes.</b>
/// It was describing a different set of glyphs (<c>▲ ◆ ● ○</c>) and was never re-measured when
/// they changed, so it went on reassuring readers about a hierarchy that had quietly inverted.
/// </para>
/// </remarks>
public sealed class AppSeverityToGlyphConverter : IValueConverter
{
    public static readonly AppSeverityToGlyphConverter Instance = new();

    /// <summary>
    /// The face every severity glyph is drawn in. Bind it with
    /// <c>FontFamily="{x:Static …:AppSeverityToGlyphConverter.GlyphFontFamily}"</c> at every
    /// site that draws one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔⛔ <b>Without this, the four glyphs do not come from one font, and the size scales
    /// in <see cref="AppSeverityToFontSizeConverter"/> are therefore meaningless.</b> Measured
    /// on Windows through Skia's own fallback: <c>⚠</c> resolves to <b>Segoe UI Emoji</b> and
    /// <c>⊗</c> to <b>Segoe UI Symbol</b>. Two faces, two metrics — and <c>⚠</c> arrives as a
    /// COLOUR BITMAP, which is a third difference again.
    /// </para>
    /// <para>
    /// ⛔ <b>The numbers, because the ratio is the whole argument.</b> Ink heights at the 14
    /// tier: Caution 15.000 (bitmap), Critical 13.213 — Critical is <b>0.881×</b> Caution, and
    /// at the 11 tier <b>0.865×</b>. So the loudest tier drew SMALLER than the next one down,
    /// worse at the smaller tier, which is exactly how it was reported. Pinned to Segoe UI
    /// Symbol the same scales give Critical <b>1.074×</b> Caution, with Info and Neutral at
    /// 0.824× below both — the ranking the scales were tuned for.
    /// </para>
    /// <para>
    /// ⛔ <b>A colour bitmap glyph also IGNORES <c>Foreground</c>.</b> So before this, the
    /// Caution glyph was not tinted by the severity brush at all — it drew in the emoji font's
    /// own yellow on every surface, silently, while the markup said otherwise.
    /// </para>
    /// <para>
    /// ⚠ <b><see cref="AppSeverityToFontSizeConverter"/> states that both glyphs "resolve
    /// through the same font fallback".</b> That premise was false on Windows and is what this
    /// member establishes. Do not remove it and keep the scales.
    /// </para>
    /// <para>
    /// ⚠ <b>U+FE0E (text-presentation selector) was tried first and is NOT what fixed it.</b>
    /// It is the standards-based answer and names no platform font, but it could not be shown
    /// to work: font matching is codepoint-based, so a variation selector is invisible to it,
    /// and the probe could only report "not disproven". Naming the face is what was measured.
    /// </para>
    /// <para>
    /// ⭐ <b>The font is BUNDLED, not named, so the answer is the same on every platform.</b>
    /// An earlier fix named a per-platform stack — Segoe UI Symbol, Apple Symbols, DejaVu
    /// Sans — which is correct on Windows and merely *probable* elsewhere. JetBrains Mono NL
    /// carries all four codepoints as outlines, so there is no fallback to be wrong about.
    /// Measured in it: Caution 10.360, Critical/Info/Neutral 8.680 each, which under the
    /// existing scales puts Critical at <b>1.129×</b> Caution with the quiet tiers below both.
    /// </para>
    /// <para>
    /// ⚠ <b>The trailing families are a floor, not a plan.</b> They matter only if the
    /// embedded resource fails to load, which would itself be the bug worth finding.
    /// </para>
    /// </remarks>
    public static readonly FontFamily GlyphFontFamily =
        new("avares://LayeredEditors.Avalonia/Assets/Fonts#JetBrains Mono NL");

    /// <summary>The glyph for one severity.</summary>
    public static string GlyphFor(AppSeverity severity) => severity switch
    {
        // Windows' three-icon convention, which is the mental model users bring: an X in a
        // circle for an error, an exclamation in a triangle for a warning, a dot for
        // information. Reported 2026-09-14: the bare triangle "doesn't grok" because it lacks
        // the exclamation that makes it read as a warning.
        //
        // ⚠ is BARE, with no U+FE0E text-variation selector. U+26A0 already defaults to TEXT
        // presentation (Emoji_Presentation=No), so the selector is belt-and-braces - and
        // EveryGlyphIsASingleBmpCharacterNotAnEmoji rejects any glyph longer than one UTF-16
        // unit. ⚠+FE0E is two BMP units, not the surrogate pair that guard was written for, so
        // the guard's REASON does not apply here while its RULE still fires. Keeping the guard
        // intact is the better trade: if a platform renders this as colour emoji, add FE0E and
        // widen the guard deliberately rather than loosening it pre-emptively.
        // ⚠ Confirmed on WINDOWS by the 2026-09-14 retest: the screenshots show a themed
        // triangle, not colour emoji and not tofu, so the reasoning above holds there. Linux and
        // macOS are still unconfirmed, and that is where the tofu risk actually lives.
        //
        // ⚠ is deliberately NOT used for Critical as well. Two warning triangles differing
        // only in hue would put Critical and Caution on the red-amber axis with no shape to
        // separate them - which is exactly what the dual coding exists to prevent, and is
        // unrecoverable for a red-green colour-blind user.
        AppSeverity.Critical => "⊗",
        AppSeverity.Caution => "⚠",
        AppSeverity.Info => "●",
        _ => "○",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return GlyphFor(value is AppSeverity s ? s : AppSeverity.Neutral);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
