using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Behaviors;

/// <summary>
/// Attached behaviour that gives a control keyboard focus whenever a
/// view-model-side counter increments.  Solves the "View-side focus, VM-
/// side trigger" split without putting <see cref="InputElement.Focus"/>
/// calls in the view-model or wiring per-consumer <c>PropertyChanged</c>
/// handlers in code-behind.
///
/// <para><b>Usage:</b></para>
/// <code>
/// &lt;!-- AXAML --&gt;
/// &lt;TextBox x:Name="SearchBox"
///          behaviors:FocusOnRequest.RequestId="{Binding SearchFocusRequestId}" /&gt;
/// </code>
/// <code>
/// // View-model
/// [ObservableProperty] private int _searchFocusRequestId;
///
/// [RelayCommand]
/// private void FocusSearch() =&gt; SearchFocusRequestId++;
/// </code>
///
/// <para><b>Why <c>int</c> and not <c>bool</c>:</b> a bool toggle would not
/// fire the change event on the second Ctrl+F press if the prior value was
/// already <c>true</c>.  Incrementing an int guarantees every change is
/// surfaced regardless of any prior value.  Treat the property as a "bumper"
/// rather than a state flag.</para>
///
/// <para><b>Optional caret behaviour:</b> when the focused control is a
/// <see cref="TextBox"/>, the caret is moved to the end of any existing
/// text so the user can immediately type without overwriting their
/// previous query.  This is desirable for search boxes (the canonical
/// consumer) and harmless for empty inputs.  If a future consumer needs
/// "focus + select-all" semantics instead, add a sibling
/// <c>SelectAllOnFocus</c> attached property.</para>
///
/// <para><b>Dispatcher safety:</b> the focus call is posted via
/// <see cref="Dispatcher.UIThread"/> with normal priority.  Bumping the
/// counter from a background thread (rare but possible) still focuses the
/// control on the next dispatcher cycle without throwing.</para>
///
/// <para>See <c>DataGridCopyValueBehavior.cs</c> for the codebase's preferred
/// attached-property style.</para>
/// </summary>
public static class FocusOnRequest
{
    // ── Attached properties ──────────────────────────────────────────────────

    /// <summary>
    /// Counter property — increment from the view-model to request focus on
    /// the attached control.  Initial value (<c>0</c>) does NOT trigger focus;
    /// only changes do.  This avoids stealing focus during initial binding
    /// resolution at window-open time.
    /// </summary>
    public static readonly AttachedProperty<int> RequestIdProperty =
        AvaloniaProperty.RegisterAttached<Control, int>("RequestId", typeof(FocusOnRequest));

    static FocusOnRequest()
    {
        RequestIdProperty.Changed.AddClassHandler<Control>(OnRequestIdChanged);
    }

    public static int GetRequestId(Control c)
    {
        return c.GetValue(RequestIdProperty);
    }

    public static void SetRequestId(Control c, int value)
    {
        c.SetValue(RequestIdProperty, value);
    }

    // ── Wiring ───────────────────────────────────────────────────────────────

    private static void OnRequestIdChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        // Suppress the initial 0 → 0 callback that Avalonia raises during
        // binding setup.  Real bumps always change the value; spurious no-op
        // changes get filtered here.
        if (e.OldValue is int oldId && e.NewValue is int newId && oldId == newId)
        {
            return;
        }

        // Post via the dispatcher rather than calling Focus() synchronously.
        // Two reasons:
        //   1. If the bump happens during a layout pass / property-change
        //      cascade, the visual tree may not be in a state where focus
        //      can land cleanly; the next dispatcher tick is safe.
        //   2. Lets the VM bump the counter from any thread without forcing
        //      consumers to marshal manually.
        Dispatcher.UIThread.Post(() =>
        {
            c.Focus();
            if (c is TextBox tb)
            {
                tb.CaretIndex = tb.Text?.Length ?? 0;
            }
        });
    }
}