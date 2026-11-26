using System.Runtime.InteropServices;
using Bennewitz.Ninja.AgentForge.Core.Platform;

using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Platform;

/// <summary>
/// Covers the process-lifetime cache layered onto
/// <see cref="ClaudePaths.TryFindClaudeCodeBinary"/> and
/// <see cref="ClaudePaths.IsClaudeCodeOnPath"/>. Two contracts:
///
/// 1. After a first probe, removing the binary on disk does NOT change the
///    next probe's result — the cache holds the prior answer until
///    <see cref="ClaudePaths.InvalidatePathCache"/> is called.
///
/// 2. After explicit invalidation, the next probe re-runs and reflects the
///    new disk state.
/// </summary>
// Tests the process-lifetime claude-code location cache by design — inherently
// process-wide shared state, so it cannot run concurrently. Run serially,
// isolated from the method-level-parallelized rest of the assembly.
[Collection("DoNotParallelize")]
public sealed class PlatformPathsCacheTests : IDisposable
{
    private string _sandbox = null!;
    private string? _originalPath;

    public PlatformPathsCacheTests() => Init();

    private void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "ppc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        _originalPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", string.Empty);

        ClaudePaths.InvalidatePathCache();
    }

    private void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        ClaudePaths.InvalidatePathCache();
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    [Fact]
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
        ClaudePaths.ClaudeCodeLocation? first = ClaudePaths.TryFindClaudeCodeBinary(ClaudeEnvironment.Empty);
        Assert.NotNull(first);
        Assert.Equal(binaryPath, first!.BinaryPath);

        // Delete the binary on disk. A re-probe would now find nothing.
        File.Delete(binaryPath);

        // Second call WITHOUT invalidation — must still return the cached
        // location. (Process-lifetime cache contract.)
        ClaudePaths.ClaudeCodeLocation? secondCached = ClaudePaths.TryFindClaudeCodeBinary(ClaudeEnvironment.Empty);
        MessageAssert.NotNull(secondCached,
            "Cache should still return the prior location even though the file is gone.");
        Assert.Equal(binaryPath, secondCached!.BinaryPath);

        // After invalidation, re-probe sees the empty disk.
        ClaudePaths.InvalidatePathCache();
        ClaudePaths.ClaudeCodeLocation? afterInvalidate = ClaudePaths.TryFindClaudeCodeBinary(ClaudeEnvironment.Empty);
        MessageAssert.Null(afterInvalidate,
            "Post-invalidation probe should reflect the now-empty sandbox.");
    }

    [Fact]
    public void IsClaudeCodeOnPath_CachesNegativeResult()
    {
        // PATH is empty (set by Init), so the bare-name lookup will miss.
        Assert.False(ClaudePaths.IsClaudeCodeOnPath,
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

        Assert.False(ClaudePaths.IsClaudeCodeOnPath,
            "Negative cache must be honoured until InvalidatePathCache is called.");

        ClaudePaths.InvalidatePathCache();

        Assert.True(ClaudePaths.IsClaudeCodeOnPath,
            "After invalidation, the next probe should see the binary on PATH.");
    }
}