using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Pins the Unix shell-detection table used by the new
/// macOS / Linux Add-to-PATH path: which rc file to write, what export
/// syntax to use.  Mutates <c>$SHELL</c> + <c>$HOME</c> for the duration
/// of each test and restores them afterwards.
/// </summary>
[TestClass]
public sealed class AboutEditorResolveUnixShellRcTests
{
    private string? _origShell;
    private string? _origHome;

    [TestInitialize]
    public void Setup()
    {
        _origShell = Environment.GetEnvironmentVariable("SHELL");
        _origHome = Environment.GetEnvironmentVariable("HOME");
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("SHELL", _origShell);
        Environment.SetEnvironmentVariable("HOME", _origHome);
    }

    private const string Dir = "/home/test/.local/bin";

    [TestMethod]
    public void Resolve_NoHome_ReturnsNullRcPath()
    {
        Environment.SetEnvironmentVariable("HOME", string.Empty);
        Environment.SetEnvironmentVariable("SHELL", "/bin/bash");

        (string? rcPath, string _, string _) = AboutEditorViewModel.ResolveUnixShellRcTarget(Dir);

        Assert.IsNull(rcPath, "Empty $HOME must produce a null rcPath so the caller falls back to Unsupported.");
    }

    [TestMethod]
    public void Resolve_FishShell_PicksFishConfig_AndFishSyntax()
    {
        Environment.SetEnvironmentVariable("HOME", "/home/test");
        Environment.SetEnvironmentVariable("SHELL", "/usr/bin/fish");

        (string? rcPath, string exportLine, string kind) = AboutEditorViewModel.ResolveUnixShellRcTarget(Dir);

        Assert.IsNotNull(rcPath);
        StringAssert.EndsWith(rcPath!, Path.Combine(".config", "fish", "config.fish"));
        StringAssert.Contains(exportLine, "set -x PATH");
        StringAssert.Contains(exportLine, Dir);
        Assert.AreEqual("fish", kind);
    }

    [TestMethod]
    public void Resolve_ZshShell_PicksZshrc_AndExportSyntax()
    {
        Environment.SetEnvironmentVariable("HOME", "/home/test");
        Environment.SetEnvironmentVariable("SHELL", "/bin/zsh");

        (string? rcPath, string exportLine, string kind) = AboutEditorViewModel.ResolveUnixShellRcTarget(Dir);

        Assert.IsNotNull(rcPath);
        StringAssert.EndsWith(rcPath!, ".zshrc");
        StringAssert.Contains(exportLine, "export PATH=");
        StringAssert.Contains(exportLine, Dir);
        Assert.AreEqual("zsh", kind);
    }

    [TestMethod]
    public void Resolve_BashShell_PicksBashrc_AndExportSyntax()
    {
        Environment.SetEnvironmentVariable("HOME", "/home/test");
        Environment.SetEnvironmentVariable("SHELL", "/bin/bash");

        (string? rcPath, string exportLine, string kind) = AboutEditorViewModel.ResolveUnixShellRcTarget(Dir);

        Assert.IsNotNull(rcPath);
        StringAssert.EndsWith(rcPath!, ".bashrc");
        StringAssert.Contains(exportLine, "export PATH=");
        StringAssert.Contains(exportLine, Dir);
        Assert.AreEqual("bash", kind);
    }

    [TestMethod]
    public void Resolve_UnknownShell_FallsBackToBashrc()
    {
        // Default fallback covers sh / ash / dash / busybox via the same
        // POSIX-ish export syntax — bash semantics are a safe superset.
        Environment.SetEnvironmentVariable("HOME", "/home/test");
        Environment.SetEnvironmentVariable("SHELL", "/bin/sh");

        (string? rcPath, string _, string kind) = AboutEditorViewModel.ResolveUnixShellRcTarget(Dir);

        Assert.IsNotNull(rcPath);
        StringAssert.EndsWith(rcPath!, ".bashrc");
        Assert.AreEqual("bash", kind);
    }

    [TestMethod]
    public void Resolve_FishExportLine_QuotesDirectory()
    {
        // Defensive: directory paths can contain spaces ($HOME/My Apps/.local/bin
        // for instance).  The fish + bash export lines must wrap the directory
        // in quotes so spaces don't break the line.
        Environment.SetEnvironmentVariable("HOME", "/home/test");
        Environment.SetEnvironmentVariable("SHELL", "/usr/bin/fish");

        string dirWithSpace = "/home/test/My Apps/.local/bin";
        (string? _, string exportLine, string _) = AboutEditorViewModel.ResolveUnixShellRcTarget(dirWithSpace);

        StringAssert.Contains(exportLine, $"\"{dirWithSpace}\"");
    }
}