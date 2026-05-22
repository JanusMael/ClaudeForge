using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Converters;

/// <summary>
/// Converts a scope value to a colored brush for the scope badge. Accepts either
/// an <see cref="IEditorScope"/> (editor abstraction) or a <see cref="ConfigScope"/>
/// enum value (used by <c>EffectivePropertyRow</c>).
/// Looks up <c>scope-brush-{id}</c> from application resources; falls back to grey.
/// </summary>
public sealed class ScopeToBrushConverter : IValueConverter
{
    private static readonly IBrush FallbackBrush = new SolidColorBrush(Color.Parse("#9E9E9E"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? id = value switch
        {
            IEditorScope s => s.Id,
            ConfigScope cs => ConfigScopeId(cs),
            var _ => null,
        };
        if (id is null)
        {
            return FallbackBrush;
        }

        string key = $"scope-brush-{id}";
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

    /// <summary>
    /// Map a <see cref="ConfigScope"/> enum to the lowercase id used in
    /// <c>scope-brush-*</c> resource keys (see <c>ScopeTheme.axaml</c>).
    /// </summary>
    internal static string ConfigScopeId(ConfigScope scope)
    {
        return scope switch
        {
            ConfigScope.Managed => "managed",
            ConfigScope.User => "user",
            ConfigScope.Project => "project",
            ConfigScope.Local => "local",
            var _ => scope.ToString().ToLowerInvariant(),
        };
    }
}