namespace Bennewitz.Ninja.ClaudeForge.Core.Settings;

/// <summary>
/// The scope level at which a setting is defined.
/// Lower numeric value = higher priority (Managed overrides everything).
/// Priority order matches Claude Code's documented behaviour:
///   Managed (0) &gt; Local (1) &gt; Project (2) &gt; User (3)
/// More-specific scopes win: a project-local personal override beats a shared
/// project default, which in turn beats the user-global baseline.
/// </summary>
public enum ConfigScope
{
    /// <summary>Enterprise/MDM policy. Read-only; cannot be overridden by any other scope.</summary>
    Managed = 0,

    /// <summary>
    /// Local per-project settings (.claude/settings.local.json).
    /// Gitignored and personal — highest priority among user-editable scopes.
    /// Overrides both Project and User settings for this working tree.
    /// </summary>
    Local = 1,

    /// <summary>
    /// Project settings (.claude/settings.json).
    /// Committed to git and shared with the team.
    /// Overrides User settings; overridden by Local.
    /// </summary>
    Project = 2,

    /// <summary>
    /// User-global settings (~/.claude/settings.json).
    /// Applies to all projects; lowest-priority user-editable scope.
    /// Overridden by Project and Local when a project is open.
    /// </summary>
    User = 3,
}