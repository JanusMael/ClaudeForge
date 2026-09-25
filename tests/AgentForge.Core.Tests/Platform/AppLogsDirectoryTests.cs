using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Platform;

/// <summary>
/// Verifies <see cref="PlatformPaths.AppLogsDirectory"/> always resolves to a
/// <c>logs/</c> subdirectory next to the running executable, and that the
/// <c>TestAppBaseDirOverride</c> seam correctly sandboxes tests.
/// </summary>
public sealed class AppLogsDirectoryTests : IDisposable
{
    private string _sandbox = null!;

    public AppLogsDirectoryTests() => Init();

    private void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestAppBaseDirOverride = _sandbox;
    }

    private void Cleanup()
    {
        PlatformPaths.TestAppBaseDirOverride = null;
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
    public void AppLogsDirectory_IsExactlySandboxLogs_WhenOverrideSet()
    {
        // With the override active the path must be <sandbox>/logs — no extra segments.
        string expected = Path.Combine(_sandbox, "logs");
        Assert.Equal(expected, PlatformPaths.AppLogsDirectory);
    }

    [Fact]
    public void AppLogsDirectory_EndsWithLogsSegment()
    {
        // The trailing segment must always be "logs" regardless of how the base dir is set.
        string path = PlatformPaths.AppLogsDirectory;
        string segment = Path.GetFileName(path);
        MessageAssert.Equal("logs", segment, $"Expected last path segment to be 'logs'; got: {path}");
    }

    [Fact]
    public void AppLogsDirectory_IsStableAcrossReads()
    {
        // The property is a computed getter (not cached), but while the override
        // is pinned by TestInitialize the two reads must return the same path.
        // MSTEST0032: the analyzer sees two syntactically identical expressions and
        // calls the comparison always-true. Repeated evaluation IS the subject under
        // test — a getter that recomputed a different path each read would fail here.
#pragma warning disable MSTEST0032
        Assert.Equal(PlatformPaths.AppLogsDirectory, PlatformPaths.AppLogsDirectory);
#pragma warning restore MSTEST0032
    }

    [Fact]
    public void AppLogsDirectory_IsDistinctFromDesktopLogsPath()
    {
        // Safety rail: we must never alias Anthropic's Claude Desktop log directory.
        string app = PlatformPaths.AppLogsDirectory;
        string? desktop = PlatformPaths.DesktopLogsPath;

        if (desktop is not null)
        {
            MessageAssert.NotEqual(desktop, app,
                $"AppLogsDirectory must not equal DesktopLogsPath. Both were '{app}'.");
        }
    }

    [Fact]
    public void AppLogsDirectory_ReturnsExeRelativePath_WhenOverrideIsNull()
    {
        // Clear the override so the property falls back to the real exe location.
        PlatformPaths.TestAppBaseDirOverride = null;

        string path = PlatformPaths.AppLogsDirectory;

        // Must still end with the "logs" segment.
        MessageAssert.Equal("logs", Path.GetFileName(path),
            $"Without override, the last segment must be 'logs'; got: {path}");

        // Must be an absolute path (not relative or empty).
        Assert.True(Path.IsPathRooted(path),
            $"Without override, path should be absolute; got: {path}");
    }
}