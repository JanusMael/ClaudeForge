namespace Bennewitz.Ninja.ClaudeForge.Core.Profile;

/// <summary>
/// Metadata snapshot for a single named profile directory discovered under
/// <c>~/.claude/profiles/</c>. Immutable — rebuilt on each <see cref="ProfileEngine.DiscoverProfiles"/> call.
/// </summary>
public sealed record ProfileInfo(
    /// <summary>Profile name (= subdirectory name under ~/.claude/profiles/).</summary>
    string Name,
    /// <summary>True when ~/.claude/profiles/&lt;name&gt;/settings.json exists.</summary>
    bool HasSettings,
    /// <summary>True when ~/.claude/profiles/&lt;name&gt;/CLAUDE.md exists.</summary>
    bool HasClaudeMd,
    /// <summary>True when ~/.claude/profiles/&lt;name&gt;/mcp.json exists.</summary>
    bool HasMcp,
    /// <summary>
    /// True when this profile is the one written to <c>~/.claude/.claudectx-current</c>
    /// — i.e. the profile whose files were last applied to the live Claude Code locations.
    /// This is the profile the Claude Code CLI is currently using.
    /// </summary>
    bool IsCliActive
);