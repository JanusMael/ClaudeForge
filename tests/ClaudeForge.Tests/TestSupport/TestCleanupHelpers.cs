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
    /// forced GC + finaliser pass.
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
    /// general), neither holds.  The <c>ReadDirectoryChangesW</c> I/O
    /// completion-port handle behind <c>FileSystemWatcher</c> can stay
    /// attached to the directory for tens of milliseconds after the
    /// owning object is unreferenced.  <c>Directory.Delete</c> then
    /// fails with:
    /// </para>
    /// <code>
    ///   IOException: The process cannot access the file
    ///   'claude-code-settings.json' because it is being used by
    ///   another process.
    /// </code>
    /// <para>
    /// The fix: force the GC to run finalisers (which drains the
    /// watcher handles), then retry the delete with a short backoff
    /// to cover any remaining drain latency.  Total budget on the
    /// worst-case path is ~750 ms (50 + 100 + 200 + 400 ms across
    /// five attempts) which is well under the test's normal duration.
    /// </para>
    /// <para>
    /// Use this in <c>[TestCleanup]</c> in place of bare
    /// <c>Directory.Delete(path, recursive: true)</c> whenever the
    /// test constructs a <c>MainWindowViewModel</c>, an SDK client,
    /// or any other type that may open a <see cref="FileSystemWatcher"/>
    /// against the sandbox directory.
    /// </para>
    /// </remarks>
    /// <param name="path">Absolute directory path to delete.  No-op if the directory does not exist.</param>
    /// <param name="maxAttempts">Maximum number of delete attempts before re-throwing.  Default 5.</param>
    /// <summary>
    /// Delete a single file that a <b>live</b> view-model is watching, tolerating the
    /// background re-read its <c>ConfigFileWatcher</c> may be part-way through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the mid-test sibling of <see cref="DeleteDirectoryWithRetry"/>, and the
    /// difference matters. That one runs in <c>[TestCleanup]</c>, where the view-model is
    /// finished with and forcing a finaliser pass is the whole point. Here the view-model is
    /// deliberately still alive — the test is about how it reacts to the file vanishing — so
    /// a <see cref="GC.Collect()"/> would be both useless and wrong. All that is needed is to
    /// wait out a read that is already in flight.
    /// </para>
    /// <para>
    /// <b>The race.</b> <c>ConfigFileWatcher</c> debounces for 400 ms. A test that writes a
    /// config file, reloads, asserts, and then deletes that file does all of it inside the
    /// debounce window, so the watcher's own reload can be opening the file at the moment the
    /// test deletes it. Windows refuses to delete a file with an open handle; Unix does not,
    /// which is why this shows up on <c>windows-latest</c> and passes on ubuntu and macOS in
    /// the same run.
    /// </para>
    /// <para>
    /// Found by CI, not locally: it failed once, went unreproduced across five later runs
    /// with even the test name lost to output aggregation, and only recurred here. Timing
    /// races on a loaded shared runner do not reproduce on a developer machine on demand.
    /// </para>
    /// </remarks>
    /// <param name="path">Absolute file path to delete. No-op if the file does not exist.</param>
    /// <param name="maxAttempts">Maximum number of delete attempts before re-throwing. Default 5.</param>
    public static void DeleteFileWithRetry(string path, int maxAttempts = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return;
        }

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (Exception ex) when (
                (ex is IOException || ex is UnauthorizedAccessException)
                && attempt < maxAttempts)
            {
                // 50, 100, 200, 400 ms — cumulative 750 ms, which covers the watcher's
                // 400 ms debounce plus the read it then performs.
                Thread.Sleep(50 * (1 << (attempt - 1)));
            }
        }
    }

    public static void DeleteDirectoryWithRetry(string path, int maxAttempts = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

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

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (
                (ex is IOException || ex is UnauthorizedAccessException)
                && attempt < maxAttempts)
            {
                // Exponential backoff: 50, 100, 200, 400 ms (cumulative 750 ms).
                Thread.Sleep(50 * (1 << (attempt - 1)));
            }
        }
    }
}
