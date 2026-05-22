using Avalonia.Data.Converters;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

/// <summary>
/// Returns <c>true</c> when the input integer is greater than zero.
/// Used to show/hide count badges in the Hooks editor and other list views.
/// </summary>
public static class IsPositiveConverter
{
    public static readonly IValueConverter Instance =
        new FuncValueConverter<int?, bool>(n => n is > 0);
}