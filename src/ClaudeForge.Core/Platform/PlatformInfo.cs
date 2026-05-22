using System.Runtime.InteropServices;

namespace Bennewitz.Ninja.ClaudeForge.Core.Platform;

/// <summary>
/// Static accessor for the current <see cref="IPlatformInfo"/>.
/// <para>
/// Defaults to <see cref="RuntimePlatformInfo.Instance"/> (the real OS).
/// Tests and the <c>--linux</c> / <c>--macos</c> / <c>--windows</c> debug
/// flags swap in an <see cref="EmulatedPlatformInfo"/> via
/// <see cref="OverrideForDebug"/>; tests must call
/// <see cref="ResetForTesting"/> in their cleanup hook to restore the
/// runtime instance for the next test.
/// </para>
/// <para>
/// The override is intentionally NOT thread-safe: it is meant to run once
/// during <c>Program.Main</c> before any UI thread has started, and to be
/// stable for the rest of the process. Tests that mutate it serialise via
/// MSTest's per-test isolation.
/// </para>
/// </summary>
public static class PlatformInfo
{
    /// <summary>The active platform identity (real or emulated).</summary>
    public static IPlatformInfo Current { get; private set; } = RuntimePlatformInfo.Instance;

    /// <summary>
    /// Replace the active platform identity for the rest of the process.
    /// Intended for the <c>--linux</c> / <c>--macos</c> / <c>--windows</c>
    /// debug flags wired in <c>DebugFlags.Initialize</c>. Production code
    /// should never call this.
    /// </summary>
    public static void OverrideForDebug(IPlatformInfo info)
    {
        Current = info ?? throw new ArgumentNullException(nameof(info));
    }

    /// <summary>Test-only reset hook — restores the runtime-OS instance.</summary>
    public static void ResetForTesting()
    {
        Current = RuntimePlatformInfo.Instance;
    }
}

/// <summary>
/// Production <see cref="IPlatformInfo"/> implementation backed by
/// <see cref="RuntimeInformation.IsOSPlatform"/>. Cached as a singleton —
/// the platform identity does not change at runtime.
/// </summary>
public sealed class RuntimePlatformInfo : IPlatformInfo
{
    public static IPlatformInfo Instance { get; } = new RuntimePlatformInfo();

    private RuntimePlatformInfo()
    {
    }

    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public string PlatformId =>
        IsWindows ? "windows" : IsMacOS ? "macos" : IsLinux ? "linux" : "unknown";

    public string DisplayName =>
        IsWindows ? "Windows" : IsMacOS ? "macOS" : IsLinux ? "Linux" : "Unknown";

    public char PathListSeparator => IsWindows ? ';' : ':';

    public StringComparison PathComparison =>
        IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

/// <summary>
/// Debug-flag <see cref="IPlatformInfo"/> implementation that pretends to be
/// a specific platform regardless of the host OS. Used to test UI surfaces
/// that vary by platform (e.g. the Desktop config path string shown in the
/// About page) without rebooting into a different OS.
/// <para>
/// Construct via <see cref="ForId"/> with <c>"windows"</c>, <c>"macos"</c>,
/// or <c>"linux"</c>. Unknown ids fall back to a non-Windows/Mac/Linux state
/// so misuse fails loudly rather than silently emulating Windows.
/// </para>
/// </summary>
public sealed class EmulatedPlatformInfo : IPlatformInfo
{
    public bool IsWindows { get; }
    public bool IsMacOS { get; }
    public bool IsLinux { get; }
    public string PlatformId { get; }
    public string DisplayName { get; }
    public char PathListSeparator { get; }
    public StringComparison PathComparison { get; }

    private EmulatedPlatformInfo(string id)
    {
        IsWindows = id == "windows";
        IsMacOS = id == "macos";
        IsLinux = id == "linux";

        PlatformId = id;
        DisplayName = id switch
        {
            "windows" => "Windows",
            "macos" => "macOS",
            "linux" => "Linux",
            var _ => "Unknown",
        };

        PathListSeparator = IsWindows ? ';' : ':';
        PathComparison = IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    /// <summary>
    /// Build an emulated platform info for the given lowercase id.
    /// Throws when the id is not <c>"windows"</c>, <c>"macos"</c>, or
    /// <c>"linux"</c>.
    /// </summary>
    public static EmulatedPlatformInfo ForId(string id)
    {
        if (id != "windows" && id != "macos" && id != "linux")
        {
            throw new ArgumentOutOfRangeException(nameof(id),
                id, "Expected 'windows', 'macos', or 'linux'.");
        }

        return new EmulatedPlatformInfo(id);
    }
}