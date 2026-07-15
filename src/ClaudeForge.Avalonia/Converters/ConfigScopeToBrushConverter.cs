using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Avalonia.Converters;

/// <summary>
/// Maps a <see cref="ConfigScope"/> to its badge brush, matching the app's
/// scope-chiclet palette (see <c>Resources/ScopeTheme.axaml</c>): Managed = red,
/// Local = amber, Project = green, User = blue. Fixed colors (theme-independent,
/// exactly as the app defines them) so the dry-run tester's scope chiclet reads
/// the same in light and dark without depending on the host's UI library.
/// </summary>
public sealed class ConfigScopeToBrushConverter : IValueConverter
{
    private static readonly IBrush Managed = new SolidColorBrush(Color.Parse("#D32F2F"));
    private static readonly IBrush Local = new SolidColorBrush(Color.Parse("#F57F17"));
    private static readonly IBrush Project = new SolidColorBrush(Color.Parse("#388E3C"));
    private static readonly IBrush User = new SolidColorBrush(Color.Parse("#1976D2"));
    private static readonly IBrush Neutral = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));

    /// <summary>Shared instance for <c>{x:Static}</c> use from AXAML.</summary>
    public static ConfigScopeToBrushConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ConfigScope scope
            ? scope switch
            {
                ConfigScope.Managed => Managed,
                ConfigScope.Local => Local,
                ConfigScope.Project => Project,
                ConfigScope.User => User,
                var _ => Neutral,
            }
            : Neutral;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
