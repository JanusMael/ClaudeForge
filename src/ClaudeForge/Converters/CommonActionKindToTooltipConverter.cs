using System.Globalization;
using Avalonia.Data.Converters;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

namespace Bennewitz.Ninja.ClaudeForge.Converters;

/// <summary>
/// Converts a <see cref="CommonActionKind"/> value to its localized one-sentence
/// description, used as the <c>ToolTip.Tip</c> on the kind chiclet rendered in
/// the Permissions › Common Actions row template.
/// </summary>
/// <remarks>
/// Strings are sourced from <c>Strings.resx</c> via the keys
/// <c>TipCommonActionKindRead</c>, <c>TipCommonActionKindWrite</c>,
/// <c>TipCommonActionKindNetwork</c>, and <c>TipCommonActionKindDestructive</c>
/// so culture switches re-resolve correctly without the View having to know
/// the enum-to-tooltip mapping. Mirrors <see cref="CommonActionKindToLabelConverter"/>'s
/// shape; both are registered side-by-side in the View's resource dictionary.
/// </remarks>
public sealed class CommonActionKindToTooltipConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            CommonActionKind.Read => Strings.TipCommonActionKindRead,
            CommonActionKind.Write => Strings.TipCommonActionKindWrite,
            CommonActionKind.Network => Strings.TipCommonActionKindNetwork,
            CommonActionKind.Destructive => Strings.TipCommonActionKindDestructive,
            var _ => string.Empty,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}