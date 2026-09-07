using System;
using System.IO;
using System.Threading;

namespace Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;

/// <summary>
/// Helpers for robust test teardown, specifically targeting Windows
/// file-handle release latency that the CI Windows runner exposes
/// but developer machines hide.
/// </summary>
public static class TestCleanupHelpers
{
    /// <summary>
    /// Delete a directory tree, tolerating Windows file-handle release
    /// latency.  The cleanup retries with exponential backoff after a
    /// forced GC + finaliser pass, and is <b>best-effort</b>: it never
    /// throws when the tree cannot be removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Background — the canonical cleanup pattern
    /// <c>Directory.Delete(path, recursive: true)</c> works fine on
    /// developer Windows machines because:
    /// </para>
    /// <list type="bullet">
    ///   <item>SSD I/O completes within microseconds.</item>
    ///   <item>The GC is under enough pressure to finalise un-disposed
    ///         <see cref="System.IO.FileSystemWatcher"/> instances
    ///         and file streams between tests.</item>
    /// </list>
    /// <para>
    /// On GitHub Actions <c>windows-latest</c> runners (and Azure VMs in
    /// general), neither holds.  <c>Directory.Delete</c> then fails with:
    /// </para>
    /// <code>
    ///   IOException: The process cannot access the file
    ///   'claude-code-settings.json' because it is being used by
    ///   another process.
    /// </code>
    /// <para>
    /// Two distinct writers produce that message, and only the first is
    /// drained by finalisers:
    /// </para>
    /// <list type="bullet">
    ///   <item>The <c>ReadDirectoryChangesW</c> I/O completion-port handle
    ///         behind <c>FileSystemWatcher</c>, which can stay attached to
    ///         the directory for tens of milliseconds after the owning
    ///         object is unreferenced.</item>
    ///   <item><b>A fire-and-forget schema-cache write.</b>
    ///         <c>SchemaRegistry.GetSchemaAsync</c> starts
    ///         <c>SyncDiskWithBundledAsync</c> with a discarded task, so the
    ///         write to <c>claude-code-settings.json</c> under the sandbox's
    ///         schema-cache directory outlives whatever triggered it.  No
    ///         caller holds that task, so no amount of disposing — of the
    ///         view-model or of the registry — can wait for it.  This is the
    ///         writer that turned <c>main</c> red on the Windows runner
    ///         while the same commit passed in the sibling run.</item>
    /// </list>
    /// <para>
    /// The response is layered.  First force the GC to run finalisers
    /// (which drains the watcher handles), then retry the delete with
    /// exponential backoff to outlast an in-flight write: a worst-case
    /// budget of ~6.35 s (50 ms doubling to 3200 ms across eight attempts),
    /// which only elapses on the pathological path.
    /// </para>
    /// <para>
    /// <b>Then give up quietly.</b>  A test's assertions have already run by
    /// the time teardown executes, and the sandbox lives under the OS temp
    /// directory, which CI runners discard wholesale.  Failing an otherwise
    /// green test because a handle outlived it reports the wrong thing: the
    /// code under test is fine, the janitor was merely too slow.  So the
    /// final failure is reported as a warning on stderr — naming the path
    /// and the last error, so a genuine handle leak stays greppable in the
    /// CI log — rather than thrown.  Only <see cref="IOException"/> and
    /// <see cref="UnauthorizedAccessException"/> are absorbed; anything else
    /// still propagates.
    /// </para>
    /// <para>
    /// Use this in <c>[TestCleanup]</c> in place of bare
    /// <c>Directory.Delete(path, recursive: true)</c> whenever the
    /// test constructs a <c>MainWindowViewModel</c>, an SDK client,
    /// a <c>SchemaRegistry</c>, or any other type that may open a
    /// <see cref="FileSystemWatcher"/> or a background writer against the
    /// sandbox directory.
    /// </para>
    /// </remarks>
    /// <param name="path">Absolute directory path to delete.  No-op if the directory does not exist.</param>
    /// <param name="maxAttempts">Maximum number of delete attempts before giving up.  Default 8.</param>
    public static void DeleteDirectoryWithRetry(string path, int maxAttempts = 8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        if (!Directory.Exists(path))
        {
            return;
        }

        // Force a GC + finaliser pass so any un-disposed FileSystemWatcher
        // / FileStream finalisers run and release their underlying handles
        // BEFORE the first delete attempt.  Two Collect calls bracket the
        // WaitForPendingFinalizers because the finalisers themselves can
        // produce new garbage that needs collecting on the second pass.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Exception? lastFailure = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastFailure = ex;

                if (attempt < maxAttempts)
                {
                    // Exponential backoff: 50, 100, 200, 400, 800, 1600, 3200 ms
                    // (cumulative ~6.35 s across the seven waits).
                    Thread.Sleep(50 * (1 << (attempt - 1)));
                }
            }
        }

        // Best-effort by contract — see the remarks above. Loud enough to grep,
        // quiet enough not to fail a test that already passed.
        Console.Error.WriteLine(
            $"[TestCleanup] WARNING: could not delete sandbox '{path}' after {maxAttempts} attempts; " +
            $"leaving it for the OS to reclaim. Last error: " +
            $"{lastFailure?.GetType().Name}: {lastFailure?.Message}");
    }
}
