using System.Globalization;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Converters;

/// <summary>
/// The size half of the dual-coded severity indicator.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>A points-only assertion would pass while the defect was still on screen.</b> The bug
/// F1 records is not that Critical and Caution had different point sizes, it is that they had the
/// SAME one and <c>⊗</c> draws smaller than <c>⚠</c> at equal points. A test asserting only
/// <c>SizeFor(Critical) &gt; SizeFor(Caution)</c> would go green at ×1.01 and the user would still
/// see Critical as the smaller glyph. <see cref="TheScalesBeatTheMeasuredInkDisparity"/> is the
/// test that actually holds the line, because it multiplies the scales by MEASURED ink heights
/// that this class does not get to choose.
/// </para>
/// <para>
/// ⚠ <b>Those measurements are Windows numbers</b>, taken through Avalonia + Skia at 14pt with
/// the default typeface. Neither <c>U+2297</c> nor <c>U+26A0</c> exists in Segoe UI, Inter or
/// Arial, so both resolve through the same font fallback and the figures held identically across
/// every family tried. A platform whose fallback draws the pair differently would change the
/// margin; it cannot invert the ranking, because the scales are strictly ordered.
/// </para>
/// </remarks>
[TestClass]
public sealed class AppSeverityToFontSizeConverterTests
{
    /// <summary>
    /// Ink HEIGHT at 14pt, measured rather than assumed. The line box is font-metric driven and
    /// identical for all four glyphs, so it cannot express this and is not what the eye compares.
    /// </summary>
    private static readonly Dictionary<AppSeverity, double> MeasuredInkAt14 = new()
    {
        [AppSeverity.Critical] = 8.52,
        [AppSeverity.Caution] = 10.70,
        [AppSeverity.Info] = 6.02,
        [AppSeverity.Neutral] = 10.14,
    };

    /// <summary>The two tier bases in the markup: a settings row, and everything narrower.</summary>
    private static readonly double[] TierBases = [14d, 11d];

    private static object Convert(object? value, object? parameter, CultureInfo? culture = null) =>
        AppSeverityToFontSizeConverter.Instance.Convert(
            value, typeof(double), parameter, culture ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// The premise, asserted before anything rests on it: the glyphs really do draw at different
    /// sizes for the same point size, and Critical really is the smaller one.
    /// </summary>
    [TestMethod]
    public void ThePremiseHolds_CriticalDrawsSmallerThanCautionAtEqualPoints()
    {
        Assert.IsTrue(MeasuredInkAt14[AppSeverity.Critical] < MeasuredInkAt14[AppSeverity.Caution],
            "the measured ink table no longer says Critical draws smaller than Caution, so the "
            + "defect this converter exists to fix is not the defect these numbers describe");

        Assert.IsTrue(MeasuredInkAt14[AppSeverity.Critical] < MeasuredInkAt14[AppSeverity.Neutral],
            "the measured ink table no longer says the hollow Neutral circle out-draws Critical. "
            + "That inversion is half of why the scales are as large as they are.");
    }

    /// <summary>
    /// ⭐ The load-bearing test. Scales are chosen to overcome the ink disparity, not merely to
    /// differ from each other.
    /// </summary>
    [TestMethod]
    public void TheScalesBeatTheMeasuredInkDisparity()
    {
        double DrawnInk(AppSeverity s) =>
            MeasuredInkAt14[s] * AppSeverityToFontSizeConverter.ScaleFor(s);

        double critical = DrawnInk(AppSeverity.Critical);

        foreach (AppSeverity quieter in (AppSeverity[])
                 [AppSeverity.Caution, AppSeverity.Info, AppSeverity.Neutral])
        {
            Assert.IsTrue(critical > DrawnInk(quieter),
                $"Critical draws {critical:F2} of ink against {quieter}'s {DrawnInk(quieter):F2}. "
                + "Critical is the loudest tier and must be the largest glyph on screen; a scale "
                + "that merely differs in POINTS is not enough, because ⊗ starts 20% shorter "
                + "than ⚠ and shorter than the hollow ○ as well.");
        }

        Assert.IsTrue(DrawnInk(AppSeverity.Caution) > DrawnInk(AppSeverity.Info),
            "Caution must out-draw Info, or the amber tier reads as quieter than the blue one");
    }

    /// <remarks>
    /// The report asked for the circles to stay exactly as they are. Recorded as a test so a
    /// later tidy-up does not sweep them into the scale table on the grounds of consistency.
    /// </remarks>
    [TestMethod]
    public void TheCirclesAreDeliberatelyUnscaled()
    {
        foreach (AppSeverity circle in (AppSeverity[]) [AppSeverity.Info, AppSeverity.Neutral])
        {
            Assert.AreEqual(1.0, AppSeverityToFontSizeConverter.ScaleFor(circle), 1e-9,
                $"{circle} is drawn with a circle and was reported as correct, so its size is "
                + "meant to equal the site's base");
        }
    }

    [TestMethod]
    public void EverySeverityGetsAFiniteScaleAboveZero()
    {
        foreach (AppSeverity s in Enum.GetValues<AppSeverity>())
        {
            double scale = AppSeverityToFontSizeConverter.ScaleFor(s);
            Assert.IsTrue(double.IsFinite(scale) && scale > 0,
                $"{s} scales by {scale}, which would render its glyph invisible or throw");
        }
    }

    [TestMethod]
    public void SizeForMultipliesTheTierBase()
    {
        foreach (double tierBase in TierBases)
        {
            foreach (AppSeverity s in Enum.GetValues<AppSeverity>())
            {
                Assert.AreEqual(
                    tierBase * AppSeverityToFontSizeConverter.ScaleFor(s),
                    AppSeverityToFontSizeConverter.SizeFor(s, tierBase),
                    1e-9,
                    $"{s} at base {tierBase}");
            }
        }
    }

    /// <summary>
    /// ⚠ The two tiers must stay distinct. A scale table applied to one hardcoded base would make
    /// a nav badge the same size as a settings row, which is the thing the ConverterParameter
    /// exists to prevent.
    /// </summary>
    [TestMethod]
    public void TheTiersStayDistinct()
    {
        foreach (AppSeverity s in Enum.GetValues<AppSeverity>())
        {
            Assert.IsTrue(
                AppSeverityToFontSizeConverter.SizeFor(s, 14d)
                > AppSeverityToFontSizeConverter.SizeFor(s, 11d),
                $"{s} renders the same size on the 14pt and 11pt tiers");
        }
    }

    [TestMethod]
    public void TheConverterAgreesWithSizeFor()
    {
        foreach (double tierBase in TierBases)
        {
            foreach (AppSeverity s in Enum.GetValues<AppSeverity>())
            {
                Assert.AreEqual(
                    AppSeverityToFontSizeConverter.SizeFor(s, tierBase),
                    (double) Convert(s, tierBase.ToString(CultureInfo.InvariantCulture)),
                    1e-9,
                    $"{s} at base {tierBase}");
            }
        }
    }

    /// <summary>
    /// ⛔ The premise for every fallback assertion below: the parameter is genuinely read. Without
    /// this, a converter that ignored its parameter entirely would satisfy the fallback tests,
    /// because a fallback and an ignored input are indistinguishable from the outside.
    /// </summary>
    [TestMethod]
    public void ThePremiseHolds_TheParameterIsActuallyRead()
    {
        Assert.AreNotEqual(
            (double) Convert(AppSeverity.Critical, "11"),
            (double) Convert(AppSeverity.Critical, "14"),
            "the ConverterParameter changes nothing, so the tier base is not being read and both "
            + "tiers would render at one size");
    }

    [TestMethod]
    [DataRow(null, DisplayName = "no parameter")]
    [DataRow("", DisplayName = "empty")]
    [DataRow("fourteen", DisplayName = "not a number")]
    [DataRow("0", DisplayName = "zero")]
    [DataRow("-11", DisplayName = "negative")]
    [DataRow("NaN", DisplayName = "NaN")]
    public void AnUnusableParameterFallsBackInsteadOfThrowing(string? parameter)
    {
        double size = (double) Convert(AppSeverity.Caution, parameter);

        Assert.AreEqual(
            AppSeverityToFontSizeConverter.SizeFor(
                AppSeverity.Caution, AppSeverityToFontSizeConverter.DefaultBaseSize),
            size,
            1e-9,
            "a converter that throws inside a template takes the whole page down, so an unusable "
            + "base must fall back to the default tier rather than propagate");
    }

    /// <summary>
    /// ⚠ A <c>ConverterParameter</c> is a literal authored in markup, so it is invariant text no
    /// matter whose machine renders it. Parsing it with the UI culture would make a fractional
    /// base parse in one locale and fall back in another.
    /// </summary>
    [TestMethod]
    public void AFractionalBaseParsesUnderACommaDecimalCulture()
    {
        CultureInfo german = CultureInfo.GetCultureInfo("de-DE");

        Assert.AreEqual(
            AppSeverityToFontSizeConverter.SizeFor(AppSeverity.Info, 13.5),
            (double) Convert(AppSeverity.Info, "13.5", german),
            1e-9,
            "\"13.5\" was read with the UI culture, so a comma-decimal locale either failed to "
            + "parse it or read it as 135");
    }

    /// <remarks>
    /// Mirrors <c>AppSeverityToBrushConverter</c>: an unrecognised value is the quiet tier rather
    /// than an exception, because "I do not know how severe this is" is not a reason to take the
    /// page down.
    /// </remarks>
    [TestMethod]
    public void AnUnrecognisedValueIsTreatedAsNeutral()
    {
        Assert.AreEqual(
            AppSeverityToFontSizeConverter.SizeFor(AppSeverity.Neutral, 14d),
            (double) Convert("not a severity", "14"),
            1e-9);

        Assert.AreEqual(
            AppSeverityToFontSizeConverter.SizeFor(AppSeverity.Neutral, 14d),
            (double) Convert(null, "14"),
            1e-9);
    }

    [TestMethod]
    public void ConvertBackIsNotSupported()
    {
        Assert.ThrowsExactly<NotSupportedException>(() =>
            AppSeverityToFontSizeConverter.Instance.ConvertBack(
                14d, typeof(AppSeverity), null, CultureInfo.InvariantCulture));
    }
}
