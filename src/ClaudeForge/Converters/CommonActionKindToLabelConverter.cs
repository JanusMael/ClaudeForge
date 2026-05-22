using System.Globalization;
using Avalonia.Data.Converters;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

namespace Bennewitz.Ninja.ClaudeForge.Converters;

/// <summary>
/// Converts a <see cref="CommonActionKind"/> value to the localized chiclet
/// label rendered inside the chiclet badge in the Permissions › Common
/// Actions row template.
/// </summary>
/// <remarks>
/// Strings are sourced from <c>Strings.resx</c> via the keys
/// <c>LabelCommonActionKindRead</c>, <c>LabelCommonActionKindWrite</c>,
/// <c>LabelCommonActionKindNetwork</c>, and <c>LabelCommonActionKindDestructive</c>
/// so culture switches re-resolve correctly without the View having to
/// know the enum-to-string mapping.
/// </remarks>
public sealed class CommonActionKindToLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            CommonActionKind.Read => Strings.LabelCommonActionKindRead,
            CommonActionKind.Write => Strings.LabelCommonActionKindWrite,
            CommonActionKind.Network => Strings.LabelCommonActionKindNetwork,
            CommonActionKind.Destructive => Strings.LabelCommonActionKindDestructive,
            var _ => string.Empty,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}