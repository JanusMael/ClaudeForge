using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;

namespace Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;

/// <summary>
/// Proves <see cref="TestCleanupHelpers.DeleteFileWithRetry"/> actually retries.
/// </summary>
/// <remarks>
/// The flake it was written for is a timing race against <c>ConfigFileWatcher</c>'s 400 ms
/// debounce on a loaded CI runner, which cannot be reproduced on demand — it surfaced once,
/// stayed unreproduced across five later runs, and only recurred much later. So the
/// mechanism is proven directly instead: hold a real exclusive handle, release it on a
/// timer, and require the helper to outlast it. A bare <c>File.Delete</c> in the same
/// position throws, which is what the first test pins.
/// </remarks>
[TestClass]
public sealed class DeleteFileWithRetryTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dfwr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup() => TestCleanupHelpers.DeleteDirectoryWithRetry(_dir);

    /// <summary>
    /// The premise. Without an open handle there is nothing to retry against and the whole
    /// helper would be theatre, so this pins that the situation it guards is real on this
    /// platform. On Unix an open handle does not block deletion at all, which is precisely
    /// why the original failure was Windows-only — so the assertion is made only there.
    /// </summary>
    [TestMethod]
    public void AnOpenHandleBlocksABareDelete_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix permits unlinking an open file; there is nothing to guard.");
            return;
        }

        string path = Path.Combine(_dir, "locked.json");
        File.WriteAllText(path, "{}");

        using FileStream hold = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.ThrowsExactly<IOException>(() => File.Delete(path),
            "If a bare delete no longer throws while a handle is open, DeleteFileWithRetry "
            + "is guarding a hazard that no longer exists and should be reconsidered.");
    }

    /// <summary>
    /// The behaviour the fix depends on: a handle held briefly — as a debounced background
    /// re-read holds one — must not fail the delete.
    /// </summary>
    [TestMethod]
    public void RetriesUntilAHeldHandleIsReleased()
    {
        string path = Path.Combine(_dir, "briefly-locked.json");
        File.WriteAllText(path, "{}");

        FileStream hold = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using ManualResetEventSlim released = new(false);

        // Release after ~150 ms: inside the helper's ~750 ms budget, and past its first
        // couple of attempts, so the retry loop is genuinely exercised rather than
        // succeeding immediately.
        Thread releaser = new(() =>
        {
            Thread.Sleep(150);
            hold.Dispose();
            released.Set();
        });
        releaser.Start();

        try
        {
            TestCleanupHelpers.DeleteFileWithRetry(path);
        }
        finally
        {
            released.Wait(TimeSpan.FromSeconds(5));
            releaser.Join(TimeSpan.FromSeconds(5));
            hold.Dispose();
        }

        Assert.IsFalse(File.Exists(path),
            "The helper returned without deleting the file. It must either delete it or "
            + "throw — silently leaving it behind would let a test carry on against state "
            + "it believes is gone.");
    }

    /// <summary>
    /// A handle that is never released must surface as a failure, not be swallowed. A retry
    /// helper that gives up quietly turns a hard error into a mystery further downstream.
    /// </summary>
    [TestMethod]
    public void ThrowsWhenTheHandleIsNeverReleased()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix permits unlinking an open file, so this cannot fail there.");
            return;
        }

        string path = Path.Combine(_dir, "永久.json");
        File.WriteAllText(path, "{}");

        using FileStream hold = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        // maxAttempts: 2 keeps the wait to ~50 ms rather than the default ~750 ms.
        Assert.ThrowsExactly<IOException>(
            () => TestCleanupHelpers.DeleteFileWithRetry(path, maxAttempts: 2));
    }

    /// <summary>A path that is already gone is not an error — teardown ordering varies.</summary>
    [TestMethod]
    public void MissingFileIsANoOp()
    {
        TestCleanupHelpers.DeleteFileWithRetry(Path.Combine(_dir, "never-existed.json"));
    }
}
