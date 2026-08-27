using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Keybinds;

/// <summary>
/// Code-behind for the keybinds editor view.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>This is the only non-empty code-behind in the product's specialised editors, and the reason
/// is specific: key capture is inherently a view concern.</b> Every row action is still a command on
/// its own row view-model — the rule that keeps ancestor bindings, and the <c>IL2026</c> build error
/// they cause, out of this repo. What is here is the one thing a view-model cannot do: observe a
/// keystroke. The translation itself lives in <see cref="OpenCodeKeyCapture"/> so it is testable
/// without a window, and this handler is the three lines that cannot be.
/// </para>
/// <para>
/// ⚠⚠ <b>The handler is registered on the TUNNEL (preview) route, with <c>handledEventsToo</c>.</b>
/// Capture has to win against the controls underneath it: a bubbling handler never sees
/// <c>Space</c>, <c>Enter</c> or the arrow keys, because the <c>TextBox</c> and <c>ListBox</c> in
/// this template consume them first — so capturing <c>Ctrl</c>+<c>Space</c> would silently record
/// nothing while looking like it was listening.
/// </para>
/// </remarks>
public partial class OpenCodeKeybindEditorView : UserControl
{
    /// <summary>Creates the view.</summary>
    public OpenCodeKeybindEditorView()
    {
        InitializeComponent();

        AddHandler(
            KeyDownEvent,
            OnPreviewKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    /// <summary>
    /// Route a keystroke to whichever binding row is waiting for one.
    /// </summary>
    /// <remarks>
    /// Does nothing unless a row is capturing, so the search box and the list keep their ordinary
    /// keyboard behaviour the rest of the time. <c>Escape</c> is spent leaving capture rather than
    /// being recorded — it is the way out of a mode, and a user who wants Escape as a binding can
    /// type its name.
    /// </remarks>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not OpenCodeKeybindEditorViewModel editor)
        {
            return;
        }

        OpenCodeKeyBindingViewModel? capturing = null;
        foreach (OpenCodeKeybindActionViewModel action in editor.Actions)
        {
            foreach (OpenCodeKeyBindingViewModel binding in action.Bindings)
            {
                if (binding.IsCapturing)
                {
                    capturing = binding;
                    break;
                }
            }

            if (capturing is not null)
            {
                break;
            }
        }

        if (capturing is null)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            capturing.IsCapturing = false;
            e.Handled = true;
            return;
        }

        if (!OpenCodeKeyCapture.TryTranslate(e.Key, e.KeyModifiers, out CapturedKey captured))
        {
            // A modifier on its own. Swallowed rather than ignored, so reaching for Ctrl+K does not
            // move the list selection on the way to the second key.
            e.Handled = true;
            return;
        }

        capturing.ApplyCapture(captured);
        e.Handled = true;
    }
}
