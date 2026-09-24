using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Bennewitz.Ninja.ClaudeForge.Diagnostics;

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
    /// <param name="onClick">Invoked on click, Enter or Space.</param>
    /// <remarks>
    /// ⛔⛔ <b>THIS RETURNED A TextBlock, AND THAT MADE EVERY HEADER ACTION MOUSE-ONLY.</b>
    /// A TextBlock is not focusable and is not a tab stop, so the F12 window's only focusable
    /// control was its log ListBox: Tab appeared to do nothing, which was reported on
    /// 2026-09-14 as "tabbing gets stuck in the list of items". The list was innocent — there
    /// was simply nowhere else for focus to go, and the <i>Log file</i>, logs-folder and
    /// <i>Config-file events</i> actions could not be reached or activated from the keyboard
    /// at all.
    /// <para>
    /// ⚠ The automation properties below were already set, which is what made this easy to
    /// miss: a screen reader saw a Hyperlink in the tree, correctly named. It just could not
    /// be focused or invoked. <b>Announcing a control is not the same as exposing it.</b>
    /// </para>
    /// <para>
    /// A <see cref="Button"/> is the fix rather than <c>Focusable = true</c> on the TextBlock:
    /// it brings a focus adornment, Enter/Space activation and a real
    /// <c>ButtonAutomationPeer</c> with an Invoke pattern. A focusable TextBlock would be
    /// reachable but would draw no focus ring, which is its own accessibility failure.
    /// </para>
    /// </remarks>
    internal static Button Create(
        string text,
        string automationName,
        string tooltip,
        IBrush foreground,
        EventHandler<RoutedEventArgs> onClick)
    {
        TextBlock label = new()
        {
            Text = text,
            FontSize = 11,
            Foreground = foreground,
            TextDecorations = TextDecorations.Underline,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Chromeless so it still reads as a link: the Button is here for behaviour
        // (focus, Enter/Space, automation), not for a button's appearance.
        Button link = new()
        {
            Content = label,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(link, tooltip);
        AutomationProperties.SetName(link, automationName);
        AutomationProperties.SetHelpText(link, tooltip);
        AutomationProperties.SetControlTypeOverride(link, AutomationControlType.Hyperlink);
        link.Click += onClick;
        return link;
    }

    /// <summary>Replaces the visible text of a link built by <see cref="Create"/>.</summary>
    /// <remarks>
    /// The text lives on the Button's TextBlock content, not on the Button, so callers that
    /// used to assign <c>link.Text</c> go through here rather than reaching into the tree.
    /// </remarks>
    internal static void SetText(Button link, string text)
    {
        if (link.Content is TextBlock label)
        {
            label.Text = text;
        }
    }

    /// <summary>Applies a font family to a link built by <see cref="Create"/>.</summary>
    internal static void SetFontFamily(Button link, FontFamily family)
    {
        if (link.Content is TextBlock label)
        {
            label.FontFamily = family;
        }
    }
}
