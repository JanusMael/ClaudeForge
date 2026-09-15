using System.Globalization;
using Avalonia.Data.Converters;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

/// <summary>
/// Scales a severity glyph's font size by how loud the severity is, so Critical outranks Caution
/// visually and not only chromatically.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The third member of the pair that was a pair.</b>
/// <see cref="AppSeverityToGlyphConverter"/> makes severity a shape and
/// <see cref="AppSeverityToBrushConverter"/> makes it a colour, on the same argument each time:
/// severity is a type, and what it looks like is a function of it. Size was the one dimension
/// still hardcoded, at NINE render sites, which is why Critical and Caution were necessarily
/// drawn at the same point size.
/// </para>
/// <para>
/// ⛔ <b>Equal point sizes do NOT mean equal drawn sizes, and that is the whole defect.</b>
/// Measured through Skia at 14pt, the glyphs' ink heights are:
/// <c>⚠</c> 10.70, <c>○</c> 10.14, <c>⊗</c> 8.52, <c>●</c> 6.02. So <c>⊗</c> draws at
/// <b>0.797×</b> <c>⚠</c> — and the hollow "nothing to do" circle drew LARGER than Critical.
/// The hierarchy the glyphs exist to express was inverted in two places, not one.
/// </para>
/// <para>
/// ⚠ <b>Which is why Critical's scale looks disproportionate and is not.</b> ×1.255 buys nothing
/// but parity with Caution; the lead starts above that. ×1.55 against Caution's ×1.15 puts
/// Critical's ink about 7% above Caution's, which is the smallest gap that reads as a rank rather
/// than as a rendering wobble.
/// </para>
/// <para>
/// ⚠ <b>The ratio is a WINDOWS measurement.</b> Neither <c>U+2297</c> nor <c>U+26A0</c> exists in
/// Segoe UI, Inter or Arial, so both resolve through the same font fallback and the ratio held
/// identically across every family tried. A platform whose fallback font draws these two glyphs
/// with different relative ink would shift the gap. It would not invert it: the scales are
/// strictly ordered, so Critical is larger than Caution on any font.
/// </para>
/// <para>
/// ⭐ <b>Info and Neutral are deliberately left at 1.0.</b> The circles are the quiet tiers and
/// were reported as correct; scaling them would be scope the report did not ask for. Critical
/// clears <c>○</c>'s 10.14 from about ×1.20 onward, so it outranks the circles too.
/// </para>
/// <para>
/// ⚠ <b>The base size is a ConverterParameter, not a constant here</b>, because there are two
/// tiers and they are not the same decision: a settings row draws its glyph at 14 and a nav badge,
/// search hit, effective-value cell or save-dialog line draws at 11. A scale multiplies whichever
/// base the site passes, so the tiers stay distinct while the ranking within each is identical.
/// </para>
/// <para>
/// ⛔ <b>A malformed parameter falls back rather than throwing.</b> A converter that throws inside
/// a template takes the whole page down, which is a far worse outcome than one glyph at the
/// default size. That does mean a typo in markup is invisible at runtime, so it is
/// <c>SeverityGlyphFontSizeMarkupTests</c> that actually catches it, not this method.
/// </para>
/// </remarks>
public sealed class AppSeverityToFontSizeConverter : IValueConverter
{
    public static readonly AppSeverityToFontSizeConverter Instance = new();

    /// <summary>
    /// The base used when a site passes no usable <c>ConverterParameter</c>. The 14pt tier,
    /// because that is the surface where a wrong size is most visible and so most likely to be
    /// noticed rather than lived with.
    /// </summary>
    public const double DefaultBaseSize = 14d;

    /// <summary>
    /// How much louder than the base this severity is drawn.
    /// </summary>
    /// <remarks>
    /// Public and static for the same reason <see cref="AppSeverityToBrushConverter.KeyFor"/> is:
    /// the tests assert against the function the converter actually calls, rather than against
    /// their own copy of the table.
    /// </remarks>
    public static double ScaleFor(AppSeverity severity) => severity switch
    {
        AppSeverity.Critical => 1.55,
        AppSeverity.Caution => 1.15,
        AppSeverity.Info => 1.0,
        var _ => 1.0,
    };

    /// <summary>The font size for <paramref name="severity"/> at a given tier base.</summary>
    public static double SizeFor(AppSeverity severity, double baseSize) =>
        baseSize * ScaleFor(severity);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        AppSeverity severity = value is AppSeverity s ? s : AppSeverity.Neutral;
        return SizeFor(severity, BaseSizeFrom(parameter));
    }

    /// <summary>
    /// ⚠ <b>Invariant culture, deliberately.</b> A <c>ConverterParameter</c> is authored as a
    /// literal in markup, so it is invariant text no matter what the user's culture is. Parsing it
    /// with <paramref name="culture"/> would make a base of <c>13.5</c> parse on one machine and
    /// fall back on another.
    /// </summary>
    private static double BaseSizeFrom(object? parameter)
    {
        double parsed = parameter switch
        {
            double d => d,
            string text when double.TryParse(
                text, NumberStyles.Float, CultureInfo.InvariantCulture, out double t) => t,
            var _ => double.NaN,
        };

        // A non-positive or non-finite base would render the glyph invisible or throw downstream,
        // which is the same "page is gone" outcome the fallback exists to avoid.
        return double.IsFinite(parsed) && parsed > 0 ? parsed : DefaultBaseSize;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
