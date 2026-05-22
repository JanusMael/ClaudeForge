using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Platform;

/// <summary>
/// Verifies <see cref="PlatformPaths.AppLogsDirectory"/> always resolves to a
/// <c>logs/</c> subdirectory next to the running executable, and that the
/// <c>TestAppBaseDirOverride</c> seam correctly sandboxes tests.
/// </summary>
[TestClass]
public sealed class AppLogsDirectoryTests
{
    private string _sandbox = null!;

    [TestInitialize]
    public void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestAppBaseDirOverride = _sandbox;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestAppBaseDirOverride = null;
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }

    [TestMethod]
    public void AppLogsDirectory_IsExactlySandboxLogs_WhenOverrideSet()
    {
        // With the override active the path must be <sandbox>/logs — no extra segments.
        string expected = Path.Combine(_sandbox, "logs");
        Assert.AreEqual(expected, PlatformPaths.AppLogsDirectory);
    }

    [TestMethod]
    public void AppLogsDirectory_EndsWithLogsSegment()
    {
        // The trailing segment must always be "logs" regardless of how the base dir is set.
        string path = PlatformPaths.AppLogsDirectory;
        string segment = Path.GetFileName(path);
        Assert.AreEqual("logs", segment, $"Expected last path segment to be 'logs'; got: {path}");
    }

    [TestMethod]
    public void AppLogsDirectory_IsStableAcrossReads()
    {
        // The property is a computed getter (not cached), but while the override
        // is pinned by TestInitialize the two reads must return the same path.
        Assert.AreEqual(PlatformPaths.AppLogsDirectory, PlatformPaths.AppLogsDirectory);
    }

    [TestMethod]
    public void AppLogsDirectory_IsDistinctFromDesktopLogsPath()
    {
        // Safety rail: we must never alias Anthropic's Claude Desktop log directory.
        string app = PlatformPaths.AppLogsDirectory;
        string? desktop = PlatformPaths.DesktopLogsPath;

        if (desktop is not null)
        {
            Assert.AreNotEqual(desktop, app,
                $"AppLogsDirectory must not equal DesktopLogsPath. Both were '{app}'.");
        }
    }

    [TestMethod]
    public void AppLogsDirectory_ReturnsExeRelativePath_WhenOverrideIsNull()
    {
        // Clear the override so the property falls back to the real exe location.
        PlatformPaths.TestAppBaseDirOverride = null;

        string path = PlatformPaths.AppLogsDirectory;

        // Must still end with the "logs" segment.
        Assert.AreEqual("logs", Path.GetFileName(path),
            $"Without override, the last segment must be 'logs'; got: {path}");

        // Must be an absolute path (not relative or empty).
        Assert.IsTrue(Path.IsPathRooted(path),
            $"Without override, path should be absolute; got: {path}");
    }
}