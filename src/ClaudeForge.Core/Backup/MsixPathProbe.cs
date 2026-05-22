using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Windows-only helper that detects MSIX-virtualised Claude Desktop installs and
/// offers to remediate them by creating an NTFS junction from the standard
/// <c>%APPDATA%\Claude</c> path to the MSIX <c>LocalCache\Roaming\Claude</c> path.
/// </summary>
/// <remarks>
/// <para>
/// **Why this exists.** MSIX apps write to a virtualised per-package AppData path,
/// but Claude Desktop's "Edit Config" button still opens the un-virtualised
/// <c>%APPDATA%\Claude</c> — so edits made there are silently ignored. A junction
/// fixes both that button and every external tool that reads the standard path.
/// </para>
/// <para>
/// **Why a junction, not a symlink.** Directory symlinks on Windows require Developer
/// Mode or admin. NTFS junctions require neither — and every process (including the
/// MSIX sandbox) transparently resolves them.
/// </para>
/// <para>
/// All non-Windows callers get empty / <c>false</c> results; calling
/// <see cref="CreateJunctionAsync"/> on non-Windows returns a failure result with
/// an explanatory message.
/// </para>
/// </remarks>
public sealed class MsixPathProbe
{
    /// <summary>Shared instance.</summary>
    public static readonly MsixPathProbe Instance = new();

    /// <summary>
    /// Scans <c>%LOCALAPPDATA%\Packages</c> for a <c>Claude_*</c> package folder.
    /// Returns the virtualised Claude Desktop directory if one exists.
    /// </summary>
    /// <returns>
    /// Absolute path like <c>C:\Users\me\AppData\Local\Packages\Claude_{pubid}\LocalCache\Roaming\Claude</c>,
    /// or <c>null</c> if no MSIX install is found or we are not on Windows.
    /// </returns>
    public string? FindVirtualisedPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string packages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages");
        if (!Directory.Exists(packages))
        {
            return null;
        }

        try
        {
            string? match = Directory.EnumerateDirectories(packages, "Claude_*", SearchOption.TopDirectoryOnly)
                                     .FirstOrDefault();
            if (match == null)
            {
                return null;
            }

            string candidate = Path.Combine(match, "LocalCache", "Roaming", "Claude");
            return Directory.Exists(candidate) ? candidate : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Standard (un-virtualised) <c>%APPDATA%\Claude</c> path.</summary>
    public static string StandardAppDataPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Claude");

    /// <summary>
    /// True when <paramref name="path"/> exists and has the <c>ReparsePoint</c> attribute,
    /// i.e. it is a junction or symlink. Returns <c>false</c> for regular folders and for
    /// non-existent paths.
    /// </summary>
    public static bool IsReparsePoint(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            return (new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Summary of the current MSIX state for the UI. <see cref="NeedsFix"/> is the
    /// signal to show the MSIX tab.
    /// </summary>
    public MsixStatus Probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new MsixStatus(false, null, StandardAppDataPath, false, false);
        }

        string? virtualised = FindVirtualisedPath();
        string stdPath = StandardAppDataPath;
        bool stdExists = Directory.Exists(stdPath);
        bool isJunction = IsReparsePoint(stdPath);

        // "Needs fix" = MSIX install exists AND the standard path is NOT already a junction.
        bool needsFix = virtualised != null && !isJunction;

        return new MsixStatus(
            HasMsixInstall: virtualised != null,
            VirtualisedPath: virtualised,
            StandardPath: stdPath,
            IsJunctioned: isJunction,
            NeedsFix: needsFix);
    }

    /// <summary>
    /// Attempts to create the junction. If a real folder already lives at
    /// <see cref="StandardAppDataPath"/>, its contents are merged into the MSIX path
    /// first (files that already exist in MSIX win — MSIX is the authoritative version
    /// since that is the one Claude Desktop is actually reading).
    /// </summary>
    /// <remarks>
    /// Uses <c>mklink /J</c> via <c>cmd.exe</c>. Junctions do **not** require admin
    /// rights or Developer Mode, so this should succeed for ordinary users. A non-zero
    /// exit code is surfaced as a failure result.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public async Task<MsixFixResult> CreateJunctionAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new MsixFixResult(false, "MSIX fix is only applicable on Windows.");
        }

        MsixStatus status = Probe();
        if (!status.HasMsixInstall)
        {
            return new MsixFixResult(false, "No MSIX Claude Desktop install detected.");
        }

        if (status.IsJunctioned)
        {
            return new MsixFixResult(true, "A junction is already in place — nothing to do.");
        }

        string std = status.StandardPath!;
        string msix = status.VirtualisedPath!;

        // Cmd metacharacter injection defence: the `mklink` call below passes these
        // paths to cmd.exe as quoted arguments inside a /c string.  A path that
        // contains cmd.exe metacharacters — '"', '&', '|', '<', '>', '^', '%', '!',
        // ';', ',', '(', ')' — could break the quoting or inject additional commands.
        // Windows forbids '"' in path components but a crafted MSIX package name could
        // slip in other metacharacters (e.g. "Claude_abc&calc.exe"), so we validate
        // the entire metacharacter set rather than just '"'.
        // Note: ArgumentList cannot be used here because cmd.exe /c takes the rest of
        // the command line as a single string that it parses internally; the OS-level
        // argument split happens *before* cmd.exe runs, so splitting into ArgumentList
        // does not bypass cmd's own parser.
        if (HasCmdMetachars(std) || HasCmdMetachars(msix))
        {
            return new MsixFixResult(false,
                "Junction creation aborted: one of the resolved paths contains a cmd.exe metacharacter " +
                "that would break the mklink command line. Please raise a bug report.");
        }

        // 1) Merge any files that exist at the standard path into the MSIX path
        //    (skip conflicts — MSIX wins).
        if (Directory.Exists(std))
        {
            try
            {
                MergeContents(std, msix);
                Directory.Delete(std, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new MsixFixResult(false,
                    $"Could not merge contents from {std} into {msix}: {ex.Message}");
            }
        }

        // 2) Create the junction via cmd /c mklink /J.
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{std}\" \"{msix}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using Process? proc = Process.Start(psi);
            if (proc == null)
            {
                return new MsixFixResult(false, "Could not launch cmd.exe to create the junction.");
            }

            await proc.WaitForExitAsync(ct).ConfigureAwait(false);

            if (proc.ExitCode != 0)
            {
                string stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                return new MsixFixResult(false,
                    $"mklink exited with code {proc.ExitCode}. {stderr.Trim()}");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new MsixFixResult(false, $"Failed to invoke mklink: {ex.Message}");
        }

        if (!IsReparsePoint(std))
        {
            return new MsixFixResult(false,
                "Junction creation appeared to succeed but the standard path is still a real folder.");
        }

        return new MsixFixResult(true,
            $"Junction created: {std} → {msix}. Restart Claude Desktop for the change to take effect.");
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="path"/> contains any character that
    /// cmd.exe treats as a metacharacter when parsing a <c>/c</c> command string.
    /// Windows itself forbids <c>"</c> in path components, but a crafted MSIX
    /// package name could embed other metacharacters (e.g. <c>&amp;</c>), so we
    /// validate the full set rather than just the quote character.
    /// </summary>
    private static bool HasCmdMetachars(string path)
    {
        return path.Any(c => c is '"' or '&' or '|' or '<' or '>' or '^' or '%' or '!' or ';' or ',' or '(' or ')');
    }

    /// <summary>
    /// Copies every file and subdirectory from <paramref name="source"/> into
    /// <paramref name="destination"/> but **only** where the destination doesn't
    /// already have a copy — MSIX wins on conflict.
    /// </summary>
    private static void MergeContents(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            string dst = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(dst))
            {
                File.Copy(file, dst, overwrite: false);
            }
        }

        foreach (string subDir in Directory.EnumerateDirectories(source))
        {
            string subName = Path.GetFileName(subDir);
            string dstSub = Path.Combine(destination, subName);
            MergeContents(subDir, dstSub);
        }
    }
}

/// <summary>Result of <see cref="MsixPathProbe.Probe"/>.</summary>
public sealed record MsixStatus(
    bool HasMsixInstall,
    string? VirtualisedPath,
    string StandardPath,
    bool IsJunctioned,
    bool NeedsFix);

/// <summary>Outcome of <see cref="MsixPathProbe.CreateJunctionAsync"/>.</summary>
public sealed record MsixFixResult(bool Succeeded, string Message);