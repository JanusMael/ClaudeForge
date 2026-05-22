using System.Globalization;
using Avalonia.Data.Converters;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

/// <summary>
/// Converts an <see cref="IEditorScope"/> to its <see cref="IEditorScope.DisplayName"/>
/// for the scope badge label.
/// </summary>
public sealed class ScopeToDisplayNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is IEditorScope scope ? scope.DisplayName : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}