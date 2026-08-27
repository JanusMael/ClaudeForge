using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// Every path the artifact and footprint services read, resolved from one user-profile root.
/// </summary>
/// <param name="userProfile">
/// The profile directory everything hangs off — <c>%USERPROFILE%</c> / <c>$HOME</c> in production,
/// a sandbox in tests, and a profile directory once profiles land.
/// </param>
/// <remarks>
/// <para>
/// ⭐⭐ <b>The root is the USER PROFILE, not <c>~/.claude</c>, and that is a measured correction to
/// this phase's own plan.</b> Two of the surfaces sit <i>beside</i> <c>~/.claude</c> rather than
/// inside it: <c>~/.claude.json</c> is Claude Code's global config, and the cross-tool memory probes
/// (<c>.codex</c>, <c>.gemini</c>, <c>.opencode</c>) are siblings. Rooting at <c>ClaudeHome</c>
/// cannot express either — Phase 10b had to recover the profile with
/// <c>Directory.GetParent(home)</c>, a step this type removes.
/// </para>
/// <para>
/// ⛔ <b>The project-scope paths are deliberately NOT here.</b> <c>ProjectSettingsPath</c>,
/// <c>LocalSettingsPath</c> and <c>ProjectMcpPath</c> are pure functions of a project root the
/// caller already passes in, so they have no static root to inject and putting them on a
/// profile-rooted type would imply a relationship that does not exist. The same reasoning is why
/// <c>MemoryArtifactDeleter</c> and <c>MemoryFileWriter</c> need no injection at all: they already
/// take their paths as arguments. **The real injection surface is what is rooted at the profile,
/// and nothing else.**
/// </para>
/// <para>
/// ⚠ <b>These literals also exist in <see cref="PlatformPaths"/>, and
/// <c>ClaudeArtifactPathsTests</c> pins the two equal.</b> Duplication was the deliberate trade:
/// delegating to <see cref="PlatformPaths"/> would produce a provider that can only ever return the
/// process-global paths, which is a seam in name only — the point of this type is that a different
/// root is a constructor argument rather than a mutation of a process-wide static. The drift guard
/// is what makes the copy safe.
/// </para>
/// </remarks>
public sealed class ClaudeArtifactPaths(string userProfile)
{
    /// <summary>
    /// The paths for the current process, as <see cref="PlatformPaths"/> resolves them.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>A fresh instance per read, on purpose.</b> <see cref="PlatformPaths.UserProfile"/>
    /// honours an <c>AsyncLocal</c> test override, so a cached instance would freeze whichever
    /// sandbox happened to be current when it was first touched — and that failure looks like a
    /// flaky test rather than a stale cache. Constructing one is a single field assignment.
    /// </remarks>
    public static ClaudeArtifactPaths Default => new(PlatformPaths.UserProfile);

    /// <summary>The profile root everything below is resolved against.</summary>
    public string UserProfile { get; } = userProfile;

    /// <summary><c>~/.claude/</c></summary>
    public string ClaudeHome => Path.Combine(UserProfile, ".claude");

    /// <summary><c>~/.claude/settings.json</c> — user scope.</summary>
    public string UserSettingsPath => Path.Combine(ClaudeHome, "settings.json");

    /// <summary><c>~/.claude/mcp.json</c> — user-level MCP server overrides.</summary>
    public string UserMcpPath => Path.Combine(ClaudeHome, "mcp.json");

    /// <summary><c>~/.claude/managed-settings.json</c> — enterprise / MDM policy.</summary>
    public string ManagedSettingsPath => Path.Combine(ClaudeHome, "managed-settings.json");

    /// <summary><c>~/.claude/managed-settings.d/</c> — admin-populated policy fragments.</summary>
    public string ManagedSettingsDropInDir => Path.Combine(ClaudeHome, "managed-settings.d");

    /// <summary>
    /// <c>~/.claude.json</c> — Claude Code's global config, which lives in the profile root rather
    /// than inside <c>.claude/</c>.
    /// </summary>
    public string ClaudeJsonPath => Path.Combine(UserProfile, ".claude.json");

    /// <summary>
    /// <c>~/.claude/.credentials.json</c> — live auth tokens.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>Present so it can be excluded precisely.</b> Nothing enumerates this file; it is here
    /// only so the guard that keeps it out of a browsable inventory can compare an exact path
    /// rather than search a row for the substring "credentials". A guard that matches on a
    /// substring passes for the wrong reasons and fails for them too.
    /// </remarks>
    public string CredentialsPath => Path.Combine(ClaudeHome, ".credentials.json");
}
