using System.ComponentModel;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Status;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Status;

/// <summary>
/// locks <see cref="StatusController"/>'s lifecycle rules.
/// Replaces the prior "bare <c>string? StatusMessage</c> property that
/// never auto-cleared".  The five kinds (None / Active / Success /
/// Warning / Failure / State) have different auto-clear behaviours and
/// the View renders different per-kind icons / colours; these tests pin
/// each contract so a future refactor doesn't quietly regress one.
/// </summary>
/// <remarks>
/// All auto-clear tests use the <see cref="StatusController.DelayOverride"/>
/// test seam so timing is deterministic.  Two patterns:
/// <list type="bullet">
///   <item>Replace the delay with one that completes <em>immediately</em>
///         (used for "auto-clear fires" assertions).</item>
///   <item>Replace the delay with one that hangs on a TaskCompletionSource
///         until the test releases it (used for "auto-clear is cancelled
///         by a subsequent Set" assertions).</item>
/// </list>
/// Each test resets <see cref="StatusController.DelayOverride"/> in
/// TestCleanup to avoid cross-test contamination.
/// </remarks>
[TestClass]
public sealed class StatusControllerTests
{
    [TestCleanup]
    public void Cleanup()
    {
        StatusController.ResetForTesting();
    }

    [TestMethod]
    public void Set_PutsTextAndKindOnTheController()
    {
        StatusController sc = new();
        sc.Set("Saved.", StatusKind.Success);

        Assert.AreEqual("Saved.", sc.Text);
        Assert.AreEqual(StatusKind.Success, sc.Kind);
        Assert.IsTrue(sc.HasText);
        Assert.IsTrue(sc.IsSuccess);
        Assert.IsFalse(sc.IsFailure);
    }

    [TestMethod]
    public void Set_EmptyText_ForcesKindNoneEvenWhenCallerPassedAKind()
    {
        // Empty text always means "nothing on screen" — the kind enum is
        // irrelevant in that case.  Caller passing a non-None kind alongside
        // a null/empty text would otherwise produce a visible coloured pill
        // with no message in it.
        StatusController sc = new();
        sc.Set(null, StatusKind.Failure);

        Assert.AreEqual(StatusKind.None, sc.Kind);
        Assert.IsFalse(sc.HasText);
        Assert.IsFalse(sc.IsFailure);
    }

    [TestMethod]
    public void Dismiss_ClearsTextAndResetsKindToNone()
    {
        StatusController sc = new();
        sc.Set("Save failed: ...", StatusKind.Failure);
        Assert.IsTrue(sc.IsFailure);

        sc.Dismiss();
        Assert.IsNull(sc.Text);
        Assert.AreEqual(StatusKind.None, sc.Kind);
        Assert.IsFalse(sc.IsDismissible);
    }

    [TestMethod]
    public void IsDismissible_OnlyTrueForFailureKind()
    {
        StatusController sc = new();
        // Success / Warning auto-clear instead, so no × button.
        sc.Set("Saved.", StatusKind.Success);
        Assert.IsFalse(sc.IsDismissible);

        sc.Set("Nothing to save", StatusKind.Warning);
        Assert.IsFalse(sc.IsDismissible);

        // Active is operation-tied, gets cleared by the next status emit.
        sc.Set("Reloading…", StatusKind.Active);
        Assert.IsFalse(sc.IsDismissible);

        // State is long-lived identity text.
        sc.Set("Ready", StatusKind.State);
        Assert.IsFalse(sc.IsDismissible);

        // Only Failure surfaces the manual × button.
        sc.Set("Save failed: ...", StatusKind.Failure);
        Assert.IsTrue(sc.IsDismissible);
    }

    [TestMethod]
    public async Task Set_SuccessKind_AutoClearsAfterDelay()
    {
        // Inject a delay function that completes immediately so the
        // auto-clear path fires without us actually sleeping.
        StatusController.DelayOverride = (d, ct) => Task.CompletedTask;

        StatusController sc = new();
        sc.Set("Saved.", StatusKind.Success);

        // The auto-clear is scheduled via Task.Run + UI-thread post;
        // give the dispatcher a tick to drain.
        await WaitForClearAsync(sc);

        Assert.IsNull(sc.Text, "Success status should auto-clear after the delay.");
        Assert.AreEqual(StatusKind.None, sc.Kind);
    }

    [TestMethod]
    public async Task Set_WarningKind_AlsoAutoClears()
    {
        StatusController.DelayOverride = (d, ct) => Task.CompletedTask;

        StatusController sc = new();
        sc.Set("Nothing to save", StatusKind.Warning);

        await WaitForClearAsync(sc);

        Assert.IsNull(sc.Text, "Warning status should auto-clear after the delay.");
        Assert.AreEqual(StatusKind.None, sc.Kind);
    }

    [TestMethod]
    public async Task Set_FailureKind_DoesNotAutoClear()
    {
        // Even with an immediate-completion delay function, a Failure status
        // must NOT auto-clear — the controller doesn't schedule an auto-clear
        // for that kind at all.  Tests that the delay function isn't even
        // wired in for Failure.
        bool delayCalled = false;
        StatusController.DelayOverride = (d, ct) =>
        {
            delayCalled = true;
            return Task.CompletedTask;
        };

        StatusController sc = new();
        sc.Set("Save failed: ...", StatusKind.Failure);

        // Give any wayward task a moment to run.
        await Task.Delay(50);

        Assert.IsFalse(delayCalled,
            "Failure status must not schedule any auto-clear delay.");
        Assert.AreEqual("Save failed: ...", sc.Text);
        Assert.AreEqual(StatusKind.Failure, sc.Kind);
        Assert.IsTrue(sc.IsDismissible);
    }

    [TestMethod]
    public async Task Set_ActiveKind_DoesNotAutoClear()
    {
        // Active statuses are tied to the lifetime of the emitting
        // operation; the caller is responsible for replacing them with
        // a terminal state.  No auto-clear timer should fire.
        bool delayCalled = false;
        StatusController.DelayOverride = (d, ct) =>
        {
            delayCalled = true;
            return Task.CompletedTask;
        };

        StatusController sc = new();
        sc.Set("Reloading…", StatusKind.Active);
        await Task.Delay(50);

        Assert.IsFalse(delayCalled,
            "Active status must not schedule any auto-clear delay.");
        Assert.AreEqual("Reloading…", sc.Text);
        Assert.AreEqual(StatusKind.Active, sc.Kind);
    }

    [TestMethod]
    public async Task Set_StateKind_DoesNotAutoClear()
    {
        bool delayCalled = false;
        StatusController.DelayOverride = (d, ct) =>
        {
            delayCalled = true;
            return Task.CompletedTask;
        };

        StatusController sc = new();
        sc.Set("Ready", StatusKind.State);
        await Task.Delay(50);

        Assert.IsFalse(delayCalled,
            "State status must not schedule any auto-clear delay.");
        Assert.AreEqual("Ready", sc.Text);
    }

    [TestMethod]
    public async Task Set_ReplacingPendingSuccessWithFailure_CancelsAutoClear()
    {
        // Sequence: Set Success (starts timer hanging on TCS) → Set Failure
        // before the timer fires.  The Success timer must NOT race ahead
        // and wipe the now-Failure status when finally released.
        TaskCompletionSource releaseFirstDelay = new();
        int delayCount = 0;
        StatusController.DelayOverride = (d, ct) =>
        {
            delayCount++;
            if (delayCount == 1)
            {
                return releaseFirstDelay.Task.WaitAsync(ct);
            }

            // Second call (which shouldn't happen for Failure) — fail-fast.
            return Task.CompletedTask;
        };

        StatusController sc = new();
        sc.Set("Saved.", StatusKind.Success); // schedule #1
        sc.Set("Save failed: ...", StatusKind.Failure); // cancels #1

        // Release the hung first delay; its post-await body checks the CTS
        // and bails because the controller cancelled it.
        releaseFirstDelay.SetResult();
        await Task.Delay(50);

        Assert.AreEqual("Save failed: ...", sc.Text,
            "The Failure status must survive a late-firing Success auto-clear.");
        Assert.AreEqual(StatusKind.Failure, sc.Kind);
    }

    [TestMethod]
    public async Task Dismiss_CancelsPendingAutoClear()
    {
        // After Dismiss(), the previously-scheduled auto-clear must NOT
        // fire and wipe the next Set() call.
        TaskCompletionSource releaseDelay = new();
        StatusController.DelayOverride = (d, ct) => releaseDelay.Task.WaitAsync(ct);

        StatusController sc = new();
        sc.Set("Saved.", StatusKind.Success); // schedules an auto-clear
        sc.Dismiss(); // cancels it
        sc.Set("New active op…", StatusKind.Active);

        releaseDelay.SetResult();
        await Task.Delay(50);

        Assert.AreEqual("New active op…", sc.Text,
            "Dismiss() must cancel the pending auto-clear so a subsequent Set isn't wiped by it.");
        Assert.AreEqual(StatusKind.Active, sc.Kind);
    }

    /// <summary>
    /// Event-driven wait for the controller's text to drain to null.
    /// Subscribes to <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>
    /// and completes the moment <c>Text</c> transitions to null;
    /// applies a 1 s watchdog via <see cref="Task.WhenAny"/> so a stuck
    /// auto-clear still surfaces as a test failure (not a hang).
    /// </summary>
    /// <remarks>
    /// Replaces the prior 10 ms polling spin which
    /// could flake under loaded CI schedulers.
    /// </remarks>
    private static async Task WaitForClearAsync(StatusController sc,
                                                int maxWaitMs = 1000)
    {
        if (sc.Text is null)
        {
            return; // already clear, nothing to wait for
        }

        TaskCompletionSource tcs = new();

        sc.PropertyChanged += Handler;
        try
        {
            // Re-check after subscribing in case Text drained between the
            // initial guard and the subscription registering.
            if (sc.Text is null)
            {
                tcs.TrySetResult();
            }

            Task watchdog = Task.Delay(maxWaitMs);
            await Task.WhenAny(tcs.Task, watchdog);
        }
        finally
        {
            sc.PropertyChanged -= Handler;
        }

        return;

        void Handler(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StatusController.Text) && sc.Text is null)
            {
                tcs.TrySetResult();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  H2 — Dispose
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Dispose_CancelsPendingAutoClear_AndDoesNotThrow()
    {
        // Schedule a long-pending Success auto-clear (delay hangs until
        // released), then Dispose the controller.  The pending timer
        // must be cancelled cleanly — no unhandled exception, no
        // post-dispose text re-mutation.
        TaskCompletionSource release = new();
        StatusController.DelayOverride = (d, ct) => release.Task.WaitAsync(ct);

        StatusController sc = new();
        sc.Set("Saved.", StatusKind.Success);

        sc.Dispose(); // must not throw

        // Now release the hung delay; the post-await body checks
        // _disposed / CTS state and bails without touching Text/Kind.
        release.SetResult();
        await Task.Delay(50);

        Assert.IsNull(sc.Text,
            "Disposed controller must end up with Text=null (Dispose clears it).");
        Assert.AreEqual(StatusKind.None, sc.Kind);
    }

    [TestMethod]
    public void Dispose_IsIdempotent()
    {
        StatusController sc = new();
        sc.Set("Saved.", StatusKind.Success);

        sc.Dispose();
        sc.Dispose(); // second call must be a no-op, not throw

        Assert.IsNull(sc.Text);
    }

    // ─────────────────────────────────────────────────────────────────
    //  H3 — ResetForTesting
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ResetForTesting_RestoresAllSeamsToDeclaredDefaults()
    {
        // Mutate every internal test seam, then call ResetForTesting()
        // and assert each is back to its declared default.  Locks the
        // contract that future seam additions get a line in the reset
        // method — a missed seam would silently leak across tests,
        // which is exactly the bug H3 fixes.
        StatusController.SuccessAutoClearDelay = TimeSpan.FromMilliseconds(1);
        StatusController.WarningAutoClearDelay = TimeSpan.FromMilliseconds(2);
        StatusController.DelayOverride = (d, ct) => Task.CompletedTask;

        StatusController.ResetForTesting();

        Assert.AreEqual(TimeSpan.FromSeconds(6), StatusController.SuccessAutoClearDelay,
            "SuccessAutoClearDelay must reset to the 6-second production default.");
        Assert.AreEqual(TimeSpan.FromSeconds(10), StatusController.WarningAutoClearDelay,
            "WarningAutoClearDelay must reset to the 10-second production default.");
        Assert.IsNull(StatusController.DelayOverride,
            "DelayOverride must reset to null (production uses Task.Delay).");
    }
}