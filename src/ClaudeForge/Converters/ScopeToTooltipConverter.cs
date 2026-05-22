using System.Globalization;
using Avalonia.Data.Converters;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Converters;

/// <summary>
/// Converts a scope value to a human-readable tooltip that explains what the scope
/// level means and which config file it corresponds to.
/// Accepts either an <see cref="IEditorScope"/> (the library's abstraction used in
/// <c>PropertyEditorWrapper</c> bindings) or a raw <see cref="ConfigScope"/> enum
/// (used by <c>EffectiveSettingsView</c>).
/// </summary>
public sealed class ScopeToTooltipConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // IEditorScope.Id is the lowercase scope name ("managed", "user", "project", "local").
        // ConfigScope is used directly in EffectiveSettingsView bindings.
        string? id = value switch
        {
            IEditorScope s => s.Id,
            ConfigScope cs => ScopeToBrushConverter.ConfigScopeId(cs),
            var _ => null,
        };

        return id switch
        {
            "managed" => "managed — organisation-controlled; highest priority, read-only",
            "user" => "user — your personal defaults (~/.claude/settings.json)",
            "project" => "project — shared with the repo (.claude/settings.json)",
            "local" => "local — machine-local overrides (.claude/settings.local.json)",
            var _ => null,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}