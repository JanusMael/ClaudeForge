using System.Globalization;
using Avalonia.Data.Converters;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Helpers;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

/// <summary>
/// Maps an <see cref="AppSeverity"/> to a translucent wash of its own colour, for use as the
/// background of a bordered callout.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Derived from the severity brush, not declared.</b> The alternative was an
/// <c>AppSeverity*BackgroundBrush</c> family: four tints, two variants, two apps — eight new
/// colours. This palette's own comments record that the severity brushes were reused from
/// already-vetted pairs *"so no unreviewed colour enters the palette"*, and eight new tints is
/// exactly what that rule is about. Compositing the existing colour over whatever sits behind
/// gives the right tint for every severity and both themes with no new colour at all.
/// </para>
/// <para>
/// ⛔ <b>The banner this exists for is SEVERITY-driven, which is why a single caution tint was
/// not an option.</b> <c>DangerAssessment</c> carries <c>Severity</c> and <c>IsDangerNow</c> as
/// independent values, and <c>TableDangerClassifier</c> computes them independently — severity
/// from <c>EscalatesAt</c>/<c>Tier</c>, whose <c>EscalatedTier</c> defaults to
/// <see cref="AppSeverity.Critical"/>, and <c>IsDangerNow</c> from the rule's <c>Unsafe</c>
/// predicate. A Critical banner is therefore the designed-for case, and an amber wash beneath a
/// red-bordered one would say the wrong thing.
/// </para>
/// <para>
/// ⚠ <b><see cref="TintAlpha"/> is bounded above by contrast, not by taste.</b> The callout draws
/// its border in the same colour this tint is made from, so raising alpha pulls the two together.
/// Measured on white, Caution's border against its own tint is 3.23:1 at 8%, 3.15:1 at 10%,
/// 3.07:1 at 12% and <b>2.96:1 at 15%</b> — under the 3.0:1 non-text floor. 10% keeps the wash
/// clearly visible (1.13–1.20 against the surface) with the border still clear of its floor.
/// <c>SeverityTintStaysLegibleTests</c> is what holds that, by recomputing rather than by quoting.
/// </para>
/// <para>
/// ⚠ <b>Resolution happens at Convert-time and the brush is TRACKED</b>, for the same reason
/// <see cref="AppSeverityToBrushConverter"/> does both: a converter only re-runs when its binding
/// source changes, and a severity does not change because the theme did. A tint minted fresh per
/// Convert would go stale on a theme switch — and a stale 10% wash reads as a slightly-off
/// background rather than as a wrong colour, so it would survive review.
/// </para>
/// </remarks>
public sealed class AppSeverityToTintBrushConverter : IValueConverter
{
    public static readonly AppSeverityToTintBrushConverter Instance = new();

    /// <summary>How much of the severity colour reaches the surface behind it.</summary>
    /// <remarks>
    /// ⚠ <b><c>static readonly</c> and not <c>const</c>, for two independent reasons.</b> This
    /// assembly ships as a NuGet package, and a <c>public const</c> is baked into the CONSUMER's
    /// IL at compile time — a consumer that did not recompile would keep an old alpha while every
    /// test here measured the new one. And a <c>const</c> lets the compiler fold a range check
    /// into a constant, which turned the premise assertion in
    /// <c>SeverityTintStaysLegibleTests</c> into something it proved always true (CS8793) — a
    /// tautological test that the compiler, to its credit, refused to build.
    /// </remarks>
    public static readonly double TintAlpha = 0.10;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        AppSeverity severity = value is AppSeverity s ? s : AppSeverity.Neutral;

        return BrushHelper.ResolveThemedTint(
            AppSeverityToBrushConverter.KeyFor(severity),
            AppSeverityToBrushConverter.FallbackFor(severity),
            TintAlpha);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
