using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Status;
using Microsoft.Extensions.Time.Testing;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Status;

/// <summary>
/// Locks <see cref="StatusController"/>'s lifecycle rules.  The five kinds
/// (Active / Success / Warning / Failure / State) have different auto-clear
/// behaviours and the View renders different per-kind icons / colours; these
/// tests pin each contract so a future refactor doesn't quietly regress one.
/// </summary>
/// <remarks>
/// <para>
/// Every test drives a <see cref="FakeTimeProvider"/> the test advances by
/// hand, and passes <c>action =&gt; action()</c> as the controller's dispatch so
/// the clear runs inline.  Nothing sleeps, nothing polls, no dispatcher is
/// pumped, and no static seam has to be reset between tests — the clock and
/// the dispatch are per instance.
/// </para>
/// <para>
/// This replaces the prior <c>DelayOverride</c> static seam, which could only
/// make a delay complete immediately or hang on a
/// <c>TaskCompletionSource</c>; because it could not distinguish
/// <em>which</em> delay had been asked for, no test could assert that a
/// warning stays longer than a success.  <see cref="SetWarning_StaysLongerThanASuccess"/>
/// is that assertion.
/// </para>
/// </remarks>
public sealed class StatusControllerTests
{
    private static StatusController NewController(FakeTimeProvider time) => new(time, action => action());

    [Fact]
    public void SetSuccess_PutsTextAndKindOnTheController()
    {
        FakeTimeProvider time = new();
        using StatusController sc = NewController(time);

        sc.SetSuccess("Saved.");

        Assert.Equal("Saved.", sc.Text);
        Assert.Equal(StatusKind.Success, sc.Kind);
        Assert.True(sc.HasText);
        Assert.True(sc.IsSuccess);
        Assert.False(sc.IsFailure);
    }

    [Fact]
    public void SetAnything_WithEmptyText_ForcesKindNone()
    {
        // Empty text always means "nothing on screen" — the kind is irrelevant in
        // that case.  A caller passing a non-None kind alongside a null/empty text
        // would otherwise produce a visible coloured pill with no message in it.
        FakeTimeProvider time = new();
        using StatusController sc = NewController(time);

        sc.SetFailure(null);

        Assert.Equal(StatusKind.None, sc.Kind);
        Assert.False(sc.HasText);
        Assert.False(sc.IsFailure);
    }

    [Fact]
    public void Dismiss_ClearsTextAndResetsKindToNone()
    {
        FakeTimeProvider time = new();
        using StatusController sc = NewController(time);

        sc.SetFailure("Save failed: ...");
        Assert.True(sc.IsFailure);

        sc.Dismiss();

        Assert.Null(sc.Text);
        Assert.Equal(StatusKind.None, sc.Kind);
        Assert.False(sc.IsDismissible);
    }

    [Fact]
    public void IsDismissible_OnlyTrueForFailureKind()
    {
        FakeTimeProvider time = new();
        using StatusController sc = NewController(time);

        // Success / Warning auto-clear instead, so no × button.
        sc.SetSuccess("Saved.");
        Assert.False(sc.IsDismissible);

        sc.SetWarning("Nothing to save");
        Assert.False(sc.IsDismissible);

        // Active is operation-tied, gets cleared by the next status emit.
        sc.SetActive("Reloading…");
        Assert.False(sc.IsDismissible);

        // State is long-lived identity text.
        sc.SetState("Ready");
        Assert.False(sc.IsDismissible);

        // Only Failure surfaces the manual × button.
        sc.SetFailure("Save failed: ...");
        Assert.True(sc.IsDismissible);
    }

    [Fact]
    public void SetSuccess_AutoClearsAfterItsDelay()
    {
        FakeTimeProvider time = new();
        using StatusController sc = NewController(time);

        sc.SetSuccess("Saved.");

        time.Advance(StatusController.DefaultSuccessAutoClearDelay - TimeSpan.FromMilliseconds(1));
        MessageAssert.Equal("Saved.", sc.Text, "The success must still be on screen a millisecond early.");

        time.Advance(TimeSpan.FromMilliseconds(2));
        MessageAssert.Null(sc.Text, "Success status should auto-clear after its delay.");
        Assert.Equal(StatusKind.None, sc.Kind);
    }

    [Fact]
    public void SetWarning_StaysLongerThanASuccess()
    {
        // The delay a warning gets is its own, and longer. The previous static
        // DelayOverride seam could not express this: it replaced every delay with
        // the same immediately-completing task, so the two were indistinguishable.
        FakeTimeProvider time = new();
        using StatusController sc = NewController(time);

        sc.SetWarning("Nothing to save");

        time.Advance(StatusController.DefaultSuccessAutoClearDelay);
        MessageAssert.Equal("Nothing to save", sc.Text,
            "A warning must outlast the success delay — it has its own, longer one.");

        time.Advance(StatusController.DefaultWarningAutoClearDelay
                     - StatusController.DefaultSuccessAutoClearDelay
                     + TimeSpan.FromMilliseconds(1));
        MessageAssert.Null(sc.Text, "Warning status should auto-clear after its own delay.");
        Assert.Equal(StatusKind.None, sc.Kind);
    }

    [Fact]
    public void SetFailure_DoesNotAutoClear()
    {
        FakeTimeProvider time = new();
        using StatusController sc = NewController(time);

        sc.SetFailure("Save failed: ...");
        time.Advance(TimeSpan.FromHours(1));

        Assert.Equal("Save failed: ...", sc.Text);
        Assert.Equal(StatusKind.Failure, sc.Kind);
        Assert.True(sc.IsDismissible);
    }

    [Fact]
    public void SetActive_DoesNotAutoClear()
    {
        // Active statuses are tied to the lifetime of the emitting operation; the
        // caller is responsible for replacing them with a terminal state.
        FakeTimeProvider time = new();
        using StatusController sc = NewController(time);

        sc.SetActive("Reloading…");
        time.Advance(TimeSpan.FromHours(1));

        Assert.Equal("Reloading…", sc.Text);
        Assert.Equal(StatusKind.Active, sc.Kind);
    }

    [Fact]
    public void SetState_DoesNotAutoClear()
    {
        FakeTimeProvider time = new();
        using StatusController sc = NewController(time);

        sc.SetState("Ready");
        time.Advance(TimeSpan.FromHours(1));

        Assert.Equal("Ready", sc.Text);
        Assert.Equal(StatusKind.State, sc.Kind);
    }

    [Fact]
    public void ReplacingAPendingSuccessWithAFailure_CancelsTheAutoClear()
    {
        // Sequence: SetSuccess (schedules a clear) → SetFailure before it comes
        // due.  The success's clear must not wipe the now-Failure status.
        FakeTimeProvider time = new();
        using StatusController sc = NewController(time);

        sc.SetSuccess("Saved.");
        sc.SetFailure("Save failed: ...");

        time.Advance(TimeSpan.FromHours(1));

        MessageAssert.Equal("Save failed: ...", sc.Text,
            "The failure must survive the success's cancelled auto-clear.");
        Assert.Equal(StatusKind.Failure, sc.Kind);
    }

    [Fact]
    public void Dismiss_CancelsThePendingAutoClear()
    {
        // After Dismiss(), the previously-scheduled auto-clear must not fire and
        // wipe the next Set* call.
        FakeTimeProvider time = new();
        using StatusController sc = NewController(time);

        sc.SetSuccess("Saved.");
        sc.Dismiss();
        sc.SetActive("New active op…");

        time.Advance(TimeSpan.FromHours(1));

        MessageAssert.Equal("New active op…", sc.Text,
            "Dismiss() must cancel the pending auto-clear so a subsequent emit isn't wiped by it.");
        Assert.Equal(StatusKind.Active, sc.Kind);
    }

    [Fact]
    public void AClearThatCameDueBeforeTheNextMessage_DoesNotClearIt()
    {
        // Production marshals the clear with Dispatcher.UIThread.Post, so a timer
        // can come due and queue its clear moments before the UI thread emits the
        // next message.  That in-flight clear belongs to the message that
        // scheduled it and must not take the new one with it.
        FakeTimeProvider time = new();
        Queue<Action> posted = new();
        using StatusController sc = new(time, posted.Enqueue);

        sc.SetSuccess("first");

        time.Advance(StatusController.DefaultSuccessAutoClearDelay);
        MessageAssert.Equal(1, posted.Count, "The due timer should have queued exactly one clear.");
        MessageAssert.Equal("first", sc.Text, "Nothing is cleared until the post is drained.");

        sc.SetWarning("second");

        while (posted.Count > 0)
        {
            posted.Dequeue()();
        }

        MessageAssert.Equal("second", sc.Text, "The in-flight clear belonged to the first message.");
        Assert.Equal(StatusKind.Warning, sc.Kind);
    }

    [Fact]
    public void Dispose_CancelsThePendingAutoClear_AndDoesNotThrow()
    {
        FakeTimeProvider time = new();
        StatusController sc = NewController(time);
        sc.SetSuccess("Saved.");

        sc.Dispose();
        time.Advance(TimeSpan.FromHours(1));

        MessageAssert.Null(sc.Text, "Disposed controller must end up with Text=null (Dispose clears it).");
        Assert.Equal(StatusKind.None, sc.Kind);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        FakeTimeProvider time = new();
        StatusController sc = NewController(time);
        sc.SetSuccess("Saved.");

        sc.Dispose();
        sc.Dispose();

        Assert.Null(sc.Text);
    }
}
