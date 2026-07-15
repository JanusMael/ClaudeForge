using System.Runtime.InteropServices;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Platform;

/// <summary>
/// Covers <see cref="PlatformPaths.TryFindClaudeCodeBinary"/>, the canonical-
/// directory fallback that catches the Windows-ARM64-npm-global scenario
/// where <c>%APPDATA%\npm\claude.cmd</c> exists but is not on PATH.
/// </summary>
// Mutates the process-wide PATH env var AND the process-lifetime claude-code
// location cache (_claudeCodeLocationCache) — both real process globals, not
// AsyncLocal test seams. Must run serially, isolated from the parallel batch.
[DoNotParallelize]
[TestClass]
public sealed class ClaudeCodeDetectionTests
{
    private string _sandbox = null!;
    private string? _originalPath;

    [TestInitialize]
    public void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        // Clear the process-scope PATH so the detection tests are not
        // polluted by a real `claude` installed on the developer's machine.
        // Process-scope SetEnvironmentVariable does NOT persist beyond this
        // process, so there is no cleanup risk beyond the Cleanup restore.
        _originalPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", string.Empty);

        // PlatformPaths memoises FindFirstOnPath / TryFindClaudeCodeBinary
        // for process lifetime. Without invalidation between tests, a hit
        // from a real-machine PATH that leaked through the first test, or
        // a per-test cached negative, would survive into sibling tests.
        PlatformPaths.InvalidatePathCache();
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        // Same reason as Init — clear so the next test class doesn't see a
        // cached value derived from this test's sandbox PATH/sandbox files.
        PlatformPaths.InvalidatePathCache();
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }

    [TestMethod]
    public void TryFindClaudeCodeBinary_NoPath_NoFiles_ReturnsNull()
    {
        PlatformPaths.ClaudeCodeLocation? result = PlatformPaths.TryFindClaudeCodeBinary();
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryFindClaudeCodeBinary_SelfContainedInstall_ReturnsLocationNotOnPath()
    {
        // ~/.claude/local/claude(.exe) — the canonical first-priority install
        string localDir = Path.Combine(_sandbox, ".claude", "local");
        Directory.CreateDirectory(localDir);

        string fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "claude.exe"
            : "claude";
        string binary = Path.Combine(localDir, fileName);
        File.WriteAllText(binary, string.Empty);

        PlatformPaths.ClaudeCodeLocation? result = PlatformPaths.TryFindClaudeCodeBinary();

        Assert.IsNotNull(result);
        Assert.AreEqual(binary, result!.BinaryPath);
        Assert.IsFalse(result.IsOnPath);
    }

    [TestMethod]
    public void TryFindClaudeCodeBinary_NpmGlobalOnWindows_ReturnsLocationNotOnPath()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Windows-only scenario (%APPDATA%\\npm\\claude.cmd)");
            return;
        }

        // Simulate the Windows-ARM64 npm-global layout:
        //   %APPDATA%\npm\claude.cmd exists, but %APPDATA%\npm is NOT on PATH.
        string npmDir = Path.Combine(_sandbox, "AppData", "Roaming", "npm");
        Directory.CreateDirectory(npmDir);
        string binary = Path.Combine(npmDir, "claude.cmd");
        File.WriteAllText(binary, "@echo claude 1.0.0");

        PlatformPaths.ClaudeCodeLocation? result = PlatformPaths.TryFindClaudeCodeBinary();

        Assert.IsNotNull(result);
        Assert.AreEqual(binary, result!.BinaryPath);
        Assert.IsFalse(result.IsOnPath);
    }

    [TestMethod]
    public void TryFindClaudeCodeBinary_PathWinsOverCanonical_ReturnsIsOnPathTrue()
    {
        // Arrange: create a claude binary both on PATH (sandboxed shim
        // directory) AND at the canonical self-contained location. PATH
        // should win — the "just works" case — and IsOnPath must be true.
        string pathDir = Path.Combine(_sandbox, "bin");
        Directory.CreateDirectory(pathDir);

        string canonicalDir = Path.Combine(_sandbox, ".claude", "local");
        Directory.CreateDirectory(canonicalDir);

        string fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "claude.exe"
            : "claude";
        string pathBinary = Path.Combine(pathDir, fileName);
        string canonicalBinary = Path.Combine(canonicalDir, fileName);
        File.WriteAllText(pathBinary, string.Empty);
        File.WriteAllText(canonicalBinary, string.Empty);

        Environment.SetEnvironmentVariable("PATH", pathDir);

        try
        {
            PlatformPaths.ClaudeCodeLocation? result = PlatformPaths.TryFindClaudeCodeBinary();

            Assert.IsNotNull(result);
            Assert.IsTrue(result!.IsOnPath);
            // PATH resolution on Windows appends the PATHEXT extension as-cased
            // (e.g. ".EXE"), so the returned path's extension casing may differ
            // from the filesystem file's casing. Compare case-insensitively on
            // Windows, exact on Unix.
            StringComparison comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            Assert.IsTrue(string.Equals(pathBinary, result.BinaryPath, comparison),
                $"Expected '{pathBinary}', got '{result.BinaryPath}'");
        }
        finally
        {
            // Restore empty PATH so subsequent tests in this class see
            // the sandboxed baseline.
            Environment.SetEnvironmentVariable("PATH", string.Empty);
        }
    }

    [TestMethod]
    public void IsClaudeCodeOnPath_ReturnsFalseWhenPathEmpty()
    {
        Assert.IsFalse(PlatformPaths.IsClaudeCodeOnPath);
    }

    [TestMethod]
    public void IsClaudeCodeInstalled_TrueWhenCanonicalExistsEvenIfNotOnPath()
    {
        string localDir = Path.Combine(_sandbox, ".claude", "local");
        Directory.CreateDirectory(localDir);
        string fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "claude.exe"
            : "claude";
        File.WriteAllText(Path.Combine(localDir, fileName), string.Empty);

        Assert.IsTrue(PlatformPaths.IsClaudeCodeInstalled);
    }

    [TestMethod]
    public void IsClaudeCodeInstalled_FalseOnEmptySandbox()
    {
        Assert.IsFalse(PlatformPaths.IsClaudeCodeInstalled);
    }

    // ── Extended candidate-path coverage (2026-05-19 COVERAGE-B3 #3) ──
    //
    // The existing TryFindClaudeCodeBinary_NpmGlobalOnWindows test covers
    // `%APPDATA%\npm\claude.cmd` on Windows and SelfContainedInstall
    // covers `~/.claude/local/claude(.exe)` on both OSes.  These additions
    // exercise the remaining candidates returned by
    // CanonicalClaudeCodeCandidates so the enumeration's coverage rises
    // toward 80%.  Each test is gated by OS so it runs on the platform
    // whose branch it exercises (Windows-host runs land coverage on the
    // Windows branch; WSL/Linux runs land coverage on the non-Windows
    // branch).

    [TestMethod]
    public void TryFindClaudeCodeBinary_WindowsNpmGlobalPs1_ReturnsLocationNotOnPath()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Windows-only candidate (%APPDATA%\\npm\\claude.ps1)");
            return;
        }

        string npmDir = Path.Combine(_sandbox, "AppData", "Roaming", "npm");
        Directory.CreateDirectory(npmDir);
        string binary = Path.Combine(npmDir, "claude.ps1");
        File.WriteAllText(binary, "# claude ps1 shim");

        PlatformPaths.ClaudeCodeLocation? result = PlatformPaths.TryFindClaudeCodeBinary();

        Assert.IsNotNull(result);
        Assert.AreEqual(binary, result!.BinaryPath);
        Assert.IsFalse(result.IsOnPath);
    }

    [TestMethod]
    public void TryFindClaudeCodeBinary_WindowsLocalAppDataPrograms_ReturnsLocationNotOnPath()
    {
        // Future-proofing candidate: %LOCALAPPDATA%\Programs\claude\claude.exe
        // — mirrors how VS Code / GitHub Desktop install per-user on Windows.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Windows-only candidate (%LOCALAPPDATA%\\Programs\\claude\\)");
            return;
        }

        string programsDir = Path.Combine(_sandbox, "AppData", "Local", "Programs", "claude");
        Directory.CreateDirectory(programsDir);
        string binary = Path.Combine(programsDir, "claude.exe");
        File.WriteAllText(binary, string.Empty);

        PlatformPaths.ClaudeCodeLocation? result = PlatformPaths.TryFindClaudeCodeBinary();

        Assert.IsNotNull(result);
        Assert.AreEqual(binary, result!.BinaryPath);
        Assert.IsFalse(result.IsOnPath);
    }

    [TestMethod]
    public void TryFindClaudeCodeBinary_UnixLocalBin_ReturnsLocationNotOnPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Unix-only candidate (~/.local/bin/claude — curl|bash installer default)");
            return;
        }

        string localBin = Path.Combine(_sandbox, ".local", "bin");
        Directory.CreateDirectory(localBin);
        string binary = Path.Combine(localBin, "claude");
        File.WriteAllText(binary, "#!/usr/bin/env node\n");

        PlatformPaths.ClaudeCodeLocation? result = PlatformPaths.TryFindClaudeCodeBinary();

        Assert.IsNotNull(result);
        Assert.AreEqual(binary, result!.BinaryPath);
        Assert.IsFalse(result.IsOnPath);
    }

    [TestMethod]
    public void TryFindClaudeCodeBinary_UnixNpmGlobalBin_ReturnsLocationNotOnPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Unix-only candidate (~/.npm-global/bin/claude)");
            return;
        }

        string npmGlobalBin = Path.Combine(_sandbox, ".npm-global", "bin");
        Directory.CreateDirectory(npmGlobalBin);
        string binary = Path.Combine(npmGlobalBin, "claude");
        File.WriteAllText(binary, "#!/usr/bin/env node\n");

        PlatformPaths.ClaudeCodeLocation? result = PlatformPaths.TryFindClaudeCodeBinary();

        Assert.IsNotNull(result);
        Assert.AreEqual(binary, result!.BinaryPath);
        Assert.IsFalse(result.IsOnPath);
    }

    [TestMethod]
    public void TryFindClaudeCodeBinary_UnixVoltaBin_ReturnsLocationNotOnPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Unix-only candidate (~/.volta/bin/claude — Volta-managed npm binaries)");
            return;
        }

        string voltaBin = Path.Combine(_sandbox, ".volta", "bin");
        Directory.CreateDirectory(voltaBin);
        string binary = Path.Combine(voltaBin, "claude");
        File.WriteAllText(binary, "#!/usr/bin/env node\n");

        PlatformPaths.ClaudeCodeLocation? result = PlatformPaths.TryFindClaudeCodeBinary();

        Assert.IsNotNull(result);
        Assert.AreEqual(binary, result!.BinaryPath);
        Assert.IsFalse(result.IsOnPath);
    }
}