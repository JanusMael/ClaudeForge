using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Avalonia.Converters;

/// <summary>
/// Maps a <see cref="PermissionOutcome"/> to a status brush for the tester
/// verdict: Deny = red, Ask = amber, Allow = green, Default = neutral grey.
/// Theme-independent fixed colors (same approach as the app's severity chiclets)
/// so the verdict reads consistently in light and dark themes.
/// </summary>
public sealed class PermissionOutcomeToBrushConverter : IValueConverter
{
    private static readonly IBrush Deny = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
    private static readonly IBrush Ask = new SolidColorBrush(Color.FromRgb(0xF4, 0xB4, 0x00));
    private static readonly IBrush Allow = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly IBrush Neutral = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));

    /// <summary>Shared instance for <c>{x:Static}</c> use from AXAML.</summary>
    public static PermissionOutcomeToBrushConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is PermissionOutcome outcome
            ? outcome switch
            {
                PermissionOutcome.Deny => Deny,
                PermissionOutcome.Ask => Ask,
                PermissionOutcome.Allow => Allow,
                var _ => Neutral,
            }
            : Neutral;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
