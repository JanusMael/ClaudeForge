namespace Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;

/// <summary>
/// Locks the best-effort contract of <see cref="TestCleanupHelpers.DeleteDirectoryWithRetry"/>.
/// The helper is teardown plumbing for ten other fixtures, so a regression here surfaces as
/// unrelated tests turning red on the Windows runner — which is exactly what it is here to
/// prevent.
/// </summary>
[TestClass]
public sealed class TestCleanupHelpersTests
{
    private string _sandbox = null!;

    [TestInitialize]
    public void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
    }

    [TestCleanup]
    public void Cleanup()
    {
        // Deliberately the plain call: by now nothing in this class holds a handle, and using
        // the helper under test to clean up after the helper under test would hide a failure.
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }

    [TestMethod]
    public void DeleteDirectoryWithRetry_HandleHeldThroughout_DoesNotThrow()
    {
        // The CI flake this helper exists for: a writer still holds a file in the sandbox when
        // teardown runs. On Windows that makes Directory.Delete throw for the whole retry
        // budget; the helper must absorb it so an already-passing test is not failed by its
        // own janitor. maxAttempts is 2 to keep the backoff to a single 50 ms wait.
        string locked = Path.Combine(_sandbox, "claude-code-settings.json");
        using FileStream hold = new(locked, FileMode.Create, FileAccess.Write, FileShare.None);

        TestCleanupHelpers.DeleteDirectoryWithRetry(_sandbox, maxAttempts: 2);

        if (OperatingSystem.IsWindows())
        {
            // Windows refuses to unlink a file with an open exclusive handle, so the tree
            // survives — the point is that the helper returned rather than threw.
            Assert.IsTrue(Directory.Exists(_sandbox),
                "Precondition for this platform: the open handle should have blocked the delete.");
        }
        else
        {
            // POSIX unlink succeeds against an open file, so the tree is gone. Either outcome
            // is fine; not throwing is the contract.
            Assert.IsFalse(Directory.Exists(_sandbox),
                "On POSIX an open handle does not block unlink, so the tree should be gone.");
        }
    }

    [TestMethod]
    public void DeleteDirectoryWithRetry_NothingHoldingIt_DeletesTheTree()
    {
        Directory.CreateDirectory(Path.Combine(_sandbox, "nested", "deeper"));
        File.WriteAllText(Path.Combine(_sandbox, "nested", "deeper", "leaf.json"), "{}");

        TestCleanupHelpers.DeleteDirectoryWithRetry(_sandbox);

        Assert.IsFalse(Directory.Exists(_sandbox), "A tree nothing holds must be removed.");
    }

    [TestMethod]
    public void DeleteDirectoryWithRetry_MissingDirectory_IsNoOp()
    {
        string absent = Path.Combine(_sandbox, "never-created");

        TestCleanupHelpers.DeleteDirectoryWithRetry(absent);

        Assert.IsFalse(Directory.Exists(absent));
    }

    [TestMethod]
    public void DeleteDirectoryWithRetry_RejectsUnusableArguments()
    {
        // Absorbing IO failures must not extend to swallowing caller mistakes.
        Assert.ThrowsExactly<ArgumentException>(
            () => TestCleanupHelpers.DeleteDirectoryWithRetry("   "));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => TestCleanupHelpers.DeleteDirectoryWithRetry(_sandbox, maxAttempts: 0));
    }
}
