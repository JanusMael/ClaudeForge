using Bennewitz.Ninja.AgentForge.Core.Backup;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// Lightweight platform-gate + basic behaviour tests for <see cref="MsixPathProbe"/>.
/// Full coverage (actual junction creation + merge) requires a real MSIX install,
/// which we cannot synthesise in CI, so we test the parts that are safely probable.
/// </summary>
public sealed class MsixPathProbeTests
{
    [Fact]
    public void FindVirtualisedPath_OnNonWindowsReturnsNull()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Test is only meaningful on non-Windows.");
            return;
        }

        Assert.Null(MsixPathProbe.Instance.FindVirtualisedPath());
    }

    [Fact]
    public void Probe_NonWindowsReportsNoFixNeeded()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Test is only meaningful on non-Windows.");
            return;
        }

        MsixStatus status = MsixPathProbe.Instance.Probe();
        Assert.False(status.HasMsixInstall);
        Assert.False(status.NeedsFix);
        Assert.Null(status.VirtualisedPath);
    }

    [Fact]
    public void IsReparsePoint_OnRegularFolderReturnsFalse()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "mrp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            Assert.False(MsixPathProbe.IsReparsePoint(tmp));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void IsReparsePoint_OnMissingPathReturnsFalse()
    {
        Assert.False(MsixPathProbe.IsReparsePoint(Path.Combine(Path.GetTempPath(),
            "does-not-exist-" + Guid.NewGuid().ToString("N"))));
    }
}