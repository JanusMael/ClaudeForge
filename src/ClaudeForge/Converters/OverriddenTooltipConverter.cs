using System.Globalization;
using Avalonia.Data.Converters;
using Bennewitz.Ninja.ClaudeForge.Localization;

namespace Bennewitz.Ninja.ClaudeForge.Converters;

/// <summary>
/// Converts the <c>IsOverridden</c> bool on an effective-settings row into a
/// human-readable tooltip string. Used by <c>EffectiveSettingsView.axaml</c>
/// where each row's "overridden" cell is a checkbox; the tooltip explains
/// what the checkbox state actually means in scope-priority terms so a user
/// who is unsure does not have to leave the page to look it up.
/// </summary>
public sealed class OverriddenTooltipConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true
            ? Strings.TipEffectiveOverriddenTrue
            : Strings.TipEffectiveOverriddenFalse;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}