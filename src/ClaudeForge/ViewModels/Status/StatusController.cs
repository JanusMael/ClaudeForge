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
/// surface; <c>MainWindowViewModel</c> hangs one of these off itself
/// (<c>Status</c> property) and routes every status update through one of the
/// typed <c>Set*</c> methods.
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
///         the next <c>Set*</c> call also overrides.</item>
///   <item><see cref="StatusKind.Active"/> sticks until the next
///         <c>Set*</c> call.  Tied to the lifecycle of the operation
///         that emitted it; the caller is responsible for emitting a
///         terminal state when the operation finishes.</item>
///   <item><see cref="StatusKind.State"/> sticks until replaced — long-
///         lived identity text like "Ready" or "Project: foo".</item>
/// </list>
/// <para>
/// <strong>Emitting is typed.</strong>  There is no public method that takes a
/// <see cref="StatusKind"/> as a parameter: <see cref="SetActive"/>,
/// <see cref="SetSuccess"/>, <see cref="SetWarning"/>, <see cref="SetFailure"/>
/// and <see cref="SetState"/> are the whole surface, so the severity is
/// explicit at every callsite and cannot be passed wrongly.  The failure mode
/// this prevents is a failure rendering as quiet text.
/// </para>
/// <para>
/// <strong>Timing.</strong>  Auto-clear runs on an injected
/// <see cref="TimeProvider"/> rather than <c>Task.Delay</c> behind a static
/// test seam, so tests advance a fake clock instead of sleeping.  The
/// parameterless constructor uses <see cref="TimeProvider.System"/>.
/// </para>
/// <para>
/// <strong>Threading:</strong> the clear is marshalled through the injected
/// dispatch callback, which by default runs inline when already on Avalonia's
/// UI thread and posts to <see cref="Dispatcher.UIThread"/> otherwise — so
/// property mutations always land on the UI thread, and a test can supply its
/// own dispatch instead of pumping a dispatcher.
/// </para>
/// </remarks>
public sealed partial class StatusController : ObservableObject, IDisposable
{
    /// <summary>How long a <see cref="StatusKind.Success"/> message stays by default.</summary>
    public static readonly TimeSpan DefaultSuccessAutoClearDelay = TimeSpan.FromSeconds(6);

    /// <summary>How long a <see cref="StatusKind.Warning"/> message stays by default.</summary>
    public static readonly TimeSpan DefaultWarningAutoClearDelay = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _timeProvider;
    private readonly Action<Action> _dispatch;
    private ITimer? _pendingClear;
    private object? _pendingClearToken;
    private bool _disposed;

    /// <summary>Production constructor: the system clock and the UI-thread dispatch.</summary>
    public StatusController()
        : this(TimeProvider.System)
    {
    }

    /// <param name="timeProvider">The clock the auto-clear delays run on.</param>
    /// <param name="dispatch">
    /// Marshals the clear to the UI thread.  The default runs inline when already on
    /// Avalonia's UI thread and posts to it otherwise.
    /// </param>
    public StatusController(TimeProvider timeProvider, Action<Action>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        _dispatch = dispatch ?? DispatchToUiThread;
    }

    /// <summary>
    /// How long a <see cref="StatusKind.Success"/> message stays on screen
    /// before auto-clear.  Per instance, so nothing leaks between tests.
    /// </summary>
    public TimeSpan SuccessAutoClearDelay { get; set; } = DefaultSuccessAutoClearDelay;

    /// <summary>
    /// How long a <see cref="StatusKind.Warning"/> message stays on screen
    /// before auto-clear.  Longer than <see cref="SuccessAutoClearDelay"/>
    /// because warnings ("nothing to save") deserve more dwell time.
    /// </summary>
    public TimeSpan WarningAutoClearDelay { get; set; } = DefaultWarningAutoClearDelay;

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

    /// <summary>An operation in flight; sticks until the next message.</summary>
    public void SetActive(string? text) => Set(text, StatusKind.Active);

    /// <summary>A confirmation; clears itself after <see cref="SuccessAutoClearDelay"/>.</summary>
    public void SetSuccess(string? text) => Set(text, StatusKind.Success);

    /// <summary>A notice; clears itself after <see cref="WarningAutoClearDelay"/>.</summary>
    public void SetWarning(string? text) => Set(text, StatusKind.Warning);

    /// <summary>A failure; sticks until dismissed or replaced.</summary>
    public void SetFailure(string? text) => Set(text, StatusKind.Failure);

    /// <summary>Quiet identity text; sticks until replaced.</summary>
    public void SetState(string? text) => Set(text, StatusKind.State);

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

    /// <summary>
    /// Cancel any pending auto-clear, dispose the owned timer, and clear the
    /// text + kind.  Idempotent — safe to call multiple times.  Called from
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

    /// <summary>
    /// Replace the current status.  Cancels any pending auto-clear from the
    /// previous message and schedules a fresh one when the new kind is
    /// <see cref="StatusKind.Success"/> or <see cref="StatusKind.Warning"/>.
    /// </summary>
    /// <remarks>
    /// Private: the typed <c>Set*</c> methods above are the emitting surface, so
    /// no callsite can pass a kind that does not match what it is reporting.
    /// A null or empty <paramref name="text"/> clears the bar whatever the kind.
    /// </remarks>
    private void Set(string? text, StatusKind kind)
    {
        CancelPendingAutoClear();
        Text = text;
        Kind = string.IsNullOrEmpty(text) ? StatusKind.None : kind;

        TimeSpan? autoClearAfter = Kind switch
        {
            StatusKind.Success => SuccessAutoClearDelay,
            StatusKind.Warning => WarningAutoClearDelay,
            var _ => (TimeSpan?)null,
        };
        if (autoClearAfter is { } delay)
        {
            // The token identifies the message this clear belongs to.  It is created
            // before the timer because the callback needs something to compare
            // against, and the timer cannot be its own state.
            object token = new();
            _pendingClearToken = token;
            _pendingClear = _timeProvider.CreateTimer(OnClearDue, token, delay, Timeout.InfiniteTimeSpan);
        }
    }

    private static void DispatchToUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        // Defence-in-depth: if the dispatcher has shut down between the timer
        // coming due and us trying to post back (think: app exiting with a
        // 6-second Success pill still pending), Avalonia's Post can throw
        // InvalidOperationException ("Dispatcher has shut down") or
        // ObjectDisposedException depending on the teardown timing.  Catch +
        // swallow — there's no UI thread left to update, and the controller
        // will be disposed shortly anyway via Dispose().
        try
        {
            Dispatcher.UIThread.Post(action);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            _ = ex;
        }
    }

    private void OnClearDue(object? state)
    {
        _dispatch(() =>
        {
            // The clear belongs to the message that scheduled it.  A timer that came
            // due before this dispatch ran has already been replaced by a later
            // message, and clearing then would take that message with it — so the
            // token, not merely "is one pending", is what decides.
            if (_disposed || !ReferenceEquals(_pendingClearToken, state))
            {
                return;
            }

            CancelPendingAutoClear();
            Text = null;
            Kind = StatusKind.None;
        });
    }

    private void CancelPendingAutoClear()
    {
        _pendingClear?.Dispose();
        _pendingClear = null;
        _pendingClearToken = null;
    }
}
