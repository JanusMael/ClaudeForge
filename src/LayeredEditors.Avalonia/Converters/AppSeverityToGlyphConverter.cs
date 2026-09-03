using System.Globalization;
using Avalonia.Data.Converters;
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
/// <c>AVALONIA-GOTCHAS.md</c>. <c>▲ ◆ ● ○</c> live in ordinary text fonts.
/// </para>
/// <para>
/// The shapes escalate in visual weight rather than being arbitrary: a filled triangle reads as
/// louder than a filled diamond, which reads louder than a dot, and a hollow dot reads as
/// "noted, nothing to do".
/// </para>
/// </remarks>
public sealed class AppSeverityToGlyphConverter : IValueConverter
{
    public static readonly AppSeverityToGlyphConverter Instance = new();

    /// <summary>The glyph for one severity.</summary>
    public static string GlyphFor(AppSeverity severity) => severity switch
    {
        AppSeverity.Critical => "▲",
        AppSeverity.Caution => "◆",
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
