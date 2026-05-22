using Bennewitz.Ninja.ClaudeForge.Core.Backup;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Backup;

/// <summary>
/// Lightweight platform-gate + basic behaviour tests for <see cref="MsixPathProbe"/>.
/// Full coverage (actual junction creation + merge) requires a real MSIX install,
/// which we cannot synthesise in CI, so we test the parts that are safely probable.
/// </summary>
[TestClass]
public sealed class MsixPathProbeTests
{
    [TestMethod]
    public void FindVirtualisedPath_OnNonWindowsReturnsNull()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Test is only meaningful on non-Windows.");
            return;
        }

        Assert.IsNull(MsixPathProbe.Instance.FindVirtualisedPath());
    }

    [TestMethod]
    public void Probe_NonWindowsReportsNoFixNeeded()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Test is only meaningful on non-Windows.");
            return;
        }

        MsixStatus status = MsixPathProbe.Instance.Probe();
        Assert.IsFalse(status.HasMsixInstall);
        Assert.IsFalse(status.NeedsFix);
        Assert.IsNull(status.VirtualisedPath);
    }

    [TestMethod]
    public void IsReparsePoint_OnRegularFolderReturnsFalse()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "mrp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            Assert.IsFalse(MsixPathProbe.IsReparsePoint(tmp));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [TestMethod]
    public void IsReparsePoint_OnMissingPathReturnsFalse()
    {
        Assert.IsFalse(MsixPathProbe.IsReparsePoint(Path.Combine(Path.GetTempPath(),
            "does-not-exist-" + Guid.NewGuid().ToString("N"))));
    }
}