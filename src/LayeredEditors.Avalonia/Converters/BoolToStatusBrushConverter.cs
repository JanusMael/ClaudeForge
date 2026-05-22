using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Helpers;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

/// <summary>
/// Converts a nullable bool (tri-state checkbox value) to a status foreground brush:
/// <c>null → gray #9E9E9E</c>, <c>true → green #2E7D32</c>, <c>false → red #C62828</c>.
///
/// Colors are chosen to read well on both Semi Light and Dark themes. Paired with
/// <see cref="BoolToStatusTextConverter"/> to produce a coloured "Enabled / Disabled / Unset"
/// label next to each tri-state <see cref="global::Avalonia.Controls.CheckBox"/>.
/// <para>
/// The default colors are defined in <c>EditorColors.axaml</c> (keys <c>LE.BoolEnabled</c>,
/// <c>LE.BoolDisabled</c>, <c>LE.BoolUnset</c>). Host applications can override any key in
/// their <c>Application.Resources</c> to retheme without forking the library.
/// </para>
/// </summary>
public sealed class BoolToStatusBrushConverter : IValueConverter
{
    // Properties (not fields) so BrushHelper reads Application.Current resources at
    // Convert()-time, after the host's resource dictionary is fully loaded.
    private static IBrush EnabledBrush => BrushHelper.Resolve("LE.BoolEnabled", "#2E7D32");
    private static IBrush DisabledBrush => BrushHelper.Resolve("LE.BoolDisabled", "#C62828");
    private static IBrush UnsetBrush => BrushHelper.Resolve("LE.BoolUnset", "#9E9E9E");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            true => EnabledBrush,
            false => DisabledBrush,
            var _ => UnsetBrush,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}