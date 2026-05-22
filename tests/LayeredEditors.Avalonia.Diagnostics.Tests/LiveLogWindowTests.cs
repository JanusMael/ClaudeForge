using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.UI;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Tests;

/// <summary>
/// Tests for the channel and enqueue behaviour of <see cref="LiveLogWindow"/>.
/// <para>
/// <see cref="LiveLogWindow.Initialize"/> creates Avalonia controls and requires
/// a running UI thread, so it is intentionally NOT called here — these tests
/// exercise only the thread-safe <see cref="LiveLogWindow.EnqueueLog"/> path,
/// which is safe to call from any thread without Avalonia being initialised.
/// </para>
/// </summary>
[TestClass]
public sealed class LiveLogWindowTests
{
    [TestMethod]
    public void EnqueueLog_DoesNotThrow_WhenCalledBeforeInitialise()
    {
        // Must never throw even when the window hasn't been created yet.
        LiveLogWindow.EnqueueLog("hello before init");
    }

    [TestMethod]
    public void EnqueueLog_DoesNotThrow_WhenCalledWithEmptyString()
    {
        LiveLogWindow.EnqueueLog(string.Empty);
    }

    [TestMethod]
    public void EnqueueLog_DoesNotThrow_WhenCalledWithLongMessage()
    {
        string big = new('x', 100_000);
        LiveLogWindow.EnqueueLog(big);
    }

    [TestMethod]
    public void EnqueueLog_DoesNotThrow_WhenCalledConcurrently()
    {
        // The channel accepts concurrent writers (SingleWriter = false).
        Task[] tasks = Enumerable.Range(0, 50)
                                 .Select(i => Task.Run(() => LiveLogWindow.EnqueueLog($"msg {i}")))
                                 .ToArray();
        Task.WaitAll(tasks);
    }

    [TestMethod]
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