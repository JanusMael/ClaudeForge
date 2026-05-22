// All Windows-only API calls in this file are guarded by OperatingSystem.IsWindows()
// checks that call Assert.Inconclusive() on non-Windows.  The compiler cannot prove
// that statically, so we suppress CA1416 at the file level rather than repeating the
// pragma on every affected line.

#pragma warning disable CA1416

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Services;

/// <summary>
/// Tests for the Windows Terminal / PowerShell launch path in <see cref="ShellLauncher"/>.
///
/// Two categories:
/// 1. Unit tests — verify arg structure without launching anything.
/// 2. Integration tests — actually launch a process; require Windows and are
///    skipped on CI.  Run them manually with:
///      dotnet test --filter "TestCategory=Integration.WT"
/// 3. Diagnostic test — dumps environment info; useful when the launch silently
///    fails.  Run with:
///      dotnet test --filter "TestCategory=Diagnostic.WT"
/// </summary>
[TestClass]
public sealed class ShellLauncherWindowsTerminalTests
{
    // -----------------------------------------------------------------------
    // Unit tests — verify ProcessStartInfo structure (no process launched)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void BuildWindowsTerminalPsi_PsExeIsFirstTokenOfCommandline()
    {
        // The --commandline value is passed verbatim by wt.exe to CreateProcess as
        // lpCommandLine.  CreateProcess splits on the first space to find the executable,
        // so psExe must be the very first token and must not contain spaces itself.
        ProcessStartInfo psi = ShellLauncher.BuildWindowsTerminalPsi(@"C:\fake\wt.exe", "pwsh.exe", "dGVzdA==");

        StringAssert.StartsWith(psi.ArgumentList[2], "pwsh.exe",
            "The --commandline value must start with the psExe name");
    }

    [TestMethod]
    public void BuildWindowsTerminalPsi_ArgumentsHaveCorrectStructure()
    {
        // wt.exe positional arg parsing joins all post-subcommand tokens into one string and
        // wraps the whole block in quotes before calling CreateProcess:
        //   ["new-tab", "pwsh.exe", "-NoExit", …] → CreateProcess("\"pwsh.exe -NoExit …\"")
        // That outer-quoted block becomes a single token (the executable name) → ERROR_FILE_NOT_FOUND.
        //
        // Fix: use --commandline so wt.exe receives the shell invocation as a named option value,
        // strips the .NET-applied outer quotes, and passes the raw string to CreateProcess:
        //   ["new-tab", "--commandline", "pwsh.exe -NoExit -EncodedCommand …"]
        //   → wt passes:  pwsh.exe -NoExit -EncodedCommand …  (unquoted) to CreateProcess ✓
        const string encoded = "dGVzdA==";
        ProcessStartInfo psi = ShellLauncher.BuildWindowsTerminalPsi(@"C:\fake\wt.exe", "pwsh.exe", encoded);

        Assert.AreEqual(@"C:\fake\wt.exe", psi.FileName);
        Assert.AreEqual(3, psi.ArgumentList.Count);
        Assert.AreEqual("new-tab", psi.ArgumentList[0]);
        Assert.AreEqual("--commandline", psi.ArgumentList[1]);
        StringAssert.Contains(psi.ArgumentList[2], "pwsh.exe");
        StringAssert.Contains(psi.ArgumentList[2], "-NoExit");
        StringAssert.Contains(psi.ArgumentList[2], "-EncodedCommand");
        StringAssert.Contains(psi.ArgumentList[2], encoded);
    }

    [TestMethod]
    public void BuildWindowsTerminalPsi_CommandlineValueContainsNoSurroundingQuotes()
    {
        // The --commandline VALUE must be a bare command line string (no wrapping quotes).
        // .NET's ArgumentList will quote it when building lpCommandLine for wt.exe because
        // it contains spaces — wt.exe's option parser strips those outer quotes and passes
        // the raw value to CreateProcess.  If we also added outer quotes here, wt.exe would
        // receive a double-quoted string and mis-parse it.
        const string encoded = "dGVzdA==";
        ProcessStartInfo psi = ShellLauncher.BuildWindowsTerminalPsi(@"C:\fake\wt.exe", "pwsh.exe", encoded);
        string commandlineValue = psi.ArgumentList[2];

        Assert.IsFalse(commandlineValue.StartsWith('"'),
            $"--commandline value must not start with a quote: '{commandlineValue}'");
        Assert.IsFalse(commandlineValue.EndsWith('"'),
            $"--commandline value must not end with a quote: '{commandlineValue}'");
    }

    [TestMethod]
    public void BuildWindowsTerminalPsi_UseShellExecuteIsFalse()
    {
        // wt.exe must be launched via CreateProcess (UseShellExecute=false) so that
        // ArgumentList is used; ShellExecute ignores ArgumentList.
        ProcessStartInfo psi = ShellLauncher.BuildWindowsTerminalPsi(@"C:\fake\wt.exe", "pwsh.exe", "dGVzdA==");
        Assert.IsFalse(psi.UseShellExecute);
    }

    [TestMethod]
    public void BuildDirectPowerShellPsi_UseShellExecuteIsTrue()
    {
        // Direct PS launch (no wt.exe) uses UseShellExecute=true so the OS opens a
        // new visible console window (conhost / WT via ConDrv delegation).
        ProcessStartInfo psi = ShellLauncher.BuildDirectPowerShellPsi("pwsh.exe", "dGVzdA==");
        Assert.IsTrue(psi.UseShellExecute);
    }

    [TestMethod]
    public void BuildDirectPowerShellPsi_ArgumentsContainEncodedCommand()
    {
        const string encoded = "dGVzdA==";
        ProcessStartInfo psi = ShellLauncher.BuildDirectPowerShellPsi("pwsh.exe", encoded);

        StringAssert.Contains(psi.Arguments, "-NoExit");
        StringAssert.Contains(psi.Arguments, "-EncodedCommand");
        StringAssert.Contains(psi.Arguments, encoded);
    }

    // -----------------------------------------------------------------------
    // Diagnostic test — prints environment state without launching anything
    // Run: dotnet test --filter "TestCategory=Diagnostic.WT"
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("Diagnostic.WT")]
    public void DiagnoseWindowsTerminalEnvironment()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-only diagnostic.");
        }

        StringBuilder sb = new();
        sb.AppendLine("=== Windows Terminal Launch Diagnostics ===");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");

        bool isDefault = ShellLauncher.ProbeIsWindowsTerminalDefault();
        sb.AppendLine($"IsWindowsTerminalDefault : {isDefault}");

        string? wtExe = ShellLauncher.ProbeFindWindowsTerminalExecutable();
        sb.AppendLine($"FindWindowsTerminalExe   : {wtExe ?? "(not found)"}");

        string psName = ShellLauncher.ProbeFindSafePowerShellName();
        sb.AppendLine($"FindSafePowerShellName   : {psName}");

        string encoded = Convert.ToBase64String(
            Encoding.Unicode.GetBytes("Write-Host 'ShellLauncher diagnostic OK'"));

        if (wtExe != null)
        {
            ProcessStartInfo psi = ShellLauncher.BuildWindowsTerminalPsi(wtExe, psName, encoded);
            sb.AppendLine();
            sb.AppendLine($"ProcessStartInfo.FileName: {psi.FileName}");
            sb.AppendLine($"UseShellExecute          : {psi.UseShellExecute}");
            for (int i = 0; i < psi.ArgumentList.Count; i++)
            {
                sb.AppendLine($"  ArgumentList[{i}]        : {psi.ArgumentList[i]}");
            }
        }
        else
        {
            ProcessStartInfo psi = ShellLauncher.BuildDirectPowerShellPsi(psName, encoded);
            sb.AppendLine("\n(wt.exe not found — would use direct launch)");
            sb.AppendLine($"ProcessStartInfo.FileName: {psi.FileName}");
            sb.AppendLine($"Arguments                : {psi.Arguments}");
        }

        // Use Inconclusive so the message is visible in the test output summary.
        Assert.Inconclusive(sb.ToString());
    }

    // -----------------------------------------------------------------------
    // Integration tests — actually launch, verify via sentinel file
    // Run: dotnet test --filter "TestCategory=Integration.WT"
    // Each test opens a terminal tab — it will close itself after writing the
    // sentinel file (no -NoExit).
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("Integration.WT")]
    [Ignore("Disabled: launching wt.exe via CreateProcess triggers a UAC prompt on some machines. " +
            "Run manually and dismiss the prompt if needed.")]
    public void LaunchViaWindowsTerminal_CommandRunsSuccessfully()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-only integration test.");
        }

        if (!ShellLauncher.ProbeIsWindowsTerminalDefault())
        {
            Assert.Inconclusive("Windows Terminal is not configured as the default terminal on this machine.\n" +
                                "Set it in: Settings → System → For developers → Terminal → Windows Terminal.");
        }

        string? wtExe = ShellLauncher.ProbeFindWindowsTerminalExecutable();
        if (wtExe == null)
        {
            Assert.Inconclusive("wt.exe was not found on PATH or in %LOCALAPPDATA%\\Microsoft\\WindowsApps.");
        }

        string psExe = ShellLauncher.ProbeFindSafePowerShellName(); // pwsh.exe if available

        // Write a sentinel file at a plain (no-spaces) temp path.
        string sentinelPath = Path.Combine(
            Path.GetTempPath(), $"wt-launch-test-{Guid.NewGuid():N}.txt");
        string command = $"[System.IO.File]::WriteAllText('{sentinelPath}', 'ok')";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        // Build without -NoExit so the shell exits after writing the file.
        // Use --commandline so wt.exe receives the shell invocation as a single option
        // value and passes it verbatim to CreateProcess, rather than joining all positional
        // args into a quoted block (which causes ERROR_FILE_NOT_FOUND 0x80070002).
        ProcessStartInfo psi = new() { FileName = wtExe, UseShellExecute = false };
        psi.ArgumentList.Add("new-tab");
        psi.ArgumentList.Add("--commandline");
        psi.ArgumentList.Add($"{psExe} -EncodedCommand {encoded}"); // no -NoExit

        Process.Start(psi);

        // Poll for the sentinel file (wt.exe exits immediately; shell runs async)
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(sentinelPath) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(500);
        }

        bool created = File.Exists(sentinelPath);
        try
        {
            if (created)
            {
                File.Delete(sentinelPath);
            }
        }
        catch
        {
            /* best-effort */
        }

        Assert.IsTrue(created,
            $"Shell did not create the sentinel file within 20 s.\n" +
            $"  wt.exe  : {wtExe}\n" +
            $"  psExe   : {psExe}\n" +
            $"  sentinel: {sentinelPath}\n" +
            $"  This means either wt.exe failed to start {psExe}, or it crashed before " +
            $"executing the command.");
    }

    [TestMethod]
    [TestCategory("Integration.WT")]
    [Ignore("Disabled: launching wt.exe via CreateProcess triggers a UAC prompt on some machines. " +
            "Run manually and dismiss the prompt if needed.")]
    public void LaunchViaWindowsTerminal_CommandWithSpacesInPath_RunsSuccessfully()
    {
        // Verifies that spaces in the PowerShell command content (here: the sentinel file
        // path) do not break the launch.  The command is base64-encoded via -EncodedCommand
        // so the spaces never appear in the --commandline value; this test confirms the full
        // round-trip works even when the command payload contains spaces.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-only integration test.");
        }

        if (!ShellLauncher.ProbeIsWindowsTerminalDefault())
        {
            Assert.Inconclusive("Windows Terminal is not configured as the default terminal on this machine.");
        }

        string? wtExe = ShellLauncher.ProbeFindWindowsTerminalExecutable();
        if (wtExe == null)
        {
            Assert.Inconclusive("wt.exe was not found.");
        }

        string psExe = ShellLauncher.ProbeFindSafePowerShellName();

        // Create a sentinel file path that deliberately contains spaces.
        string spaceDir = Path.Combine(Path.GetTempPath(), "wt launch test spaces");
        Directory.CreateDirectory(spaceDir);
        string sentinelPath = Path.Combine(spaceDir, $"sentinel {Guid.NewGuid():N}.txt");

        string command = $"[System.IO.File]::WriteAllText('{sentinelPath}', 'ok')";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        ProcessStartInfo psi = new() { FileName = wtExe, UseShellExecute = false };
        psi.ArgumentList.Add("new-tab");
        psi.ArgumentList.Add("--commandline");
        psi.ArgumentList.Add($"{psExe} -EncodedCommand {encoded}"); // no -NoExit

        Process.Start(psi);

        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(sentinelPath) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(500);
        }

        bool created = File.Exists(sentinelPath);
        try
        {
            if (created)
            {
                File.Delete(sentinelPath);
            }
        }
        catch
        {
            /* best-effort */
        }

        try
        {
            Directory.Delete(spaceDir);
        }
        catch
        {
            /* best-effort */
        }

        Assert.IsTrue(created,
            $"Shell did not create the spaces-in-path sentinel file within 20 s.\n" +
            $"  wt.exe  : {wtExe}\n" +
            $"  psExe   : {psExe}\n" +
            $"  sentinel: {sentinelPath}");
    }

    [TestMethod]
    [TestCategory("Integration.WT")]
    public void LaunchDirectPowerShell_CommandRunsSuccessfully()
    {
        // Tests the direct (no wt.exe) path by launching the shell ourselves
        // and waiting for it to exit.  More reliable than the WT variant because
        // we can wait on the child process directly.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-only integration test.");
        }

        string psExe = ShellLauncher.ProbeFindSafePowerShellName(); // pwsh.exe if available

        string sentinelPath = Path.Combine(
            Path.GetTempPath(), $"direct-ps-test-{Guid.NewGuid():N}.txt");
        string command = $"[System.IO.File]::WriteAllText('{sentinelPath}', 'ok')";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        // Use ShellExecute=false so we can wait for the process to exit.
        ProcessStartInfo psi = new()
        {
            FileName = psExe,
            Arguments = $"-EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process proc = Process.Start(psi)
                             ?? throw new InvalidOperationException("Process.Start returned null.");

        bool exited = proc.WaitForExit(10_000);
        Assert.IsTrue(exited, $"{psExe} did not exit within 10 s.");
        Assert.AreEqual(0, proc.ExitCode, $"{psExe} exited with code {proc.ExitCode}.");

        bool created = File.Exists(sentinelPath);
        try
        {
            if (created)
            {
                File.Delete(sentinelPath);
            }
        }
        catch
        {
            /* best-effort */
        }

        Assert.IsTrue(created,
            $"Sentinel file was not created even though {psExe} exited 0.\n" +
            $"Encoded command was: {encoded}");
    }
}