namespace Bennewitz.Ninja.ClaudeForge.Core.Profile;

/// <summary>
/// Metadata snapshot for a single named Desktop profile directory discovered under
/// <c>&lt;DesktopConfigDir&gt;/profiles/</c>. Immutable — rebuilt on each
/// <see cref="ProfileEngine.DiscoverDesktopProfiles"/> call.
/// <para>
/// A Desktop profile stores exactly one file:
/// <c>claude_desktop_config.json</c> — a complete snapshot of the Desktop config.
/// </para>
/// </summary>
public sealed record DesktopProfileInfo(
    /// <summary>Profile name (= subdirectory name under the Desktop profiles directory).</summary>
    string Name,
    /// <summary>
    /// True when <c>claude_desktop_config.json</c> is present inside the profile directory.
    /// Profiles that lack this file are skipped by <see cref="ProfileEngine.DiscoverDesktopProfiles"/>
    /// because they cannot be applied or synced.
    /// </summary>
    bool HasConfig,
    /// <summary>
    /// True when this profile is the one written to <c>.desktop-current</c> —
    /// i.e. the profile whose config was last applied to the live Desktop location.
    /// </summary>
    bool IsActive
);