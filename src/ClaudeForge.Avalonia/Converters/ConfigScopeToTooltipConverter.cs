using System.Globalization;
using Avalonia.Data.Converters;
using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Avalonia.Converters;

/// <summary>
/// Tooltip for the dry-run tester's scope chiclet — explains what the scope level
/// means and which config file it maps to. Text mirrors the app's
/// <c>ScopeToTooltipConverter</c> so the chiclet reads the same as scope chiclets
/// elsewhere in the app.
/// </summary>
public sealed class ConfigScopeToTooltipConverter : IValueConverter
{
    /// <summary>Shared instance for <c>{x:Static}</c> use from AXAML.</summary>
    public static ConfigScopeToTooltipConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // `when` guards rather than constant patterns — see ConfigScopeToBrushConverter.
        return value is ConfigScope scope
            ? scope switch
            {
                _ when scope == ConfigScope.Managed => "managed — organisation-controlled; highest priority, read-only",
                _ when scope == ConfigScope.User => "user — your personal defaults (~/.claude/settings.json)",
                _ when scope == ConfigScope.Project => "project — shared with the repo (.claude/settings.json)",
                _ when scope == ConfigScope.Local => "local — machine-local overrides (.claude/settings.local.json)",
                var _ => null,
            }
            : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
