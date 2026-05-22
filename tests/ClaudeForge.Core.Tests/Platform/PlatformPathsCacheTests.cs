using System.Runtime.InteropServices;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Platform;

/// <summary>
/// Covers the process-lifetime cache layered onto
/// <see cref="PlatformPaths.TryFindClaudeCodeBinary"/> and
/// <see cref="PlatformPaths.IsClaudeCodeOnPath"/>. Two contracts:
///
/// 1. After a first probe, removing the binary on disk does NOT change the
///    next probe's result — the cache holds the prior answer until
///    <see cref="PlatformPaths.InvalidatePathCache"/> is called.
///
/// 2. After explicit invalidation, the next probe re-runs and reflects the
///    new disk state.
/// </summary>
[TestClass]
public sealed class PlatformPathsCacheTests
{
    private string _sandbox = null!;
    private string? _originalPath;

    [TestInitialize]
    public void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "ppc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        _originalPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", string.Empty);

        PlatformPaths.InvalidatePathCache();
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        PlatformPaths.InvalidatePathCache();
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }

    [TestMethod]
    public void TryFindClaudeCodeBinary_CachesResult_AcrossCalls()
    {
        // Place a binary at the canonical first-priority self-contained location.
        string localDir = Path.Combine(_sandbox, ".claude", "local");
        Directory.CreateDirectory(localDir);
        string binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "claude.exe"
            : "claude";
        string binaryPath = Path.Combine(localDir, binaryName);
        File.WriteAllText(binaryPath, string.Empty);

        // First call — primes the cache and returns the located binary.
        PlatformPaths.ClaudeCodeLocation? first = PlatformPaths.TryFindClaudeCodeBinary();
        Assert.IsNotNull(first);
        Assert.AreEqual(binaryPath, first!.BinaryPath);

        // Delete the binary on disk. A re-probe would now find nothing.
        File.Delete(binaryPath);

        // Second call WITHOUT invalidation — must still return the cached
        // location. (Process-lifetime cache contract.)
        PlatformPaths.ClaudeCodeLocation? secondCached = PlatformPaths.TryFindClaudeCodeBinary();
        Assert.IsNotNull(secondCached,
            "Cache should still return the prior location even though the file is gone.");
        Assert.AreEqual(binaryPath, secondCached!.BinaryPath);

        // After invalidation, re-probe sees the empty disk.
        PlatformPaths.InvalidatePathCache();
        PlatformPaths.ClaudeCodeLocation? afterInvalidate = PlatformPaths.TryFindClaudeCodeBinary();
        Assert.IsNull(afterInvalidate,
            "Post-invalidation probe should reflect the now-empty sandbox.");
    }

    [TestMethod]
    public void IsClaudeCodeOnPath_CachesNegativeResult()
    {
        // PATH is empty (set by Init), so the bare-name lookup will miss.
        Assert.IsFalse(PlatformPaths.IsClaudeCodeOnPath,
            "Sanity: empty PATH means claude is not on PATH.");

        // Now create a `claude` binary in a directory and prepend that
        // directory to PATH. Because IsClaudeCodeOnPath was already
        // probed-and-cached as false, the new arrangement should NOT change
        // its return value until we invalidate.
        string pathDir = Path.Combine(_sandbox, "bin");
        Directory.CreateDirectory(pathDir);
        string binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "claude.exe"
            : "claude";
        File.WriteAllText(Path.Combine(pathDir, binaryName), string.Empty);
        Environment.SetEnvironmentVariable("PATH", pathDir);

        Assert.IsFalse(PlatformPaths.IsClaudeCodeOnPath,
            "Negative cache must be honoured until InvalidatePathCache is called.");

        PlatformPaths.InvalidatePathCache();

        Assert.IsTrue(PlatformPaths.IsClaudeCodeOnPath,
            "After invalidation, the next probe should see the binary on PATH.");
    }
}