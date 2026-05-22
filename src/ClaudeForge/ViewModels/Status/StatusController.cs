using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Status;

/// <summary>
/// Owns the centre-status-bar's text + lifecycle.  Replaces the prior
/// "bare <c>string? StatusMessage</c> property that nothing ever cleared".
/// </summary>
/// <remarks>
/// <para>
/// The controller is an <see cref="ObservableObject"/> so the View can bind
/// directly to its <see cref="Text"/> / <see cref="Kind"/> / <see cref="IsDismissible"/>
/// surface; <see cref="MainWindowViewModel"/> hangs one of these off itself
/// (<c>Status</c> property) and routes every status update through
/// <see cref="Set"/>.
/// </para>
/// <para>
/// Lifecycle rules (locked by <c>StatusControllerTests</c>):
/// </para>
/// <list type="bullet">
///   <item><see cref="StatusKind.Success"/> auto-clears after
///         <see cref="SuccessAutoClearDelay"/> (default 6 s) — the user got
///         their confirmation; staleness afterward is just noise.</item>
///   <item><see cref="StatusKind.Warning"/> auto-clears after
///         <see cref="WarningAutoClearDelay"/> (default 10 s) — longer
///         because the user usually wants slightly more dwell time on
///         "nothing to do" / "no SDKs loaded yet" notices.</item>
///   <item><see cref="StatusKind.Failure"/> NEVER auto-clears.  The View
///         shows a close (×) button that invokes <see cref="Dismiss"/>;
///         the next <see cref="Set"/> call also overrides.</item>
///   <item><see cref="StatusKind.Active"/> sticks until the next
///         <see cref="Set"/> call.  Tied to the lifecycle of the operation
///         that emitted it; the caller is responsible for emitting a
///         terminal state when the operation finishes.</item>
///   <item><see cref="StatusKind.State"/> sticks until replaced — long-
///         lived identity text like "Ready" or "Project: foo".</item>
/// </list>
/// <para>
/// <strong>Threading:</strong> auto-clear schedules through
/// <see cref="Dispatcher.UIThread"/> so the property mutations always land
/// on the Avalonia UI thread, mirroring the pattern in
/// <c>BackupRestoreViewModel.RebuildBackupListAsync</c>.  Tests inject a
/// synchronous delay function via <see cref="DelayOverride"/> to make
/// timing deterministic.
/// </para>
/// </remarks>
public sealed partial class StatusController : ObservableObject, IDisposable
{
    /// <summary>Production default — keep in lockstep with <see cref="ResetForTesting"/>.</summary>
    private static readonly TimeSpan DefaultSuccessAutoClearDelay = TimeSpan.FromSeconds(6);

    /// <summary>Production default — keep in lockstep with <see cref="ResetForTesting"/>.</summary>
    private static readonly TimeSpan DefaultWarningAutoClearDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a <see cref="StatusKind.Success"/> message stays on screen
    /// before auto-clear.  Tunable via field-assign in tests to keep auto-
    /// clear assertions snappy.
    /// </summary>
    internal static TimeSpan SuccessAutoClearDelay { get; set; } = DefaultSuccessAutoClearDelay;

    /// <summary>
    /// How long a <see cref="StatusKind.Warning"/> message stays on screen
    /// before auto-clear.  Longer than <see cref="SuccessAutoClearDelay"/>
    /// because warnings ("nothing to save") deserve more dwell time.
    /// </summary>
    internal static TimeSpan WarningAutoClearDelay { get; set; } = DefaultWarningAutoClearDelay;

    /// <summary>
    /// Test seam — synchronous delay function injected by
    /// <c>StatusControllerTests</c> so auto-clear is deterministic.  When
    /// null (production), <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
    /// is used.
    /// </summary>
    internal static Func<TimeSpan, CancellationToken, Task>? DelayOverride { get; set; }

    /// <summary>
    /// Restore every <c>internal static</c> test seam on this class to
    /// its production default.  Tests call this from <c>[TestCleanup]</c>
    /// so a test that mutates one or more seams doesn't bleed into the
    /// next test in the run.
    /// </summary>
    /// <remarks>
    /// Convention: every new test seam added to this
    /// class MUST also get a line in this method.  Project precedent:
    /// <c>DebugFlags.ResetForTesting()</c>.  A missed seam silently
    /// leaks state across tests — exactly the bug this method exists
    /// to prevent.
    /// </remarks>
    internal static void ResetForTesting()
    {
        SuccessAutoClearDelay = DefaultSuccessAutoClearDelay;
        WarningAutoClearDelay = DefaultWarningAutoClearDelay;
        DelayOverride = null;
    }

    private CancellationTokenSource? _autoClearCts;
    private bool _disposed;

    /// <summary>Visible status text — bound to the centre status-bar TextBlock.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasText))]
    private string? _text;

    /// <summary>Lifecycle / severity category — drives icon + colour selection.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDismissible))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(IsSuccess))]
    [NotifyPropertyChangedFor(nameof(IsWarning))]
    [NotifyPropertyChangedFor(nameof(IsFailure))]
    [NotifyPropertyChangedFor(nameof(IsState))]
    private StatusKind _kind;

    /// <summary>True iff <see cref="Text"/> is non-empty — drives Border visibility.</summary>
    public bool HasText => !string.IsNullOrEmpty(Text);

    /// <summary>
    /// True iff the View should show a close (×) button.  Only
    /// <see cref="StatusKind.Failure"/> qualifies — successes auto-clear,
    /// warnings auto-clear, active/state are non-actionable in the bar.
    /// </summary>
    public bool IsDismissible => Kind == StatusKind.Failure;

    // Per-kind booleans so the View can flip per-kind IsVisible on icon glyphs
    // / theme brushes without needing an enum-to-X converter.
    public bool IsActive => Kind == StatusKind.Active;
    public bool IsSuccess => Kind == StatusKind.Success;
    public bool IsWarning => Kind == StatusKind.Warning;
    public bool IsFailure => Kind == StatusKind.Failure;
    public bool IsState => Kind == StatusKind.State;

    /// <summary>
    /// Replace the current status with <paramref name="text"/> +
    /// <paramref name="kind"/>.  Cancels any pending auto-clear from the
    /// previous message and schedules a fresh one if the new kind is
    /// <see cref="StatusKind.Success"/> or <see cref="StatusKind.Warning"/>.
    /// </summary>
    /// <remarks>
    /// Pass <see cref="StatusKind.None"/> with a null text to clear the bar
    /// explicitly — though for that case <see cref="Dismiss"/> reads more
    /// clearly.
    /// </remarks>
    public void Set(string? text, StatusKind kind)
    {
        CancelPendingAutoClear();
        Text = text;
        Kind = string.IsNullOrEmpty(text) ? StatusKind.None : kind;

        TimeSpan? autoClearAfter = kind switch
        {
            StatusKind.Success => SuccessAutoClearDelay,
            StatusKind.Warning => WarningAutoClearDelay,
            var _ => (TimeSpan?)null,
        };
        if (autoClearAfter is { } delay)
        {
            ScheduleAutoClear(delay);
        }
    }

    /// <summary>
    /// Clear the status text + reset the kind to <see cref="StatusKind.None"/>.
    /// Invoked from the View's close-(×) button (bound to
    /// <c>MainWindowViewModel.DismissStatusCommand</c>) and from
    /// <see cref="Set"/>'s "empty text" branch.
    /// </summary>
    public void Dismiss()
    {
        CancelPendingAutoClear();
        Text = null;
        Kind = StatusKind.None;
    }

    private void CancelPendingAutoClear()
    {
        if (_autoClearCts is null)
        {
            return;
        }

        try
        {
            _autoClearCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _autoClearCts.Dispose();
        _autoClearCts = null;
    }

    private void ScheduleAutoClear(TimeSpan after)
    {
        CancellationTokenSource cts = new();
        _autoClearCts = cts;
        _ = AutoClearAsync(cts, after);
    }

    private async Task AutoClearAsync(CancellationTokenSource cts, TimeSpan delay)
    {
        try
        {
            Func<TimeSpan, CancellationToken, Task> delayFn = DelayOverride ?? ((d, t) => Task.Delay(d, t));
            await delayFn(delay, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyClear();
            return;
        }

        // Defence-in-depth (H2, 2026-05-14): if the dispatcher has shut
        // down between the delay completing and us trying to post back
        // (think: app exiting with a 5-second Success pill still
        // pending), Avalonia's Post can throw InvalidOperationException
        // ("Dispatcher has shut down") or ObjectDisposedException
        // depending on the teardown timing.  Catch + swallow — there's
        // no UI thread left to update, and the controller will be
        // disposed shortly anyway via Dispose().
        try
        {
            Dispatcher.UIThread.Post(ApplyClear);
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                       or ObjectDisposedException)
        {
            _ = ex;
        }

        return;

        // Marshal back to the UI thread.  Dispatcher.UIThread.Post is
        // re-entrant-safe and survives the dispatcher not being initialised
        // (no-ops in that case — relevant for non-Avalonia unit tests).
        void ApplyClear()
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }

            // Another Set/Dismiss could have raced ahead of the dispatcher
            // post; honour it by checking the CTS is still ours (compare
            // the reference-type CTS, not the value-type Token — CA2013).
            if (!ReferenceEquals(_autoClearCts, cts))
            {
                return;
            }

            Text = null;
            Kind = StatusKind.None;
            _autoClearCts?.Dispose();
            _autoClearCts = null;
        }
    }

    /// <summary>
    /// Cancel any pending auto-clear, dispose the owned
    /// <see cref="CancellationTokenSource"/>, and clear the text + kind.
    /// Idempotent — safe to call multiple times.  Called from
    /// <c>MainWindowViewModel.Dispose()</c> as part of the regular
    /// window-close teardown chain.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelPendingAutoClear();
        Text = null;
        Kind = StatusKind.None;
    }
}