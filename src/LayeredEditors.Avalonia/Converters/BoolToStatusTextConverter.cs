using System.Globalization;
using Avalonia.Data.Converters;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

/// <summary>
/// Converts a nullable bool (tri-state checkbox value) to a human-readable status label:
/// <c>null → "Unset"</c>, <c>true → "Enabled"</c>, <c>false → "Disabled"</c>.
///
/// Pair with <see cref="BoolToStatusBrushConverter"/> to give the checkbox row a
/// coloured status word so users can tell at a glance whether a setting is on,
/// off, or inheriting from a lower scope.
/// </summary>
public sealed class BoolToStatusTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            true => "Enabled",
            false => "Disabled",
            var _ => "Unset",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}