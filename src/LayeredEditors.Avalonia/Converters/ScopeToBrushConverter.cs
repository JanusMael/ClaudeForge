using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

/// <summary>
/// Converts an <see cref="IEditorScope"/> to a colored brush for the scope badge.
/// The brush is looked up from application resources using the key
/// <c>scope-brush-{scope.Id}</c>, so consuming apps can theme it freely.
/// </summary>
/// <example>
/// Add the following to your application's resource dictionary:
/// <code>
/// &lt;SolidColorBrush x:Key="scope-brush-user"    Color="#1976D2" /&gt;
/// &lt;SolidColorBrush x:Key="scope-brush-managed" Color="#D32F2F" /&gt;
/// </code>
/// </example>
public sealed class ScopeToBrushConverter : IValueConverter
{
    private static readonly IBrush FallbackBrush = new SolidColorBrush(Color.Parse("#9E9E9E"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IEditorScope scope)
        {
            return FallbackBrush;
        }

        string key = $"scope-brush-{scope.Id}";

        if (Application.Current is { } app)
        {
            app.Resources.TryGetResource(key, app.ActualThemeVariant, out object? res);
            if (res is IBrush brush)
            {
                return brush;
            }
        }

        return FallbackBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}