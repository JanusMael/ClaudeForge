using System.Runtime.InteropServices;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Platform;

/// <summary>
/// Covers <see cref="ProductVersionProbe.ResolveCommand"/> — the
/// exe/args pair chosen for each kind of resolved Claude Code binary.
/// The .cmd/.bat/.ps1 branches matter because Windows cannot launch
/// those shims directly with <c>UseShellExecute=false</c>; they need
/// an interpreter wrapper with careful quoting.
/// </summary>
[TestClass]
public sealed class ProductVersionProbeResolveCommandTests
{
    [TestMethod]
    public void ResolveCommand_NullPath_UsesBareClaudeOnPath()
    {
        ProductVersionProbe.ResolvedCommand cmd = ProductVersionProbe.ResolveCommand(null);
        Assert.AreEqual("claude", cmd.Exe);
        Assert.AreEqual("--version", cmd.Args);
    }

    [TestMethod]
    public void ResolveCommand_WhitespacePath_UsesBareClaudeOnPath()
    {
        ProductVersionProbe.ResolvedCommand cmd = ProductVersionProbe.ResolveCommand("   ");
        Assert.AreEqual("claude", cmd.Exe);
        Assert.AreEqual("--version", cmd.Args);
    }

    [TestMethod]
    public void ResolveCommand_PlainExecutable_ReturnsPathDirectly()
    {
        // .exe on Windows, extensionless on Unix — either way the probe
        // should invoke the file directly, no interpreter wrapping.
        string path = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? @"C:\tools\claude.exe"
            : "/usr/local/bin/claude";

        ProductVersionProbe.ResolvedCommand cmd = ProductVersionProbe.ResolveCommand(path);
        Assert.AreEqual(path, cmd.Exe);
        Assert.AreEqual("--version", cmd.Args);
    }

    [TestMethod]
    public void ResolveCommand_CmdShimOnWindows_WrapsInCmdExeWithSlashSlashC()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Windows-only: .cmd shims only exist on Windows");
            return;
        }

        const string shim = @"C:\Users\u\AppData\Roaming\npm\claude.cmd";
        ProductVersionProbe.ResolvedCommand cmd = ProductVersionProbe.ResolveCommand(shim);

        Assert.AreEqual("cmd.exe", cmd.Exe);
        // Must use /s /c (not bare /c) so cmd.exe strips the OUTER quotes
        // verbatim and does NOT merge them with the inner path quotes —
        // this is the only robust way to pass a path with spaces and
        // metacharacters through cmd unmodified.
        StringAssert.StartsWith(cmd.Args, "/s /c ");
        // Inner path must be quoted so spaces don't split the argv.
        StringAssert.Contains(cmd.Args, $"\"{shim}\"");
        StringAssert.Contains(cmd.Args, "--version");
    }

    [TestMethod]
    public void ResolveCommand_BatShimOnWindows_WrapsInCmdExe()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Windows-only: .bat shims only exist on Windows");
            return;
        }

        const string shim = @"C:\legacy\claude.bat";
        ProductVersionProbe.ResolvedCommand cmd = ProductVersionProbe.ResolveCommand(shim);

        Assert.AreEqual("cmd.exe", cmd.Exe);
        StringAssert.StartsWith(cmd.Args, "/s /c ");
        StringAssert.Contains(cmd.Args, $"\"{shim}\"");
    }

    [TestMethod]
    public void ResolveCommand_Ps1ShimOnWindows_WrapsInPowerShell()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Windows-only: .ps1 shim wrapping is Windows-specific");
            return;
        }

        const string shim = @"C:\Users\u\AppData\Local\claude.ps1";
        ProductVersionProbe.ResolvedCommand cmd = ProductVersionProbe.ResolveCommand(shim);

        Assert.AreEqual("powershell.exe", cmd.Exe);
        StringAssert.Contains(cmd.Args, "-NoProfile");
        StringAssert.Contains(cmd.Args, "-ExecutionPolicy Bypass");
        StringAssert.Contains(cmd.Args, $"-File \"{shim}\"");
        StringAssert.Contains(cmd.Args, "--version");
    }

    [TestMethod]
    public void ResolveCommand_CaseInsensitiveExtensionMatch()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Windows-only case-insensitive extension");
            return;
        }

        // Windows filesystems are case-insensitive — a `.CMD` shim must
        // be treated the same as a `.cmd` shim.
        ProductVersionProbe.ResolvedCommand cmd = ProductVersionProbe.ResolveCommand(@"C:\tools\claude.CMD");
        Assert.AreEqual("cmd.exe", cmd.Exe);
    }
}