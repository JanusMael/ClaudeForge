using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

/// <summary>
/// Renders environment-variable names in a consistent blue (#1565C0) so they
/// visually stand out from the rest of the DataGrid. Every name gets the same
/// treatment regardless of casing — the earlier regex that discriminated
/// UPPER_CASE names produced an inconsistent/"missing text" feel in rows whose
/// names did not match the pattern.
/// </summary>
public sealed class EnvVarNameToBrushConverter : IValueConverter
{
    // ImmutableSolidColorBrush is safe to construct on any thread (static field).
    private static readonly IBrush EnvVarBrush = new ImmutableSolidColorBrush(Color.Parse("#1565C0"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return EnvVarBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}