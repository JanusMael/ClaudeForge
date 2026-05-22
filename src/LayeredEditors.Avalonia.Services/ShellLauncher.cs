using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

/// <summary>
/// Default <see cref="IShellLauncher"/> implementation.
/// All platform-branching is contained here so callers remain portable.
/// </summary>
/// <remarks>
/// Use <see cref="Instance"/> for the shared singleton.  The class can also
/// be subclassed or replaced for testing via the interface.
/// </remarks>
public sealed class ShellLauncher : IShellLauncher
{
    /// <summary>Shared singleton; suitable for all callers that do not need DI.</summary>
    public static readonly ShellLauncher Instance = new();

    private ShellLauncher()
    {
    }

    // -----------------------------------------------------------------------
    // IShellLauncher — public surface
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public bool LaunchTerminalWithCommand(string command)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                LaunchWindowsTerminal(command);
                return true;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                LaunchMacTerminal(command);
                return true;
            }

            return LaunchLinuxTerminal(command);
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void RevealInFileManager(string filePath)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                RevealWindows(filePath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                RevealMac(filePath);
            }
            else
            {
                RevealLinux(filePath);
            }
        }
        catch
        {
            // Silently ignore — no meaningful fallback.
        }
    }

    /// <inheritdoc />
    public void OpenInDefaultEditor(string filePath)
    {
        if (!Path.IsPathRooted(filePath))
        {
            return;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true,
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                ProcessStartInfo psi = new() { FileName = "open", UseShellExecute = false };
                psi.ArgumentList.Add("-t");
                psi.ArgumentList.Add(filePath);
                Process.Start(psi);
            }
            else
            {
                ProcessStartInfo psi = new() { FileName = "xdg-open", UseShellExecute = false };
                psi.ArgumentList.Add(filePath);
                Process.Start(psi);
            }
        }
        catch
        {
            // Silently ignore — no meaningful fallback.
        }
    }

    // -----------------------------------------------------------------------
    // Terminal launch — Windows
    // -----------------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static void LaunchWindowsTerminal(string command)
    {
        // -EncodedCommand accepts Base64-encoded UTF-16LE, bypassing all quoting/injection
        // hazards that arise when embedding an arbitrary string inside -Command "...".
        // -NoExit keeps the window open after the user runs (or abandons) the command.
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        // Both paths use FindSafePowerShellName() — pwsh.exe when PowerShell 7 is on PATH,
        // otherwise powershell.exe (Windows PowerShell, always available in System32).
        //
        // wt.exe path: the shell name is embedded in a --commandline value, which wt.exe
        //   passes verbatim to CreateProcess as lpCommandLine.  CreateProcess searches PATH
        //   for the first space-delimited token ("pwsh.exe" or "powershell.exe") and uses
        //   the found path as lpApplicationName — that resolved path (even if it contains
        //   spaces) is never re-inserted into lpCommandLine, so quoting is not a concern.
        //   The earlier approach of passing separate positional argv tokens caused wt.exe to
        //   join them into one outer-quoted block → ERROR_FILE_NOT_FOUND (0x80070002).
        //
        // Direct path (no wt.exe): ProcessStartInfo.FileName is resolved by .NET / ShellExecute
        //   directly; it is never passed through wt.exe's command-line reconstruction.
        string psExe = FindSafePowerShellName();
        string? wtExe = IsWindowsTerminalDefault() ? FindWindowsTerminalExecutable() : null;
        if (wtExe != null)
        {
            Process.Start(BuildWindowsTerminalPsi(wtExe, psExe, encoded));
        }
        else
        {
            Process.Start(BuildDirectPowerShellPsi(psExe, encoded));
        }
    }

    // -----------------------------------------------------------------------
    // Terminal launch — macOS
    // -----------------------------------------------------------------------

    private static void LaunchMacTerminal(string command)
    {
        // Use osascript to open Terminal.app with the command pre-filled.
        // Use ArgumentList (not Arguments) so that .NET does not tokenise the AppleScript
        // string — a single-quoted shell argument has no special meaning to the .NET
        // argument tokeniser and would be passed verbatim, corrupting the script.
        //
        // SECURITY CONTRACT — `command` MUST come from a literal/constant source.
        // The escaping below only handles `"`. Backslashes, control characters, and
        // other AppleScript-meaningful sequences are passed through unmodified. The
        // current call sites (InstallCommandViewModel.ForClaudeCode etc.) pass only
        // hardcoded install snippets, so no live exploit exists; future callers must
        // not flow user-controlled or settings-file content into this argument
        // without first hardening the escaping (or ideally moving to a do-script
        // variable-binding form).
        string script = $"tell application \"Terminal\" to do script \"{command.Replace("\"", "\\\"")}\"";
        ProcessStartInfo psi = new()
        {
            FileName = "osascript",
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);
        Process.Start(psi);
    }

    // -----------------------------------------------------------------------
    // Terminal launch — Linux
    // -----------------------------------------------------------------------

    private static bool LaunchLinuxTerminal(string command)
    {
        // Different terminal emulators use different argument conventions to run a
        // command in the new terminal:
        //   gnome-terminal / xfce4-terminal  →  -- bash -c CMD
        //   xterm / konsole                  →  -e bash -c CMD
        //
        // We use ArgumentList (not the Arguments string) and pass the command as a
        // discrete argument to "bash -c", so the OS delivers it directly to bash via
        // execvp rather than through a shell that would interpret metacharacters such
        // as $(), backticks, |, and ; in the path.
        string bashCmd = $"{command}; exec bash";

        // WSL: Windows Terminal (wt.exe) is typically reachable via the Windows PATH
        // that WSL inherits.  "wt.exe wsl -- bash -c CMD" opens a new Windows Terminal
        // tab running bash inside the current WSL distro — the most natural "open a
        // terminal" experience for WSL users who have no Linux terminal emulator installed.
        if (IsRunningInWsl())
        {
            try
            {
                ProcessStartInfo psi = new() { FileName = "wt.exe", UseShellExecute = false };
                psi.ArgumentList.Add("wsl");
                psi.ArgumentList.Add("--");
                psi.ArgumentList.Add("bash");
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(bashCmd);
                Process.Start(psi);
                return true;
            }
            catch (Exception ex) when (ex is Win32Exception
                                           or InvalidOperationException
                                           or FileNotFoundException
                                           or PlatformNotSupportedException)
            {
                // wt.exe not found — fall through to the standard emulator list.
            }
        }

        foreach ((string termExe, string flag) in new[]
                 {
                     ("gnome-terminal", "--"), // Ubuntu GNOME / standard Ubuntu
                     ("cosmic-term", "--"), // System76 Cosmic desktop
                     ("xfce4-terminal", "--"), // Xfce
                     ("mate-terminal", "-e"), // MATE desktop
                     ("tilix", "-e"), // GNOME tiling terminal (popular on Ubuntu)
                     ("konsole", "-e"), // KDE
                     ("lxterminal", "-e"), // LXDE / LXQt
                     ("xterm", "-e"), // universal X11 fallback
                 })
        {
            try
            {
                ProcessStartInfo psi = new()
                {
                    FileName = termExe,
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add(flag);
                psi.ArgumentList.Add("bash");
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(bashCmd);
                Process.Start(psi);
                return true;
            }
            catch (Exception ex) when (ex is Win32Exception
                                           or InvalidOperationException
                                           or FileNotFoundException
                                           or PlatformNotSupportedException)
            {
                // This emulator is not installed — try the next one.
            }
        }

        return false; // no terminal emulator found
    }

    /// <summary>
    /// Returns <see langword="true"/> when the process is running inside
    /// Windows Subsystem for Linux (WSL 1 or WSL 2).
    /// </summary>
    private static bool IsRunningInWsl()
    {
        return Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") is not null;
    }

    // -----------------------------------------------------------------------
    // Reveal in file manager — Windows
    // -----------------------------------------------------------------------

    private static void RevealWindows(string filePath)
    {
        // /select, highlights the file in Explorer.
        // Note: the comma directly follows /select — no space.
        // UseShellExecute = false lets explorer.exe parse /select correctly without
        // the shell tokenising the path first, which is required for paths with spaces.
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{filePath}\"",
            UseShellExecute = false,
        });
    }

    // -----------------------------------------------------------------------
    // Reveal in file manager — macOS
    // -----------------------------------------------------------------------

    private static void RevealMac(string filePath)
    {
        // -R: "reveal" — opens Finder with the item selected.
        ProcessStartInfo psi = new() { FileName = "open", UseShellExecute = false };
        psi.ArgumentList.Add("-R");
        psi.ArgumentList.Add(filePath);
        Process.Start(psi);
    }

    // -----------------------------------------------------------------------
    // Reveal in file manager — Linux
    // -----------------------------------------------------------------------

    private static void RevealLinux(string filePath)
    {
        string dir = Path.GetDirectoryName(filePath) ?? filePath;

        // Try file managers that support single-file selection first, then fall back
        // to opening the containing directory via xdg-open.
        // Use ArgumentList (not the Arguments string) so paths with spaces, $(), or
        // other metacharacters are delivered verbatim to the process without shell
        // interpretation.
        foreach ((string fmExe, bool useSelect, string target) in new[]
                 {
                     ("nautilus", true, filePath),
                     ("dolphin", true, filePath),
                     ("nemo", false, filePath),
                     ("thunar", false, dir),
                     ("xdg-open", false, dir),
                 })
        {
            try
            {
                ProcessStartInfo psi = new()
                {
                    FileName = fmExe,
                    UseShellExecute = false,
                };
                if (useSelect)
                {
                    psi.ArgumentList.Add("--select");
                }

                psi.ArgumentList.Add(target);
                Process.Start(psi);
                return;
            }
            catch (Exception ex) when (ex is Win32Exception
                                           or InvalidOperationException
                                           or FileNotFoundException
                                           or PlatformNotSupportedException)
            {
                // This file manager is not installed — try the next one.
            }
        }
    }

    // -----------------------------------------------------------------------
    // Windows helpers
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // Internal helpers (exposed for unit / integration testing via InternalsVisibleTo)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build the <see cref="ProcessStartInfo"/> for opening a new Windows Terminal tab
    /// running the specified PowerShell executable with a Base64-encoded command.
    /// <para>
    /// The key insight: wt.exe's positional argument parser joins all post-subcommand tokens
    /// into one string and then wraps the whole thing in outer quotes before handing it to
    /// <c>CreateProcess</c> as <c>lpCommandLine</c>.  For example, passing
    /// <c>["new-tab", "pwsh.exe", "-NoExit", "-EncodedCommand", "…"]</c> causes wt.exe
    /// to call <c>CreateProcess</c> with <c>"pwsh.exe -NoExit -EncodedCommand …"</c> as
    /// the command line — that outer-quoted block is treated as one token (the executable name),
    /// which fails with <c>ERROR_FILE_NOT_FOUND</c> (0x80070002).
    /// </para>
    /// <para>
    /// Fix: use <c>--commandline</c> and pass the entire shell invocation as a single option
    /// value.  .NET's argv quoting wraps the value in outer quotes; wt.exe's option parser
    /// strips those outer quotes and receives the raw string
    /// <c>pwsh.exe -NoExit -EncodedCommand …</c>, which it passes verbatim to
    /// <c>CreateProcess</c>.  <c>CreateProcess</c> then correctly splits the first
    /// space-delimited token as the executable name and resolves it via PATH lookup —
    /// even if the resolved path contains spaces, it is passed as <c>lpApplicationName</c>
    /// (not re-inserted into <c>lpCommandLine</c>), so quoting is not a concern.
    /// </para>
    /// </summary>
    /// <param name="wtExe">Full path to <c>wt.exe</c>.</param>
    /// <param name="psExe">
    /// Bare shell name to run inside the new tab — e.g. <c>"pwsh.exe"</c> or
    /// <c>"powershell.exe"</c>.  Must not contain a directory component or spaces,
    /// as it becomes the first token of the <c>--commandline</c> value.
    /// </param>
    /// <param name="encodedCommand">Base64-encoded UTF-16LE PowerShell command.</param>
    internal static ProcessStartInfo BuildWindowsTerminalPsi(string wtExe, string psExe, string encodedCommand)
    {
        ProcessStartInfo psi = new() { FileName = wtExe, UseShellExecute = false };
        psi.ArgumentList.Add("new-tab");
        psi.ArgumentList.Add("--commandline");
        psi.ArgumentList.Add($"{psExe} -NoExit -EncodedCommand {encodedCommand}");
        return psi;
    }

    /// <summary>
    /// Build the <see cref="ProcessStartInfo"/> for launching PowerShell directly
    /// (used when Windows Terminal is not the default terminal).
    /// <see cref="ProcessStartInfo.FileName"/> is never passed through wt.exe's
    /// command-line reconstruction, so a full path with spaces is safe here.
    /// </summary>
    internal static ProcessStartInfo BuildDirectPowerShellPsi(string psExe, string encodedCommand)
    {
        return new ProcessStartInfo
        {
            FileName = psExe,
            Arguments = $"-NoExit -EncodedCommand {encodedCommand}",
            UseShellExecute = true,
        };
    }

    // Thin wrappers used by tests to inspect the detection state without
    // running through the full LaunchTerminalWithCommand path.
    [SupportedOSPlatform("windows")]
    internal static bool ProbeIsWindowsTerminalDefault()
    {
        return IsWindowsTerminalDefault();
    }

    [SupportedOSPlatform("windows")]
    internal static string? ProbeFindWindowsTerminalExecutable()
    {
        return FindWindowsTerminalExecutable();
    }

    [SupportedOSPlatform("windows")]
    internal static string ProbeFindSafePowerShellName()
    {
        return FindSafePowerShellName();
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns a PowerShell executable name that is safe to pass to wt.exe via
    /// <see cref="ProcessStartInfo.ArgumentList"/> — i.e. it contains no spaces.
    /// <para>
    /// Strategy: if <c>pwsh.exe</c> (PowerShell 7+) exists anywhere on <c>PATH</c>
    /// we return the bare name <c>"pwsh.exe"</c> (no directory component, no spaces)
    /// and let Windows Terminal / CreateProcess resolve it via PATH lookup.
    /// Otherwise we fall back to <c>"powershell.exe"</c> (Windows PowerShell, always
    /// present in System32, resolvable by name alone).
    /// </para>
    /// <para>
    /// We intentionally avoid returning a full install path such as
    /// <c>C:\Program Files\PowerShell\7\pwsh.exe</c>.  wt.exe does not re-quote
    /// tokens that contain spaces when it reconstructs the CreateProcess command line
    /// from its own argv, which causes ERROR_FILE_NOT_FOUND (0x80070002) for any
    /// space-containing path.
    /// </para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string FindSafePowerShellName()
    {
        // If pwsh.exe is on PATH, return just the name — no path, no spaces.
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (File.Exists(Path.Combine(dir.Trim(), "pwsh.exe")))
            {
                return "pwsh.exe";
            }
        }

        // Windows PowerShell is always available and is always resolvable by name alone.
        return "powershell.exe";
    }

    /// <summary>
    /// Returns <see langword="true"/> when the current user has configured Windows Terminal
    /// as their default terminal application.
    /// <para>
    /// Detection uses <c>HKCU\Console\%%Startup → DelegationTerminal</c>: when Windows
    /// Terminal is selected in Settings, Windows writes its CLSID
    /// <c>{E12CFF52-A866-4C77-9A90-F570A7AA2C6B}</c> to that value.
    /// Any registry access error returns <see langword="false"/> silently.
    /// </para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool IsWindowsTerminalDefault()
    {
        try
        {
            // The key is literally named "%%Startup" (two percent signs).
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Console\%%Startup");
            if (key == null)
            {
                return false;
            }

            const string wtTerminalGuid = "{E12CFF52-A866-4C77-9A90-F570A7AA2C6B}";
            string? val = key.GetValue("DelegationTerminal") as string;
            return string.Equals(val, wtTerminalGuid, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Permission denied, key missing, or any other I/O error → assume not default.
            return false;
        }
    }

    /// <summary>
    /// Returns the path to <c>wt.exe</c> (Windows Terminal) if it is available,
    /// or <see langword="null"/> when it cannot be located.
    /// <list type="number">
    ///   <item>Every directory on the current PATH environment variable.</item>
    ///   <item><c>%LOCALAPPDATA%\Microsoft\WindowsApps</c> — the canonical location
    ///         for the Store-installed Windows Terminal app-execution alias.</item>
    /// </list>
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string? FindWindowsTerminalExecutable()
    {
        // 1. PATH search — covers winget, scoop, and manual/portable installs.
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(dir.Trim(), "wt.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // 2. %LOCALAPPDATA%\Microsoft\WindowsApps — app-execution alias placed by the
        //    Microsoft Store version of Windows Terminal.
        string storeAlias = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "wt.exe");
        if (File.Exists(storeAlias))
        {
            return storeAlias;
        }

        return null;
    }
}