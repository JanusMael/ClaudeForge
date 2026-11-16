using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// Guards <see cref="HeadlessSessionOwnership"/>: the headless session's thread owns
/// <see cref="Dispatcher.UIThread"/>, and background reads of it cannot take that away.
/// </summary>
/// <remarks>
/// ⚠ <b>Both assertions read from a thread that is NOT the session's, on purpose.</b> PR #74's
/// guard read <c>Application.Current</c> inside a dispatch, where the session had just built the
/// app, so it could not fail. "After the await" is not good enough either: the dispatch completes
/// on the session thread and the continuation runs there. See
/// <see cref="HeadlessSessionOwnership.UiThreadOwnerSeenFromAnotherThread"/>. Under <c>PerTest</c>
/// isolation both fail: each dispatch ends by clearing <c>UIThread</c>, so the probe thread's read
/// creates a dispatcher of its own, and the stress test fails most of its dispatches. Canaried by
/// removing the <c>AvaloniaTestIsolation</c> attribute.
/// </remarks>
public sealed class HeadlessSessionOwnershipTests(HeadlessSessionOwnership ownership)
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    [Fact]
    public async Task TheSessionThread_StillOwnsTheUiDispatcher_BetweenDispatches()
    {
        Thread dispatchedOn = await Session.Dispatch(() => Thread.CurrentThread, CancellationToken.None);

        Assert.Same(ownership.SessionThread, dispatchedOn);
        Thread owner = HeadlessSessionOwnership.UiThreadOwnerSeenFromAnotherThread();
        Assert.True(owner == dispatchedOn,
            $"Dispatcher.UIThread belongs to thread {owner.ManagedThreadId} ('{owner.Name}'), read "
            + $"from a probe thread, but the session dispatches on {dispatchedOn.ManagedThreadId}. "
            + "Under PerTest isolation each dispatch clears the UI dispatcher, and the next thread to "
            + "read it takes it. Check that [assembly: AvaloniaTestIsolation(PerAssembly)] is still set.");
    }

    [Fact]
    public async Task BackgroundReadsOfUIThread_CannotTakeTheDispatcher_FromTheSession()
    {
        using CancellationTokenSource stop = new();
        Thread noise = new(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                _ = Dispatcher.UIThread.CheckAccess();
            }
        })
        { IsBackground = true, Name = "UIThread reader" };
        noise.Start();

        int failed = 0;
        string? first = null;
        try
        {
            for (int i = 0; i < 200; i++)
            {
                try
                {
                    await Session.Dispatch(() => Application.Current is not null, CancellationToken.None);
                }
                catch (InvalidOperationException ex)
                {
                    failed++;
                    first ??= ex.Message;
                }
            }
        }
        finally
        {
            await stop.CancelAsync();
            noise.Join();
        }

        Assert.True(failed == 0,
            $"{failed} of 200 dispatches failed while another thread read Dispatcher.UIThread; first: "
            + $"{first}. Under PerTest isolation the reader claims the UI thread during app set-up.");
    }
}
