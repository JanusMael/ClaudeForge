using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// Every path the artifact and footprint services read, resolved from one user-profile root.
/// </summary>
/// <param name="userProfile">
/// The profile directory everything hangs off — <c>%USERPROFILE%</c> / <c>$HOME</c> in production,
/// a sandbox in tests, and a profile directory once profiles land.
/// </param>
/// <param name="env">
/// The resolved Claude environment. It decides whether the home is <c>~/.claude</c> or the
/// directory <c>CLAUDE_CONFIG_DIR</c> names; a caller that genuinely has no environment passes
/// <see cref="ClaudeEnvironment.Empty"/> rather than omitting it.
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
public sealed class ClaudeArtifactPaths(string userProfile, ClaudeEnvironment env)
{
    /// <summary>
    /// The paths for the current process, as <see cref="PlatformPaths"/> resolves them for
    /// <paramref name="env"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>A fresh instance per read, on purpose.</b> <see cref="PlatformPaths.UserProfile"/>
    /// honours an <c>AsyncLocal</c> test override, so a cached instance would freeze whichever
    /// sandbox happened to be current when it was first touched — and that failure looks like a
    /// flaky test rather than a stale cache. Constructing one is a single field assignment.
    /// </para>
    /// <para>
    /// ⛔ <b>It is a METHOD taking the environment, and the parameterless <c>Default</c> it
    /// replaced is deleted rather than kept alongside.</b> A property that still resolved the
    /// default home would leave every existing site compiling and silently reading
    /// <c>~/.claude</c> for a user who had relocated it — which is the defect, not a migration
    /// path. Requiring the argument is what turns each site into a compile error.
    /// </para>
    /// <para>
    /// ⛔ <b>It reads <see cref="PlatformPaths.UserProfile"/> and NOTHING else process-global —
    /// in particular it does not consult <c>TestUserProfileOverride</c> itself.</b> An earlier
    /// draft did, to mirror the way <see cref="PlatformPaths.ClaudeHome"/> lets a sandbox outrank
    /// the environment, and <c>InjectedPathSeamTests</c> rejected it. That guard is right: a
    /// process-global read in this type is how an injected instance becomes silently
    /// ineffective, which is the defect the whole seam exists to prevent. The override still
    /// reaches the root, because <see cref="PlatformPaths.UserProfile"/> honours it.
    /// </para>
    /// <para>
    /// ⚠ <b>So the two implementations agree in production and can differ in ONE test-only
    /// case</b> — a fixture that sets an override <i>and</i> passes a relocated environment.
    /// Production never sets the override, so there is no configuration a user can reach where
    /// the settings pages and the memory surfaces disagree; every fixture here passes
    /// <see cref="ClaudeEnvironment.Empty"/>, where the two are identical.
    /// </para>
    /// </remarks>
    public static ClaudeArtifactPaths DefaultFor(ClaudeEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        return new(PlatformPaths.UserProfile, env);
    }

    /// <summary>The profile root everything below is resolved against.</summary>
    public string UserProfile { get; } = userProfile;

    /// <summary>The environment that decides whether the home has been relocated.</summary>
    public ClaudeEnvironment Env { get; } = env ?? throw new ArgumentNullException(nameof(env));

    /// <summary>
    /// The resolved Claude home — <c>~/.claude/</c> unless <c>CLAUDE_CONFIG_DIR</c> moved it.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Only this member and the three below it move with the environment.</b>
    /// <see cref="ClaudeJsonPath"/> stays rooted at the profile: Claude Code's documented wording
    /// is <i>"every <c>~/.claude</c> path"</i>, and <c>~/.claude.json</c> is not one of them.
    /// </remarks>
    public string ClaudeHome => Env.ResolvedConfigDir ?? Path.Combine(UserProfile, ".claude");

    /// <summary><c>~/.claude/settings.json</c> — user scope.</summary>
    public string UserSettingsPath => Path.Combine(ClaudeHome, "settings.json");

    /// <summary><c>~/.claude/mcp.json</c> — user-level MCP server overrides.</summary>
    public string UserMcpPath => Path.Combine(ClaudeHome, "mcp.json");

    /// <summary>
    /// <c>managed-settings.json</c> in the per-OS SYSTEM policy directory — enterprise / MDM policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔⛔ <b>NOT rooted at <see cref="UserProfile"/>, and that is the one place this type
    /// deliberately ignores its own root.</b> Managed policy is a system-wide location that every
    /// user on the machine shares; deriving it from this instance's root would mean a profile
    /// override silently relocated enterprise policy, which is a policy-escape hatch rather than a
    /// path bug. Every other member here is root-relative precisely so tests can sandbox them —
    /// this one must not be.
    /// </para>
    /// <para>
    /// ⚠ <b>Delegated rather than restated.</b> The rest of this type intentionally repeats
    /// <c>PlatformPaths</c>' literals, with a parity test as the price. The managed root is not
    /// repeated, because there is no root argument that could make the two agree — so the only way
    /// they can agree is to be one expression.
    /// </para>
    /// </remarks>
    public string ManagedSettingsPath => PlatformPaths.ManagedSettingsPath;

    /// <summary>
    /// <c>managed-settings.d/</c> in the per-OS system policy directory — admin-populated policy
    /// fragments. See <see cref="ManagedSettingsPath"/> for why this is not root-relative.
    /// </summary>
    public string ManagedSettingsDropInDir => PlatformPaths.ManagedSettingsDropInDir;

    /// <summary>
    /// <c>managed-mcp.json</c> in the per-OS system policy directory — managed MCP server policy.
    /// See <see cref="ManagedSettingsPath"/> for why this is not root-relative.
    /// </summary>
    public string ManagedMcpPath => PlatformPaths.ManagedMcpPath;

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
