using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Core.Platform;

/// <summary>Probes installed product versions without blocking the UI thread.</summary>
public static partial class ProductVersionProbe
{
    /// <summary>
    /// Try to get the Claude Code CLI version string, e.g. "1.2.3". When
    /// <paramref name="explicitBinaryPath"/> is provided, the probe runs that
    /// path directly (cmd.exe-wrapped on Windows for <c>.cmd</c>/<c>.bat</c>
    /// shims, which cannot be launched through <see cref="ProcessStartInfo"/>
    /// with <c>UseShellExecute=false</c> on their own). When it is
    /// <see langword="null"/>, the probe falls back to <c>"claude"</c> as a
    /// bare PATH lookup (the legacy behaviour).
    /// </summary>
    public static async Task<string?> TryGetClaudeCodeVersionAsync(string? explicitBinaryPath = null)
    {
        ResolvedCommand cmd = ResolveCommand(explicitBinaryPath);
        try
        {
            ProcessResult run = await RunWithTimeoutAsync(cmd.Exe, cmd.Args, 2000);
            if (run.ExitCode == 0 && !string.IsNullOrWhiteSpace(run.Stdout))
            {
                return run.Stdout.Trim();
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException
                                       or OperationCanceledException or TaskCanceledException
                                       or IOException)
        {
            // Expected: claude not on PATH, process failed to start, or timed out.
            Debug.WriteLine($"[VersionProbe] claude --version: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Builds the <c>(exe, args)</c> pair needed to run <c>claude --version</c>.
    /// On Windows, <c>.cmd</c> / <c>.bat</c> / <c>.ps1</c> shims cannot be
    /// launched directly by <see cref="Process.Start(ProcessStartInfo)"/> with
    /// <c>UseShellExecute=false</c> — they need an interpreter. We wrap them
    /// in <c>cmd.exe /c</c> (PowerShell shims fall back to <c>powershell -File</c>).
    /// On Unix, any resolved path is executed directly.
    /// </summary>
    internal static ResolvedCommand ResolveCommand(string? explicitBinaryPath)
    {
        const string versionFlag = "--version";
        if (string.IsNullOrWhiteSpace(explicitBinaryPath))
        {
            return new ResolvedCommand("claude", versionFlag);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string ext = Path.GetExtension(explicitBinaryPath);
            if (string.Equals(ext, ".cmd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase))
            {
                // `/s /c "cmd"` — the `/s` flag changes cmd.exe's quote-
                // stripping rule so the OUTER pair of quotes around the whole
                // command is removed verbatim rather than merged with any
                // inner quotes. This is the only robust way to pass a path
                // containing spaces AND cmd metacharacters (`& | < > ^ %`)
                // through without them being interpreted by the shell — see
                // `cmd /?` "If /C or /K is specified … and /S is not".
                // Our inputs come from a filesystem probe (not user text),
                // so injection risk is low, but this still costs nothing and
                // hardens us against exotic install-path naming.
                return new ResolvedCommand("cmd.exe", $"/s /c \"\"{explicitBinaryPath}\" {versionFlag}\"");
            }

            if (string.Equals(ext, ".ps1", StringComparison.OrdinalIgnoreCase))
            {
                return new ResolvedCommand("powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -File \"{explicitBinaryPath}\" {versionFlag}");
            }
        }

        return new ResolvedCommand(explicitBinaryPath, versionFlag);
    }

    /// <summary>Try to get the Claude Desktop version string.</summary>
    public static string? TryGetClaudeDesktopVersion()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetDesktopVersionWindows();
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return GetDesktopVersionMacOs();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or DirectoryNotFoundException)
        {
            // Expected: app not installed or directory not accessible.
            Debug.WriteLine($"[VersionProbe] Desktop version: {ex.Message}");
        }

        return null;
    }

    public static string GetPlatformInfo()
    {
        return $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), " +
               $".NET {RuntimeInformation.FrameworkDescription}";
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private static string? GetDesktopVersionWindows()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string claudeDir = Path.Combine(localAppData, "AnthropicClaude");
        if (!Directory.Exists(claudeDir))
        {
            return null;
        }

        List<string> appDirs = Directory.GetDirectories(claudeDir, "app-*")
                                        .Select(Path.GetFileName)
                                        .Where(n => n != null)
                                        .Select(n => n!)
                                        .OrderByDescending(n => n)
                                        .ToList();
        if (appDirs.Count == 0)
        {
            return null;
        }

        Match match = MyRegex().Match(appDirs[0]);
        return match.Success ? match.Groups[1].Value : appDirs[0];
    }

    private static string? GetDesktopVersionMacOs()
    {
        const string plistPath = "/Applications/Claude.app/Contents/Info.plist";
        if (!File.Exists(plistPath))
        {
            return null;
        }

        string content = File.ReadAllText(plistPath);
        Match match = Regex.Match(content,
            @"<key>CFBundleShortVersionString</key>\s*<string>([^<]+)</string>");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static async Task<ProcessResult> RunWithTimeoutAsync(
        string exe, string args, int timeoutMs)
    {
        using CancellationTokenSource cts = new(timeoutMs);
        ProcessStartInfo psi = new(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process proc = Process.Start(psi) ?? throw new InvalidOperationException("Process did not start");
        try
        {
            // Drain both streams concurrently.  Reading only stdout while stderr is also
            // redirected can deadlock if the process fills the stderr pipe buffer before
            // finishing — the process blocks on write, so ReadToEndAsync never returns.
            Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            Task<string> stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);
            string stdout = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false); // discard stderr, but drain it
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return new ProcessResult(proc.ExitCode, stdout);
        }
        finally
        {
            // Kill the process if it has not already exited — prevents zombie processes
            // when the cancellation token fires (timeout) before the child process finishes.
            if (!proc.HasExited)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[VersionProbe] Failed to kill timed-out process {Exe}", exe);
                }
            }

            proc.Dispose();
        }
    }

    // ── Companion records ────────────────────────────────────────────────────

    internal sealed record ResolvedCommand(string Exe, string Args);

    private sealed record ProcessResult(int ExitCode, string Stdout);

    [GeneratedRegex(@"app-(\d+\.\d+\.\d+.*)")]
    private static partial Regex MyRegex();
}