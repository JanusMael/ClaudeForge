using System.Globalization;
using Avalonia.Data.Converters;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Converters;

/// <summary>
/// Converts a scope value to a short display label for the scope badge pill.
/// Accepts either an <see cref="IEditorScope"/> (editor abstraction) or a
/// <see cref="ConfigScope"/> enum value (used by <c>EffectivePropertyRow</c>).
/// Unknown / unmapped values fall back to <c>value.ToString()</c> so the pill
/// never renders empty.
/// </summary>
public sealed class ScopeToDisplayNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            null => null,
            IEditorScope es => es.DisplayName,
            ConfigScope cs => DisplayFor(cs),
            var _ => value.ToString(),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Short, capitalized label for a <see cref="ConfigScope"/>. Centralized here so
    /// the Effective Settings pill uses consistent wording with the rest of the UI.
    /// </summary>
    internal static string DisplayFor(ConfigScope scope)
    {
        return scope switch
        {
            ConfigScope.Managed => "Managed",
            ConfigScope.User => "User",
            ConfigScope.Project => "Project",
            ConfigScope.Local => "Local",
            var _ => scope.ToString(),
        };
    }
}