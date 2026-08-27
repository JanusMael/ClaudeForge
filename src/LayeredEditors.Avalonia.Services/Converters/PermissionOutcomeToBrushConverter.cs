using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Bennewitz.Ninja.AgentForge.Abstractions.Permissions;

namespace Bennewitz.Ninja.Layer.Avalonia.Services.Converters;

/// <summary>
/// Maps a <see cref="PermissionOutcome"/> to a status brush for a tester verdict:
/// Deny = red, Ask = amber, Allow = green, Default = neutral grey.
/// Theme-independent fixed colors (same approach as the app's severity chiclets) so the verdict
/// reads consistently in light and dark themes.
/// </summary>
/// <remarks>
/// <para>
/// Lives here rather than in either product because <see cref="PermissionOutcome"/> is the one
/// piece of permission vocabulary both products genuinely share, so a converter over it is
/// product-neutral by construction. Phase 6 identified the move and left it, having measured that
/// the obvious shared home — the app shell — would burden <c>ClaudeForge.Avalonia</c>, a library
/// whose value is being droppable into any Claude-adjacent tool, with a dependency on an
/// application shell.
/// </para>
/// <para>
/// This project costs less: it already carries both prerequisites (Avalonia, and
/// <c>AgentForge.Abstractions</c> for the enum), and <c>ClaudeForge.Avalonia</c> already
/// references it — so the whole move adds exactly one lightweight edge, from
/// <c>OpenCode.Avalonia</c>.
/// </para>
/// <para>
/// ⚠ The four literals below are among the hardcoded severity hexes <b>Phase 11.5</b> migrates to
/// <c>AppSeverity{Critical,Caution,Info,Neutral}Brush</c> tokens. They are copied here unchanged
/// on purpose: changing colours during a move makes the move unreviewable, and the tokens do not
/// exist yet.
/// </para>
/// </remarks>
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
