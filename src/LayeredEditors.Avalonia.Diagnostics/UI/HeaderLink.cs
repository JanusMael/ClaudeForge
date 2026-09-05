using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.UI;

/// <summary>
/// Builds the underlined, hand-cursor <see cref="TextBlock"/> that the live windows use as a
/// header link. One factory so every link carries the same accessibility surface: an explicit
/// <c>AutomationProperties.Name</c> (a <see cref="TextBlock"/> otherwise announces its
/// <see cref="TextBlock.Text"/>, which for the log-path link is a bare file path and for a
/// not-yet-filled link is nothing), a <c>HelpText</c> mirroring the tooltip, and the
/// <see cref="AutomationControlType.Hyperlink"/> role so a screen reader announces a link
/// rather than static text.
/// </summary>
internal static class HeaderLink
{
    /// <param name="text">Visible text. May be empty when the caller fills it in later.</param>
    /// <param name="automationName">Screen-reader name. Clean text: no emoji, no glyphs.</param>
    /// <param name="tooltip">Tooltip, also used as the accessibility help text.</param>
    /// <param name="foreground">Link colour.</param>
    /// <param name="onClick">Pointer-pressed handler.</param>
    internal static TextBlock Create(
        string text,
        string automationName,
        string tooltip,
        IBrush foreground,
        EventHandler<PointerPressedEventArgs> onClick)
    {
        TextBlock link = new()
        {
            Text = text,
            FontSize = 11,
            Foreground = foreground,
            Cursor = new Cursor(StandardCursorType.Hand),
            TextDecorations = TextDecorations.Underline,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(link, tooltip);
        AutomationProperties.SetName(link, automationName);
        AutomationProperties.SetHelpText(link, tooltip);
        AutomationProperties.SetControlTypeOverride(link, AutomationControlType.Hyperlink);
        link.PointerPressed += onClick;
        return link;
    }
}
