using Bennewitz.Ninja.ClaudeForge.Diagnostics;

// ⓘ xUnit1031 (no blocking on a task) is suppressed in this file: the concurrency test blocks on
// Task.WaitAll by design, as it did under MSTest. Making it async would rewrite the test, which the
// xUnit move does not do (plans/00006 decision 5).
#pragma warning disable xUnit1031

namespace Bennewitz.Ninja.ClaudeForge.Tests.Diagnostics;

/// <summary>
/// Tests for the channel and enqueue behaviour of <see cref="LiveLogWindow"/>.
/// <para>
/// <see cref="LiveLogWindow.Initialize"/> creates Avalonia controls and requires
/// a running UI thread, so it is intentionally NOT called here — these tests
/// exercise only the thread-safe <see cref="LiveLogWindow.EnqueueLog"/> path,
/// which is safe to call from any thread without Avalonia being initialised.
/// </para>
/// </summary>
public sealed class LiveLogWindowTests
{
    [Fact]
    public void EnqueueLog_DoesNotThrow_WhenCalledBeforeInitialise()
    {
        // Must never throw even when the window hasn't been created yet.
        LiveLogWindow.EnqueueLog("hello before init");
    }

    [Fact]
    public void EnqueueLog_DoesNotThrow_WhenCalledWithEmptyString()
    {
        LiveLogWindow.EnqueueLog(string.Empty);
    }

    [Fact]
    public void EnqueueLog_DoesNotThrow_WhenCalledWithLongMessage()
    {
        string big = new('x', 100_000);
        LiveLogWindow.EnqueueLog(big);
    }

    [Fact]
    public void EnqueueLog_DoesNotThrow_WhenCalledConcurrently()
    {
        // The channel accepts concurrent writers (SingleWriter = false).
        Task[] tasks = Enumerable.Range(0, 50)
                                 .Select(i => Task.Run(() => LiveLogWindow.EnqueueLog($"msg {i}")))
                                 .ToArray();
        Task.WaitAll(tasks);
    }

    [Fact]
    public void EnqueueLog_DropOldest_WhenChannelFull()
    {
        // Flood the channel well past its 5000-item capacity. DropOldest must
        // silently drop entries rather than throwing or blocking.
        for (int i = 0; i < 6_000; i++)
        {
            LiveLogWindow.EnqueueLog($"flood message {i}");
        }
    }
}