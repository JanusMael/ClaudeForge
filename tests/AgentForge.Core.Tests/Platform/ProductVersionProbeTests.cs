using System.Diagnostics;
using System.Runtime.InteropServices;
using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Platform;

/// <summary>
/// Covers the timeout-and-kill behaviour of <see cref="ProductVersionProbe.TryGetClaudeCodeVersionAsync"/>:
/// processes that do not exit before the internal deadline must be killed by the finally
/// block that calls <c>process.Kill(entireProcessTree: true)</c> (the A2 fix), and the method
/// must return <see langword="null"/> without hanging or propagating an unhandled exception.
/// </summary>
public sealed class ProductVersionProbeTests
{
    /// <summary>
    /// Probe deadline used by the slow-process test. Production uses 2 000 ms; the test
    /// injects a far smaller one so the whole test costs two process spawns plus this,
    /// which keeps it an order of magnitude inside any runner's scheduling noise.
    /// </summary>
    private const int TestProbeTimeoutMs = 250;

    /// <summary>
    /// Backstop for the polls below. Never reached in practice — a killed process
    /// disappears within a few milliseconds — so this is sized for a badly starved
    /// CI runner rather than for the expected cost.
    /// </summary>
    private static readonly TimeSpan ExitPollBudget = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Passing a nonexistent binary path must return null promptly — either via a
    /// Win32Exception (process failed to start) or via the timeout path — without
    /// propagating an unhandled exception to the caller.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task TryGetClaudeCodeVersionAsync_NonExistentBinary_ReturnsNullWithoutException()
    {
        Stopwatch sw = Stopwatch.StartNew();
        string? result = await ProductVersionProbe.TryGetClaudeCodeVersionAsync(
            "/this/path/does/not/exist/claude.exe");
        sw.Stop();

        MessageAssert.Null(result,
            "A nonexistent binary must cause the probe to return null.");

        // A missing binary must fail through the synchronous Win32Exception from
        // Process.Start, not by burning the probe's 2 000 ms internal deadline.
        // The bound discriminates between those two paths; it is not a perf budget.
        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"Probe took {sw.ElapsedMilliseconds} ms — should have failed fast.");
    }

    /// <summary>
    /// Pointing the probe at a command that runs for two minutes exercises the
    /// <c>Kill(entireProcessTree: true)</c> path in the finally block: the probe must
    /// abandon the wait at its deadline, kill what it started, and return null.
    /// </summary>
    /// <remarks>
    /// Determinism comes from <see cref="TestProbeTimeoutMs"/>, not from the
    /// <c>[Timeout]</c> below. Two things are asserted independently: that the call
    /// returned at all (the command itself would take ~120 s, so returning proves the
    /// deadline fired) and that the process it started is actually gone (which a kill
    /// failure would not satisfy — the probe logs and swallows that failure, so the
    /// null result alone does not prove the kill happened).
    ///
    /// The command is run through a shell so there is a grandchild process and the
    /// tree-kill code path has a tree to tear down. Going through
    /// <see cref="ProductVersionProbe.TryGetVersionAsync"/> rather than the public
    /// entry point means the exe/args pair can be supplied directly — no temp .bat
    /// wrapper to write, chmod, and delete, and no extra interpreter spawn beyond the
    /// one that makes the tree. <c>ResolveCommand</c>'s own wrapping of .cmd/.bat/.ps1
    /// shims is covered by <see cref="ProductVersionProbeResolveCommandTests"/>.
    ///
    /// The <c>[Timeout]</c> is a hang backstop only: far above the sub-second cost this
    /// test actually has, and far below the ~120 s the command would run for if the
    /// kill silently stopped working.
    /// </remarks>
    [Fact(Timeout = 30000)]
    public async Task TryGetVersionAsync_SlowProcess_KilledAfterTimeoutReturnsNull()
    {
        ProductVersionProbe.ResolvedCommand slow =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new ProductVersionProbe.ResolvedCommand("cmd.exe", "/c ping -n 120 127.0.0.1")
                : new ProductVersionProbe.ResolvedCommand("/bin/sh", "-c \"sleep 120\"");

        int startedPid = 0;
        Stopwatch sw = Stopwatch.StartNew();
        string? result = await ProductVersionProbe.TryGetVersionAsync(
            slow, TestProbeTimeoutMs, pid => startedPid = pid);
        sw.Stop();

        MessageAssert.Null(result,
            "Slow process must be killed after the internal timeout and the method must return null.");
        MessageAssert.NotEqual(0, startedPid,
            "The probe should have started a process — the test seam never fired.");

        // ~120 s if the deadline never fired; a small multiple of TestProbeTimeoutMs if it did.
        Assert.True(sw.ElapsedMilliseconds < 10000,
            $"Probe took {sw.ElapsedMilliseconds} ms — the {TestProbeTimeoutMs} ms deadline did not fire.");

        Assert.True(WaitForProcessExit(startedPid, ExitPollBudget),
            $"Process {startedPid} was still running after the probe returned — "
            + "the Kill(entireProcessTree: true) in the finally block did not take effect.");
    }

    /// <summary>
    /// Polls until the given process is gone, or the budget runs out. Kill is asynchronous
    /// — TerminateProcess returns before the process is reaped — so a single immediate check
    /// would be racy in exactly the way this test exists to remove.
    /// </summary>
    private static bool WaitForProcessExit(int pid, TimeSpan budget)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                // Throws ArgumentException once the PID is no longer live. PID reuse inside
                // this window would need the OS to recycle the id within milliseconds.
                using Process p = Process.GetProcessById(pid);
                if (p.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }

            if (sw.Elapsed >= budget)
            {
                return false;
            }

            Thread.Sleep(25);
        }
    }
}
