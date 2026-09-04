using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Bennewitz.Ninja.AgentForge.Sdk.Diagnostics;

namespace Bennewitz.Ninja.ClaudeForge.Converters;

/// <summary>
/// Maps a <see cref="ChangeKind"/> to the pill brush behind the save dialog's <c>+</c> / <c>-</c> /
/// <c>~</c> glyph.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>This exists so the colour is not in the view-model.</b>
/// <c>SaveChangeEntryViewModel</c> used to expose <c>KindBackground</c> returning a hex string,
/// which put presentation in a view-model and put three literals beyond the reach of the theme.
/// The view-model already exposes <see cref="ChangeKind"/>; a converter over that is the same
/// shape as <c>AppSeverityToBrushConverter</c>, and the <c>GuardRawHexInViewModels</c> build
/// target now fails on the old form.
/// </para>
/// <para>
/// ⚠ <b>Themed lookup, like the severity converter.</b> The <c>AppChangeKind*Brush</c> keys live
/// under <c>ResourceDictionary.ThemeDictionaries</c>, so a flat lookup finds nothing and silently
/// returns the fallback — re-introducing exactly the hardcoded colour this type deletes.
/// Resolution happens per <see cref="Convert"/> call rather than in a static field because the
/// host's resources are not loaded when the converter is constructed.
/// </para>
/// <para>
/// ⛔ <b>The pill is redundant coding, and that is what licenses its contrast budget.</b> The
/// glyph, the tooltip and <c>KindAccessibleName</c> each carry the change kind on their own, so
/// the fill is not "information required to identify a component". What must hold is the WHITE
/// GLYPH against the fill — ≥ 4.5:1, measured, for all three in both variants. Pill-against-
/// surface sits near 2.8:1 on the dark theme and is accepted deliberately: raising it means
/// lightening the fill, which lowers the glyph contrast that actually matters. Do not "fix" one
/// without re-measuring the other.
/// </para>
/// <para>
/// An unrecognised value maps to the modified brush rather than throwing: a converter that throws
/// inside a template takes the whole dialog down, and the dialog's own
/// <c>FormattedText</c> already falls back to the bare key for an unknown kind.
/// </para>
/// </remarks>
public sealed class ChangeKindToBrushConverter : IValueConverter
{
    /// <summary>Resource key for <paramref name="kind"/>.</summary>
    /// <remarks>
    /// Public and static so the coverage test asserts against the same string the converter asks
    /// for, rather than against its own copy of the naming convention.
    /// </remarks>
    public static string KeyFor(ChangeKind kind) => $"AppChangeKind{kind}Brush";

    /// <summary>
    /// Backstop only, reached when a host has failed to declare a key.
    /// <c>AppChangeKindTokenCoverageTests</c> asserts every member has both variants in both apps,
    /// so reaching one of these is a test failure elsewhere rather than a colour decision here.
    /// </summary>
    private static readonly IBrush AddedFallback = new SolidColorBrush(Color.Parse("#2E7D32"));
    private static readonly IBrush RemovedFallback = new SolidColorBrush(Color.Parse("#C62828"));
    private static readonly IBrush ModifiedFallback = new SolidColorBrush(Color.Parse("#B45309"));

    private static IBrush FallbackFor(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => AddedFallback,
        ChangeKind.Removed => RemovedFallback,
        var _ => ModifiedFallback,
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        ChangeKind kind = value is ChangeKind k ? k : ChangeKind.Modified;

        // Inline rather than via the library's BrushHelper: that helper is `internal` to
        // LayeredEditors.Avalonia, and widening a library's surface for one caller is the wrong
        // trade. ActualThemeVariant is the themed lookup — passing no variant finds nothing,
        // because these keys live under ThemeDictionaries.
        if (Application.Current is { } app
            && app.Resources.TryGetResource(KeyFor(kind), app.ActualThemeVariant, out object? res)
            && res is IBrush brush)
        {
            return brush;
        }

        return FallbackFor(kind);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
