using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Bennewitz.Ninja.ClaudeForge.Core.Platform;

/// <summary>
/// Best-effort probes for the installed Claude Desktop version. Tries the
/// strategies in order of reliability:
/// <list type="number">
///   <item><description>Windows Uninstall Registry (classic MSI / Squirrel installs)</description></item>
///   <item><description><c>FileVersionInfo</c> on the <c>Claude.exe</c> binary</description></item>
///   <item><description>MSIX/Store <c>WindowsApps\Claude_&lt;version&gt;_&lt;arch&gt;__&lt;hash&gt;</c> folder name
///     (the current Claude Desktop distribution channel — neither the registry nor the
///     <c>%LOCALAPPDATA%</c> paths are populated for MSIX installs)</description></item>
///   <item><description>Squirrel <c>app-*</c> directory naming convention (legacy)</description></item>
/// </list>
/// The first non-null result wins; composition order is documented on
/// <see cref="TryGetVersion"/>.
/// </summary>
/// <remarks>
/// The registry and file-system probes are guarded behind
/// <see cref="OperatingSystem.IsWindows"/> so this file is safe to compile and
/// run on macOS/Linux; on those platforms <see cref="TryGetVersion"/> returns
/// <c>null</c> and the caller falls back to its existing plist-based logic.
/// </remarks>
public static partial class ClaudeDesktopVersionProbe
{
    // process-lifetime cache for the composed version probe.
    // TryGetVersionFromWindowsApps in particular is slow — it enumerates
    // %ProgramFiles%\WindowsApps\Claude_* (which often throws
    // UnauthorizedAccessException on neighbour subdirs). The composed
    // TryGetVersion was flagged as a hot path by Rider's monitoring tab on
    // profile switch because every BuildNavigationTree rebuild re-constructs
    // AboutEditorViewModel for Desktop, which calls TryGetVersion eagerly.
    //
    // Install state rarely changes mid-session — Claude Desktop installs
    // require a download + restart that the user is unlikely to perform with
    // ClaudeForge open. We document the trade-off: if a user installs or
    // upgrades Desktop while this app is open, the displayed version will
    // remain stale until the next launch (or until ResetCache() is called,
    // currently only by tests). Acceptable: the About page already does not
    // refresh while open.
    private static volatile bool _versionCacheValid;
    private static string? _versionCache;

    /// <summary>
    /// Discards the cached result of <see cref="TryGetVersion"/>. Intended for
    /// tests; production code does not invalidate this cache during a session.
    /// </summary>
    public static void ResetCache()
    {
        _versionCache = null;
        _versionCacheValid = false;
    }

    /// <summary>
    /// Compose the registry, file, and Squirrel probes in priority order.
    /// Returns the first non-null version string, or <c>null</c> if none hit.
    /// Result is memoised for the lifetime of the process — see <see cref="ResetCache"/>.
    /// </summary>
    public static string? TryGetVersion()
    {
        if (_versionCacheValid)
        {
            return _versionCache;
        }

        string? result = TryGetVersionUncached();
        _versionCache = result;
        _versionCacheValid = true;
        return result;
    }

    private static string? TryGetVersionUncached()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        // Each probe is independently fallible — registry keys may be missing, files may
        // be locked, MSIX permissions may deny enumeration. We swallow only the expected
        // exception families (IO / unauthorized / Win32 / security / argument parsing
        // failures from malformed registry data); anything else (OutOfMemoryException,
        // StackOverflowException, etc.) propagates so unexpected bugs don't get hidden.
        // This matches the project-wide "no bare catch" convention in CLAUDE.md.
        try
        {
            if (TryGetVersionFromRegistry() is { } fromRegistry)
            {
                return fromRegistry;
            }
        }
        catch (Exception ex) when (IsExpectedProbeException(ex))
        {
        }

        try
        {
            if (TryGetVersionFromExe() is { } fromExe)
            {
                return fromExe;
            }
        }
        catch (Exception ex) when (IsExpectedProbeException(ex))
        {
        }

        try
        {
            if (TryGetVersionFromWindowsApps() is { } fromMsix)
            {
                return fromMsix;
            }
        }
        catch (Exception ex) when (IsExpectedProbeException(ex))
        {
        }

        try
        {
            if (TryGetVersionFromAppModelRegistry() is { } fromAppModel)
            {
                return fromAppModel;
            }
        }
        catch (Exception ex) when (IsExpectedProbeException(ex))
        {
        }

        try
        {
            if (TryGetVersionFromSquirrel() is { } fromSquirrel)
            {
                return fromSquirrel;
            }
        }
        catch (Exception ex) when (IsExpectedProbeException(ex))
        {
        }

        return null;
    }

    /// <summary>
    /// Recognise the exception types each version probe can legitimately throw so the
    /// catch filter in <see cref="TryGetVersion"/> stays narrow. Lets non-probe failures
    /// (OOM, stack overflow, AccessViolation) surface to the caller instead of silently
    /// returning null.
    /// </summary>
    private static bool IsExpectedProbeException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or SecurityException
            or Win32Exception
            or ArgumentException
            or FormatException
            or InvalidOperationException;
    }

    /// <summary>
    /// Extract the version from the MSIX/Store package folder under
    /// <c>%ProgramFiles%\WindowsApps</c>. The folder name format is
    /// <c>Claude_&lt;version&gt;_&lt;arch&gt;__&lt;publisherHash&gt;</c>, e.g.
    /// <c>Claude_1.3109.0.0_x64__pzs8sxrjxfjjc</c>.
    /// </summary>
    /// <remarks>
    /// Standard users can enumerate directory names under <c>WindowsApps</c>
    /// (content reads are blocked without elevation) — the version is encoded
    /// in the name itself, so no elevation is required.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static string? TryGetVersionFromWindowsApps()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrEmpty(programFiles))
        {
            return null;
        }

        string windowsApps = Path.Combine(programFiles, "WindowsApps");
        if (!Directory.Exists(windowsApps))
        {
            return null;
        }

        List<string> entries;
        try
        {
            // Force evaluation inside the try so UnauthorizedException is caught here.
            // EnumerateDirectories is lazy — the exception fires during iteration, not
            // during the call, so we must materialise the list before leaving the block.
            entries = Directory.EnumerateDirectories(windowsApps, "Claude_*")
                               .Select(Path.GetFileName)
                               .Where(n => n is not null)
                               .Select(n => n!)
                               .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        return TryExtractWindowsAppsVersion(entries);
    }

    /// <summary>
    /// Pure helper — given a sequence of <c>WindowsApps</c> package folder names,
    /// return the highest four-part version encoded in any matching name, or
    /// <c>null</c> if none match. Factored out of <see cref="TryGetVersionFromWindowsApps"/>
    /// so the parsing can be unit-tested without touching the filesystem.
    /// </summary>
    public static string? TryExtractWindowsAppsVersion(IEnumerable<string> folderNames)
    {
        Regex rx = MyRegex();
        Version? best = null;
        foreach (var name in folderNames)
        {
            if (name is null)
            {
                continue;
            }

            Match m = rx.Match(name);
            if (!m.Success)
            {
                continue;
            }

            if (!Version.TryParse(m.Groups[1].Value, out Version? v))
            {
                continue;
            }

            if (best is null || v > best)
            {
                best = v;
            }
        }

        return best?.ToString();
    }

    /// <summary>
    /// Scan HKCU and HKLM Uninstall keys for an entry whose <c>DisplayName</c>
    /// mentions Claude, returning its <c>DisplayVersion</c>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string? TryGetVersionFromRegistry()
    {
        // Squirrel and MSI-based Windows installers both write an Uninstall key.
        // Enumerate HKCU first (per-user installs) then HKLM (system-wide and 32-bit).
        RegistryRoot[] roots =
        [
            new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
            new(RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
            new(RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        ];

        foreach (RegistryRoot root in roots)
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(root.Hive, RegistryView.Default);
            using RegistryKey? uninstall = baseKey.OpenSubKey(root.SubKey);
            if (uninstall is null)
            {
                continue;
            }

            foreach (string name in uninstall.GetSubKeyNames())
            {
                using RegistryKey? entry = uninstall.OpenSubKey(name);
                if (entry is null)
                {
                    continue;
                }

                string? display = entry.GetValue("DisplayName") as string;
                if (string.IsNullOrEmpty(display))
                {
                    continue;
                }

                if (!display.Contains("Claude", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? version = entry.GetValue("DisplayVersion") as string;
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version.Trim();
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resolve the product version by loading <c>FileVersionInfo</c> of
    /// <c>Claude.exe</c> under the common install roots.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string? TryGetVersionFromExe()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        ReadOnlySpan<string> candidateRoots =
        [
            Path.Combine(localAppData, "AnthropicClaude"),
            Path.Combine(localAppData, "Programs", "claude-desktop"),
            Path.Combine(programFiles, "Claude"),
        ];

        foreach (string root in candidateRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            // Squirrel installs the executable inside app-<version>\Claude.exe; a plain
            // MSI install puts it directly under the root. Enumerate recursively but cap
            // the depth so a pathological deep tree cannot stall startup.
            string? exe = FindFileShallow(root, "Claude.exe", maxDepth: 3);
            if (exe is null)
            {
                continue;
            }

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(exe);
            if (!string.IsNullOrWhiteSpace(info.ProductVersion))
            {
                return info.ProductVersion!.Trim();
            }

            if (!string.IsNullOrWhiteSpace(info.FileVersion))
            {
                return info.FileVersion!.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Enumerate the AppModel package repository in the per-user registry. For MSIX
    /// packages the subkey names under this hive match the WindowsApps folder naming
    /// convention (<c>Claude_&lt;version&gt;_&lt;arch&gt;__&lt;publisherHash&gt;</c>)
    /// and are readable without elevation.
    /// </summary>
    /// <remarks>
    /// Key: <c>HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\
    /// CurrentVersion\AppModel\Repository\Packages</c><br/>
    /// This is the "software class" hive rather than the regular HKCU software hive,
    /// but it is writable/readable by the current user — no elevation required.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static string? TryGetVersionFromAppModelRegistry()
    {
        const string subKey =
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

        using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
        using RegistryKey? packages = baseKey.OpenSubKey(subKey);
        if (packages is null)
        {
            return null;
        }

        IEnumerable<string> names = packages.GetSubKeyNames()
                                            .Where(n => n.StartsWith("Claude_", StringComparison.OrdinalIgnoreCase));
        return TryExtractWindowsAppsVersion(names);
    }

    /// <summary>
    /// Original Squirrel-folder-naming probe kept as a last-resort fallback.
    /// </summary>
    public static string? TryGetVersionFromSquirrel()
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
                                        .OrderByDescending(n => n, StringComparer.Ordinal)
                                        .ToList();
        if (appDirs.Count == 0)
        {
            return null;
        }

        Match match = Regex.Match(appDirs[0], @"app-(\d+\.\d+\.\d+.*)");
        return match.Success ? match.Groups[1].Value : appDirs[0];
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Shallow BFS for <paramref name="fileName"/> under <paramref name="root"/>,
    /// bounded by <paramref name="maxDepth"/>. Returns the first match, or <c>null</c>.
    /// </summary>
    private static string? FindFileShallow(string root, string fileName, int maxDepth)
    {
        Queue<DirectoryEntry> queue = new();
        queue.Enqueue(new DirectoryEntry(root, 0));

        while (queue.Count > 0)
        {
            DirectoryEntry entry = queue.Dequeue();
            string dir = entry.Path;
            int depth = entry.Depth;
            string[] files;
            string[] subDirs;
            try
            {
                files = Directory.GetFiles(dir, fileName);
                subDirs = depth < maxDepth ? Directory.GetDirectories(dir) : [];
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            if (files.Length > 0)
            {
                return files[0];
            }

            foreach (string sub in subDirs)
            {
                queue.Enqueue(new DirectoryEntry(sub, depth + 1));
            }
        }

        return null;
    }

    // ── Companion records ────────────────────────────────────────────────────

    private sealed record RegistryRoot(RegistryHive Hive, string SubKey);

    private sealed record DirectoryEntry(string Path, int Depth);

    [GeneratedRegex(@"^Claude_(\d+\.\d+\.\d+\.\d+)_", RegexOptions.CultureInvariant)]
    private static partial Regex MyRegex();
}