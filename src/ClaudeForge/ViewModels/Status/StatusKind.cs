namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Status;

/// <summary>
/// Categorises a centre-status-bar message by lifecycle + severity so the
/// View can render it with the right icon / colour and so
/// <see cref="StatusController"/> can decide whether and when to auto-clear
/// it.  Added 2026-05-14 alongside the status-bar rework — see
/// <c>CLAUDE.md</c> "Status bar".
/// </summary>
public enum StatusKind
{
    /// <summary>
    /// Initial / cleared state.  The View renders nothing in the centre slot.
    /// </summary>
    None = 0,

    /// <summary>
    /// In-flight verb tied to a long-running operation (Loading…, Reloading…,
    /// Opening project…).  Sticks until the next <c>SetStatus</c> call
    /// overwrites it — typically when the operation reports its terminal
    /// state.  Rendered in the accent colour with no icon (the right-side
    /// progress bar already signals "something is happening").
    /// </summary>
    Active = 1,

    /// <summary>
    /// Terminal positive outcome (Saved., Reloaded., Exported to …,
    /// Profile created…).  Auto-clears after
    /// <see cref="StatusController.SuccessAutoClearDelay"/> so the bar
    /// doesn't hold stale confirmations.  Rendered in green with a ✓ icon.
    /// </summary>
    Success = 2,

    /// <summary>
    /// Informational notice that isn't a hard failure but is worth pausing
    /// on (Nothing to save / Nothing to export — caller had nothing to act
    /// on).  Auto-clears after
    /// <see cref="StatusController.WarningAutoClearDelay"/> — longer than
    /// Success because it usually wants slightly more attention.  Rendered
    /// in amber with a ⚠ icon.
    /// </summary>
    Warning = 3,

    /// <summary>
    /// Terminal negative outcome the user MUST see (Save failed, Reload
    /// failed, Schema validation blocked the save).  Does NOT auto-clear —
    /// the View shows a close (×) button that calls
    /// <c>MainWindowViewModel.DismissStatusCommand</c>.  Rendered in red
    /// with a ✗ icon.
    /// </summary>
    Failure = 4,

    /// <summary>
    /// Long-lived identity-level text (Ready, "Project: foo").  Does NOT
    /// auto-clear — it survives until a different status replaces it.
    /// Rendered in the muted secondary-text colour with no icon, the same
    /// way the original status bar looked, so this category covers the
    /// "settled / quiet" state.
    /// </summary>
    State = 5,
}