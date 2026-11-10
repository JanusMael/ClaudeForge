using Bennewitz.Ninja.AgentForge.Core.FileIO;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.FileIO;

/// <summary>
/// <see cref="ConfigFileWatcher"/> refuses, at construction, a debounce that
/// <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> would refuse later.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists</b> (plans/00010 decision 11, found by BNCQ1009). The debounce used to be
/// accepted as given and first used inside a <see cref="FileSystemWatcher"/> callback, on a thread
/// no caller catches, when a watched file changed. A negative debounce therefore built cleanly and
/// then ended the process on the first external edit, far from the line that caused it.
/// </para>
/// </remarks>
public sealed class ConfigFileWatcherDebounceTests
{
    [Fact]
    public void ANegativeDebounce_IsRefusedAtConstruction()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ConfigFileWatcher(TimeSpan.FromMilliseconds(-1)));

        MessageAssert.Equal("debounce", ex.ParamName,
            "The refusal names the debounce, so the caller sees which argument was wrong.");
    }

    [Fact]
    public void ADebounceLongerThanTaskDelayAccepts_IsRefusedAtConstruction()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ConfigFileWatcher(ConfigFileWatcher.MaxDebounce + TimeSpan.FromMilliseconds(1)));

        MessageAssert.Equal("debounce", ex.ParamName, "The refusal names the debounce.");
    }

    [Fact]
    public void TheBoundaryValues_AreAccepted()
    {
        // Zero means "the default", and MaxDebounce is the longest Task.Delay takes: both must
        // still build, or the guard is refusing values the watcher can use.
        using ConfigFileWatcher zero = new(TimeSpan.Zero);
        using ConfigFileWatcher longest = new(ConfigFileWatcher.MaxDebounce);
        using ConfigFileWatcher unset = new();
    }

    [Fact]
    public void MaxDebounce_IsWhatTaskDelayActuallyAccepts()
    {
        // Pins the constant to the runtime's rule rather than to a number copied from its docs:
        // one millisecond more is what Task.Delay itself refuses.
        using CancellationTokenSource cts = new();
        cts.Cancel();
        Task accepted = Task.Delay(ConfigFileWatcher.MaxDebounce, TimeProvider.System, cts.Token);
        MessageAssert.Equal(true, accepted.IsCanceled, "Task.Delay accepts MaxDebounce.");

        // A statement lambda: Task.Delay validates synchronously, and this asserts exactly that.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => { _ = Task.Delay(ConfigFileWatcher.MaxDebounce + TimeSpan.FromMilliseconds(1), TimeProvider.System, cts.Token); });
    }
}
