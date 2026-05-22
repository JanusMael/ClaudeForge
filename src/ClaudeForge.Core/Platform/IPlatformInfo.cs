namespace Bennewitz.Ninja.ClaudeForge.Core.Platform;

/// <summary>
/// Abstraction over OS-platform detection.
/// <para>
/// Production code resolves <see cref="PlatformInfo.Current"/> which by default
/// points at <see cref="RuntimePlatformInfo"/> (a thin wrapper around
/// <see cref="System.Runtime.InteropServices.RuntimeInformation"/>). Debug
/// builds can swap in <see cref="EmulatedPlatformInfo"/> via
/// <see cref="PlatformInfo.OverrideForDebug"/> — wired from
/// <c>DebugFlags.Initialize</c> when <c>--linux</c>, <c>--macos</c>, or
/// <c>--windows</c> is passed on the command line.
/// </para>
/// <para>
/// <strong>What this abstraction CAN emulate:</strong> UI surfaces that
/// branch on platform identity to format text or pick a path style — the
/// "Desktop config path" shown in the About page, the platform tag in the
/// backup manifest, command-string selection (osascript / explorer / xdg-open)
/// where the string is just a label, path-comparison case-sensitivity for
/// deduplication.
/// </para>
/// <para>
/// <strong>What this abstraction CANNOT emulate:</strong> platform-intrinsic
/// APIs that genuinely require the host OS — Windows registry access in
/// <c>ClaudeDesktopVersionProbe</c> and <c>EnvironmentEditorViewModel</c>,
/// MSIX junction creation in <c>MsixPathProbe</c>, and actual shell
/// execution. Those call sites continue to use <see cref="OperatingSystem.IsWindows"/>
/// directly because the underlying APIs do not exist outside their host OS;
/// emulating them would just produce harder-to-debug failures than the
/// honest "feature unavailable" path.
/// </para>
/// </summary>
public interface IPlatformInfo
{
    /// <summary>True when the (effective) platform is Windows.</summary>
    bool IsWindows { get; }

    /// <summary>True when the (effective) platform is macOS / OS X.</summary>
    bool IsMacOS { get; }

    /// <summary>True when the (effective) platform is Linux.</summary>
    bool IsLinux { get; }

    /// <summary>
    /// Lowercase platform tag suitable for backup manifests and structured
    /// log fields: <c>"windows"</c>, <c>"macos"</c>, <c>"linux"</c>, or
    /// <c>"unknown"</c>.
    /// </summary>
    string PlatformId { get; }

    /// <summary>
    /// Human-readable platform name for UI display:
    /// <c>"Windows"</c>, <c>"macOS"</c>, <c>"Linux"</c>, or <c>"Unknown"</c>.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// PATH-list separator: <c>;</c> on Windows, <c>:</c> elsewhere.
    /// </summary>
    char PathListSeparator { get; }

    /// <summary>
    /// String comparison appropriate for filesystem path equality on this
    /// platform: <see cref="StringComparison.OrdinalIgnoreCase"/> on Windows
    /// (NTFS is case-insensitive in practice), <see cref="StringComparison.Ordinal"/>
    /// elsewhere. Used for path deduplication and PATH-membership checks.
    /// </summary>
    StringComparison PathComparison { get; }
}