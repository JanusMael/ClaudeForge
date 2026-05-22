using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

namespace Bennewitz.Ninja.ClaudeForge.Converters;

/// <summary>
/// Converts a <see cref="CommonActionKind"/> value to the chiclet brush
/// rendered behind the kind label in the Permissions › Common Actions
/// row template.  Looks up <c>common-action-brush-{kind}</c> from
/// application resources (defined in <c>Resources/CommonActionKindTheme.axaml</c>);
/// falls back to neutral grey on miss.
/// </summary>
/// <remarks>
/// Modeled on <see cref="ScopeToBrushConverter"/> so the resource-key
/// convention is consistent (<c>scope-brush-{id}</c> ↔
/// <c>common-action-brush-{kind}</c>) and the fallback behaviour is
/// identical.
/// </remarks>
public sealed class CommonActionKindToBrushConverter : IValueConverter
{
    private static readonly IBrush FallbackBrush = new SolidColorBrush(Color.Parse("#9E9E9E"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CommonActionKind kind)
        {
            return FallbackBrush;
        }

        string key = $"common-action-brush-{kind.ToString().ToLowerInvariant()}";
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