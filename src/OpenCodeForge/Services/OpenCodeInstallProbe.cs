using System.Diagnostics;
using System.Runtime.InteropServices;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Serilog;

namespace Bennewitz.Ninja.OpenCodeForge.Services;

/// <summary>Whether the agent is installed, and which version.</summary>
/// <param name="ExecutablePath">Where the binary was found, or <see langword="null"/> if it was not.</param>
/// <param name="Version">Reported version, or <see langword="null"/> when it could not be read.</param>
public sealed record OpenCodeInstallStatus(string? ExecutablePath, string? Version)
{
    /// <summary>True when a binary was found.</summary>
    public bool IsInstalled => ExecutablePath is not null;

    /// <summary>Nothing found.</summary>
    public static OpenCodeInstallStatus NotFound { get; } = new(null, null);
}

/// <summary>
/// Finds the agent's executable and asks it for its version.
/// </summary>
/// <remarks>
/// <para>
/// Detection exists so the app can be honest. Without it, an editor happily presents settings
/// pages for a tool that is not installed, and the user's first sign of trouble is that nothing
/// they configure has any effect.
/// </para>
/// <para>
/// ⚠ <b>Absence is reported, never asserted as "not installed anywhere".</b> A binary outside
/// PATH and outside the known locations is invisible to this probe, so the banner says the agent
/// was not detected rather than that it is missing.
/// </para>
/// </remarks>
public static class OpenCodeInstallProbe
{
    private const string BinaryName = "opencode";

    /// <summary>How long the version query may take before it is abandoned.</summary>
    /// <remarks>
    /// A short budget on purpose: this runs during startup, and a hung child process must not be
    /// able to delay the window. Failing to read a version is cosmetic; blocking the app is not.
    /// </remarks>
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Locate the executable, or return <see langword="null"/>.</summary>
    public static string? FindExecutable()
    {
        foreach (string candidate in CandidatePaths())
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable directory on PATH must not stop the search.
                _ = ex;
            }
        }

        return null;
    }

    /// <summary>Every place worth looking, in priority order.</summary>
    /// <remarks>
    /// PATH first, because that is the install the user's shell would actually run — finding a
    /// different copy elsewhere and reporting its version would be worse than finding nothing.
    /// </remarks>
    private static IEnumerable<string> CandidatePaths()
    {
        bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string[] names = windows
            ? [BinaryName + ".exe", BinaryName + ".cmd", BinaryName]
            : [BinaryName];

        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVar))
        {
            foreach (string dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string name in names)
                {
                    yield return Path.Combine(dir.Trim(), name);
                }
            }
        }

        string home = PlatformPaths.UserProfile;

        if (windows)
        {
            // ⚠ npm-global on Windows. S10 found the maintainer's own install here and noted the
            // plan's probe list omitted it — an install this probe cannot see reads to the user
            // as "your detection is broken", not as "my install is unusual".
            yield return @"C:\Program Files\nodejs\opencode.cmd";
            yield return @"C:\Program Files\nodejs\opencode";
            yield return Path.Combine(home, "AppData", "Roaming", "npm", "opencode.cmd");
        }
        else
        {
            yield return "/usr/local/bin/" + BinaryName;
            yield return "/usr/bin/" + BinaryName;
            yield return "/opt/homebrew/bin/" + BinaryName;          // Apple silicon Homebrew
            yield return Path.Combine(home, ".local", "bin", BinaryName);
            yield return Path.Combine(home, ".opencode", "bin", BinaryName);
        }
    }

    /// <summary>Ask the located executable for its version.</summary>
    /// <remarks>
    /// Returns <see langword="null"/> on any failure — missing binary, non-zero exit, timeout, or
    /// output that does not look like a version. The caller shows "installed, version unknown",
    /// which is accurate and harmless.
    /// </remarks>
    public static async Task<string?> TryGetVersionAsync(
        string executablePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(executablePath);

        try
        {
            using Process process = new();
            process.StartInfo = new ProcessStartInfo(executablePath, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            process.Start();

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(VersionTimeout);

            string output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                Log.Debug("[Probe] '{Path} --version' exited {Code}", executablePath, process.ExitCode);
                return null;
            }

            // Take the first non-empty line: some builds print a banner after the version.
            string? line = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(line) ? null : line;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.Debug("[Probe] version query timed out after {Seconds}s", VersionTimeout.TotalSeconds);
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Log.Debug(ex, "[Probe] could not run '{Path} --version'", executablePath);
            return null;
        }
    }

    /// <summary>Find the agent and read its version in one pass.</summary>
    public static async Task<OpenCodeInstallStatus> DetectAsync(CancellationToken ct = default)
    {
        string? path = FindExecutable();
        if (path is null)
        {
            return OpenCodeInstallStatus.NotFound;
        }

        return new OpenCodeInstallStatus(path, await TryGetVersionAsync(path, ct).ConfigureAwait(false));
    }
}
