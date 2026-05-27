using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk.Dialogs;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Views;

public partial class MainWindow : Window
{
    /// <summary>
    /// Latched once a user choice has resolved the unsaved-changes
    /// prompt on close.  Without this, calling Window.Close() from the
    /// dialog-resolution path re-enters OnClosing → re-shows the prompt
    /// → infinite loop.  Set in OnClosing's Save / DontSave branches
    /// before the Close() call so the second entry passes straight
    /// through.
    /// </summary>
    private bool _confirmedClose;

    /// <summary>
    /// Debounce timer for <see cref="WindowStateService"/> writes triggered
    /// by <see cref="SizeChanged"/> / <see cref="PositionChanged"/>. Each
    /// event resets the countdown; the save only fires when the user has
    /// stopped moving/resizing for <see cref="SaveDebounceMs"/>. The close
    /// path bypasses the timer and flushes the final geometry directly
    /// (see <see cref="OnClosed"/>).
    /// </summary>
    private DispatcherTimer? _saveDebounceTimer;

    /// <summary>
    /// Debounce window for window-state persistence. 500 ms is long enough
    /// to coalesce a continuous drag/resize (which fires events on every
    /// pixel of mouse movement) into a single save, and short enough that
    /// a user-initiated close immediately after dragging still has time to
    /// flush the final state via <see cref="OnClosed"/>.
    /// </summary>
    private const int SaveDebounceMs = 500;

    public MainWindow()
    {
        InitializeComponent();

        // NOTE: Icon assignment moved to OnOpened (below) so the underlying
        // SVG → PNG render in AppIcon.EnsureLoaded doesn't fire on the
        // dispatcher during MainWindow construction. The render is ~100-300 ms
        // and was previously on the critical first-paint path (visible in
        // cold-start profiles as a SKSvg.C... block under MainWindow's ctor).
        // Setting Icon from OnOpened produces a very brief "default icon"
        // flash on Windows/X11 before the rendered icon appears; on Wayland
        // Window.Icon is ignored entirely so there's no visual cost there.
        // Net: faster first paint with a tolerable icon-pop-in.

        // Ctrl+S → Save (other shortcuts wired via Window.KeyBindings in AXAML)
        KeyDown += OnKeyDown;

        // Restore geometry and theme before first render
        Opened += OnOpened;

        // Persist geometry while the window is in use
        SizeChanged += OnSizeChanged;
        PositionChanged += OnPositionChanged;

        // guard window-close on unsaved changes (W1).
        // Pre-fix: closing the window with edits in flight silently
        // exited and the edits were lost.  Now we cancel the close
        // and show a three-way Save / Don't Save / Cancel modal; on
        // Save or DontSave we set _confirmedClose and re-Close.
        Closing += OnClosing;

        // Dispose VM (file watcher, HTTP client) when window closes
        Closed += OnClosed;
    }

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    private void OnOpened(object? sender, EventArgs e)
    {
        // Set the window icon now that first paint has happened — moved out
        // of the ctor so the SVG render in AppIcon.EnsureLoaded doesn't
        // block first paint. Avalonia paints the icon update lazily on the
        // next idle tick. See the ctor comment for the full rationale.
        Icon = AppIcon.Instance;

        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        // Note: Ctrl+F search-focus is wired declaratively in MainWindow.axaml
        // via the FocusOnRequest attached behaviour on the SearchBox TextBox.
        // No View-side PropertyChanged subscriber needed here.

        // Apply persisted theme before the window is shown
        vm.ApplyRestoredTheme();

        // Restore window geometry
        SavedWindowGeometry geo = vm.GetSavedGeometry();
        Width = geo.W;
        Height = geo.H;

        if (geo.X.HasValue && geo.Y.HasValue)
        {
            // Clamp to screen bounds so the window isn't off-screen
            Screen? screen = Screens.ScreenFromWindow(this);
            if (screen != null)
            {
                PixelRect bounds = screen.WorkingArea;
                double cx = Math.Max(bounds.X, Math.Min(geo.X.Value, bounds.X + bounds.Width - 200));
                double cy = Math.Max(bounds.Y, Math.Min(geo.Y.Value, bounds.Y + bounds.Height - 100));
                Position = new PixelPoint((int)cx, (int)cy);
            }
            else
            {
                Position = new PixelPoint((int)geo.X.Value, (int)geo.Y.Value);
            }
        }
        else
        {
            // No saved position (first launch) — center on the primary screen's working area.
            Screen? primary = Screens.Primary;
            if (primary != null)
            {
                PixelRect area = primary.WorkingArea;
                int cx = area.X + (area.Width - (int)geo.W) / 2;
                int cy = area.Y + (area.Height - (int)geo.H) / 2;
                Position = new PixelPoint(cx, cy);
            }
        }
    }

    // Both handlers delegate to the same debounce path; we don't actually
    // need the event args because FlushWindowStateSave reads Width/Height/
    // Position directly off the Window at flush time, which means the saved
    // value is always the FINAL settled geometry rather than whatever
    // intermediate frame happened to trigger the last event.
    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        DebouncedSaveWindowState();
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        DebouncedSaveWindowState();
    }

    /// <summary>
    /// Schedules a window-state save for <see cref="SaveDebounceMs"/> after
    /// the most recent <see cref="SizeChanged"/> / <see cref="PositionChanged"/>
    /// event. Every new event resets the countdown, so a continuous drag or
    /// resize coalesces into one save instead of dozens.
    /// <para>
    /// Pre-fix: each event called <c>vm.SaveWindowState(...)</c> directly,
    /// which serializes JSON + writes the file (~20-40 ms each on a typical
    /// SSD). A single drag across the desktop produced hundreds of writes
    /// and dominated the session's CPU profile (cumulative ~250 ms / session
    /// in the captured trace).
    /// </para>
    /// </summary>
    private void DebouncedSaveWindowState()
    {
        if (_saveDebounceTimer is null)
        {
            _saveDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(SaveDebounceMs),
            };
            _saveDebounceTimer.Tick += OnSaveDebounceTick;
        }

        // Stop+Start resets the countdown so the timer fires
        // SaveDebounceMs after the LAST event, not the first.
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    private void OnSaveDebounceTick(object? sender, EventArgs e)
    {
        _saveDebounceTimer?.Stop();
        FlushWindowStateSave();
    }

    private void FlushWindowStateSave()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SaveWindowState(Width, Height, Position.X, Position.Y);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // Cancel any pending debounced save and flush the final geometry
        // directly. A late drag/resize that landed just before close would
        // otherwise be lost if the debounce timer hadn't elapsed yet.
        _saveDebounceTimer?.Stop();

        // Permanent: mark the shutdown boundary in the rolling log so a
        // session's "did the user quit?" question has a definitive answer
        // without having to infer it from the next session's startup line.
        // Includes HasUnsavedChanges so post-mortems can flag a quit-with-
        // pending-edits as a potential data-loss scenario.  Pairs with the
        // [Save] {Mode}-dialog choice log so the full lifecycle trail is
        // visible — user opens → makes edits → saves (or cancels) → quits.
        if (DataContext is MainWindowViewModel vm)
        {
            Log.Information("[App.Shutdown] Main window closing — hasUnsavedChanges={Unsaved}",
                vm.HasUnsavedChanges);
            vm.SaveWindowState(Width, Height, Position.X, Position.Y);
            vm.Dispose();
        }
        else
        {
            // No VM bound (e.g. early-shutdown crash path) — still mark the
            // boundary, just without the unsaved-changes signal.
            Log.Information("[App.Shutdown] Main window closing (no VM bound)");
        }
    }

    // -----------------------------------------------------------------------
    // Search popup
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reopens the search popup when the user refocuses the search box after
    /// light-dismiss closed it.  Without this, if the query text hasn't changed,
    /// <see cref="SearchViewModel.OnSearchQueryChanged"/> never fires and
    /// existing results stay hidden even though they still match the query.
    /// </summary>
    /// <remarks>
    /// search state lives on
    /// <see cref="MainWindowViewModel.Search"/>, not the MWVM directly.
    /// </remarks>
    private void OnSearchBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm
            && !string.IsNullOrWhiteSpace(vm.Search.SearchQuery)
            && vm.Search.SearchResults.Count > 0)
        {
            vm.Search.IsSearchOpen = true;
        }
    }

    // -----------------------------------------------------------------------
    // About dialog
    // -----------------------------------------------------------------------

    /// <summary>
    /// Opens the <see cref="AboutDialog"/> as a modal child of this window.
    /// Called from the bottom-right version button — the entire button
    /// rectangle (text + transparent background) is the click target.
    /// </summary>
    private async void OnVersionLabelClick(object? sender, RoutedEventArgs e)
    {
        AboutDialog about = new();
        await about.ShowDialog(this);
    }

    // -----------------------------------------------------------------------
    // Keyboard shortcuts
    // -----------------------------------------------------------------------

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // F12 — toggle the floating live-log window (always available; ships in Release).
        // Placed here alongside Ctrl+S following the established codebase convention of
        // handling keyboard shortcuts in code-behind rather than XAML KeyBindings.
        if (e.Key == Key.F12)
        {
            AvaloniaDiagnostics.ToggleLiveLogWindow();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                await vm.SaveCommand.ExecuteAsync(null);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Unsaved-changes-on-close guard (W1 2026-05-15)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Intercept the window-close to prompt the user when there are
    /// unsaved edits.  Three outcomes from the modal:
    /// <list type="bullet">
    ///   <item><b>Save</b>     — call <c>SaveCommand</c>, then re-close on success.</item>
    ///   <item><b>Don't Save</b> — close immediately, discarding edits.</item>
    ///   <item><b>Cancel / X</b> — keep the window open.</item>
    /// </list>
    /// The handler uses <c>_confirmedClose</c> as a re-entrancy latch:
    /// after the user picks Save / DontSave we set the latch and call
    /// <c>Close()</c>, which fires <c>Closing</c> a second time; the
    /// second entry sees the latch and passes through without
    /// re-prompting.
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Second-entry pass-through: user already resolved the prompt
        // and called Close() from the Save / DontSave branch.
        if (_confirmedClose)
        {
            return;
        }

        // No VM bound (early-shutdown crash path) — let the close proceed.
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        // No unsaved edits — nothing to guard.
        if (!vm.HasUnsavedChanges)
        {
            return;
        }

        // Cancel the close so the modal can run.  The await below would
        // otherwise allow the window to tear down underneath the dialog.
        e.Cancel = true;

        // Pull the dialog service the VM already uses.  Same instance —
        // the prompt runs as a modal child of THIS window.
        IDialogService? dialogService = vm.DialogServiceForViewAccess;
        if (dialogService is null)
        {
            // Defensive: no dialog service available (e.g. headless test
            // path) — fall back to closing without prompt to avoid an
            // un-closable window.  In production this branch is
            // unreachable because the VM ctor requires a dialog service.
            _confirmedClose = true;
            Close();
            return;
        }

        DialogMessage prompt = DialogMessage.Builder()
                                            .Text(Strings.TextUnsavedChangesOnClose)
                                            .Build();

        UnsavedChangesChoice choice = await dialogService.ShowUnsavedChangesAsync(
            Strings.TitleUnsavedChangesOnClose, prompt);

        Log.Information("[App.Close] Unsaved-changes prompt choice: {Choice}", choice);

        switch (choice)
        {
            case UnsavedChangesChoice.Save:
                // Trigger the normal save flow.  SaveCommand handles its
                // own dialogs (the SaveChangesDialog preview).  If the
                // user cancels THAT dialog, we leave the window open —
                // they backed out of the save.
                await vm.SaveCommand.ExecuteAsync(null);
                // Re-check after save: did it succeed?  If HasUnsavedChanges
                // is still true, the save was cancelled or failed — keep
                // the window open.  If false, the save worked and we can
                // close now.
                if (!vm.HasUnsavedChanges)
                {
                    _confirmedClose = true;
                    Close();
                }

                break;

            case UnsavedChangesChoice.DontSave:
                _confirmedClose = true;
                Close();
                break;

            case UnsavedChangesChoice.Cancel:
            default:
                // Window stays open — e.Cancel is already true.
                break;
        }
    }
}