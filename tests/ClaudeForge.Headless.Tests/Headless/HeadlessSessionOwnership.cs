using Avalonia.Headless;
using Avalonia.Threading;
using Bennewitz.Ninja.ClaudeForge.Tests.Headless;

[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
[assembly: AssemblyFixture(typeof(HeadlessSessionOwnership))]

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// Makes the headless session's thread the owner of <see cref="Dispatcher.UIThread"/> before any
/// test runs, and fails the whole assembly if something else got there first.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>Why this exists: Avalonia gives <see cref="Dispatcher.UIThread"/> to whichever thread reads
/// it first.</b> In Avalonia 12.1.6 the <c>Dispatcher</c> constructor does
/// <c>s_uiThread ??= this</c>, and reading <c>Dispatcher.UIThread</c> while that field is null
/// constructs a dispatcher on the reading thread. Under the default <c>PerTest</c> isolation,
/// <c>HeadlessUnitTestSession</c> clears the field at the start of every dispatch
/// (<c>Dispatcher.ResetBeforeUnitTests</c>) and rebuilds the app. The headless platform first reads
/// <c>UIThread</c> only when it builds the <c>Compositor</c>, and
/// <c>DefaultRenderLoop.Add</c> then calls <c>VerifyAccess</c>. A background thread that reads
/// <c>Dispatcher.UIThread</c> in that window claims the UI thread, and that test fails with "the
/// calling thread cannot access this object". App code does that read from pool threads:
/// <c>StatusController.DispatchToUiThread</c>, <c>MainWindowViewModel</c>'s <c>CheckAccess</c>
/// and <c>InvokeAsync</c> after a delay, and file-watcher subscribers that <c>Post</c>. A late
/// continuation from an earlier test is enough.
/// </para>
/// <para>
/// ⭐ <b>Measured with a standalone repro</b> (one background thread calling
/// <c>Dispatcher.UIThread.CheckAccess()</c> in a loop, 2,000 empty dispatches): <c>PerTest</c>
/// failed 1,815 with the exact CI stack; <c>PerAssembly</c> with this warm-up failed 0.
/// </para>
/// <para>
/// ⛔ <b><c>PerAssembly</c> WITHOUT this warm-up is worse, not better.</b> It never resets the
/// dispatcher, so if anything reads <c>Dispatcher.UIThread</c> before the first dispatch, the
/// wrong thread owns it for the rest of the process: 2,000 of 2,000 failed in the same repro. Under
/// <c>PerTest</c> the next reset would have cleared that. This fixture runs before every test in
/// the assembly, so the session thread reads it first.
/// </para>
/// <para>
/// ⚠ <b>The consequence of <c>PerAssembly</c>:</b> every headless test shares one
/// <see cref="Avalonia.Application"/>, and Avalonia disposes nothing between tests. A test that
/// changes application state, such as merged dictionaries, <c>RequestedThemeVariant</c> or open
/// windows, must restore it in a <c>finally</c>, as <see cref="ChangeKindThemedLookupTests"/> does.
/// </para>
/// </remarks>
public sealed class HeadlessSessionOwnership : IAsyncLifetime
{
    /// <summary>The thread the headless session dispatches on.</summary>
    public Thread SessionThread { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        HeadlessUnitTestSession session =
            HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessSessionOwnership).Assembly);

        SessionThread = await session.Dispatch(() => Thread.CurrentThread, CancellationToken.None);

        Thread owner = UiThreadOwnerSeenFromAnotherThread();
        if (owner != SessionThread)
        {
            throw new InvalidOperationException(
                $"Dispatcher.UIThread belongs to thread {owner.ManagedThreadId} ('{owner.Name}'), not to "
                + $"the headless session's thread {SessionThread.ManagedThreadId}. Either this assembly "
                + "lost [assembly: AvaloniaTestIsolation(PerAssembly)], so every dispatch resets the UI "
                + "dispatcher, or something read Dispatcher.UIThread before the session's first "
                + "dispatch, which under PerAssembly cannot be undone. Either way headless tests would "
                + "fail in the compositor's DefaultRenderLoop.Add.");
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// The owner of <see cref="Dispatcher.UIThread"/>, read on a NEW thread that is neither the
    /// session's nor the caller's.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>Do not read it right after <c>await session.Dispatch(…)</c>.</b> The session completes its
    /// task on the session thread, so the continuation runs THERE too. A read placed after the
    /// await is still on the session thread and returns the session's own dispatcher whatever the
    /// isolation level. That makes the check true by construction, the same flaw as PR #74's guard.
    /// Measured: with that read, removing <c>PerAssembly</c> left this check and
    /// <c>TheSessionThread_StillOwnsTheUiDispatcher_BetweenDispatches</c> green.
    /// </remarks>
    public static Thread UiThreadOwnerSeenFromAnotherThread()
    {
        Thread? owner = null;
        Thread reader = new(() => owner = Dispatcher.UIThread.Thread) { IsBackground = true, Name = "UIThread owner probe" };
        reader.Start();
        reader.Join();
        return owner!;
    }
}
