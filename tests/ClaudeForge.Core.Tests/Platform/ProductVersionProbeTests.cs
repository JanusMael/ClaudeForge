using System.Diagnostics;
using System.Runtime.InteropServices;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Platform;

/// <summary>
/// Covers the timeout-and-kill behaviour of <see cref="ProductVersionProbe.TryGetClaudeCodeVersionAsync"/>:
/// processes that do not exit before the 2 s internal deadline must be killed by the finally
/// block that calls <c>process.Kill(entireProcessTree: true)</c> (the A2 fix), and the method
/// must return <see langword="null"/> without hanging or propagating an unhandled exception.
/// </summary>
[TestClass]
public sealed class ProductVersionProbeTests
{
    /// <summary>
    /// Passing a nonexistent binary path must return null promptly — either via a
    /// Win32Exception (process failed to start) or via the timeout path — without
    /// propagating an unhandled exception to the caller.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task TryGetClaudeCodeVersionAsync_NonExistentBinary_ReturnsNullWithoutException()
    {
        Stopwatch sw = Stopwatch.StartNew();
        string? result = await ProductVersionProbe.TryGetClaudeCodeVersionAsync(
            "/this/path/does/not/exist/claude.exe");
        sw.Stop();

        Assert.IsNull(result,
            "A nonexistent binary must cause the probe to return null.");

        // Should not take anywhere near the 5 s test timeout — a missing binary
        // triggers a Win32Exception immediately during Process.Start.
        Assert.IsTrue(sw.ElapsedMilliseconds < 4000,
            $"Probe took {sw.ElapsedMilliseconds} ms — should have failed fast.");
    }

    /// <summary>
    /// Pointing the probe at a long-running process (ping on Windows, sleep on Unix)
    /// exercises the Kill(entireProcessTree:true) path in the finally block.
    /// The probe's internal timeout is 2 000 ms; the test grants 5 s so there is a
    /// comfortable margin even on slow CI machines.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task TryGetClaudeCodeVersionAsync_SlowProcess_KilledAfterTimeoutReturnsNull()
    {
        // Choose a binary that is guaranteed to exist and will run for well beyond the
        // 2 000 ms probe timeout without writing a recognisable version string.
        string slowBinary;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // ping -n 30 loops for ~30 s — far longer than the 2 s probe timeout.
            // We can't pass additional arguments through the probe API, so we use a
            // batch wrapper written to a temp file that calls ping internally.
            string batchPath = Path.Combine(Path.GetTempPath(), $"probe-slow-{Guid.NewGuid():N}.bat");
            await File.WriteAllTextAsync(batchPath, "@ping -n 30 127.0.0.1 > nul\r\n");
            slowBinary = batchPath;
            try
            {
                string? result = await ProductVersionProbe.TryGetClaudeCodeVersionAsync(slowBinary);
                Assert.IsNull(result,
                    "Slow process must be killed after the internal timeout and the method must return null.");
            }
            finally
            {
                try
                {
                    File.Delete(batchPath);
                }
                catch
                {
                    /* best-effort cleanup */
                }
            }
        }
        else
        {
            // On Unix, /bin/sleep is universally available.
            // We pass the path directly; the args "--version" won't affect sleep's
            // behaviour — it still blocks until killed.
            slowBinary = "/bin/sleep";
            string? result = await ProductVersionProbe.TryGetClaudeCodeVersionAsync(slowBinary);
            Assert.IsNull(result,
                "Slow process must be killed after the internal timeout and the method must return null.");
        }
    }
}