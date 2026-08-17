namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// Tier 1 user-memory categories — files the user authored that Claude reads
/// every session. Each enum value maps to a discrete on-disk location plus a
/// loose semantic intent (e.g. <see cref="Plan"/> = saved plan markdown).
/// </summary>
/// <remarks>
/// The set of categories is closed: <see cref="UserMemoryService"/> enumerates
/// each one explicitly. Adding a new category requires extending both the
/// enum AND the service's <c>SnapshotFiles</c> dispatch.
/// </remarks>
public enum UserMemoryCategory
{
    /// <summary><c>~/.claude/CLAUDE.md</c> or <c>~/.claude/AGENTS.md</c>.</summary>
    PrimaryMemory,

    /// <summary><c>&lt;project&gt;/CLAUDE.md</c> or <c>&lt;project&gt;/AGENTS.md</c> — only when a project root is supplied.</summary>
    ProjectMemory,

    /// <summary><c>~/.claude/agents/*.md</c> — custom subagent definitions.</summary>
    Subagent,

    /// <summary><c>~/.claude/commands/*.md</c> — custom slash commands.</summary>
    SlashCommand,

    /// <summary><c>~/.claude/hooks/*</c> — hook scripts (any extension).</summary>
    Hook,

    /// <summary><c>~/.claude/plans/*.md</c> — saved plans.</summary>
    Plan,

    /// <summary><c>~/.claude/rules/**/*.md</c> — rule files (recursive).</summary>
    Rule,

    /// <summary><c>~/.claude/skills/&lt;name&gt;/SKILL.md</c> — custom skills.</summary>
    Skill,

    /// <summary>Cross-tool memory: <c>.codex/AGENTS.md</c>, <c>.gemini/GEMINI.md</c>, <c>.opencode/*.md</c> next to <c>~/.claude/</c>.</summary>
    CrossToolMemory,

    /// <summary>
    /// The JSON configuration files themselves — user scope
    /// (<c>~/.claude/settings.json</c>, <c>mcp.json</c>, <c>managed-settings.json</c> and its
    /// <c>managed-settings.d/*.json</c> drop-ins, plus <c>~/.claude.json</c>) and project scope
    /// (<c>&lt;project&gt;/.claude/settings.json</c>, <c>settings.local.json</c>, <c>mcp.json</c>).
    /// Not "memory" in the CLAUDE.md sense, but they are the other half of what Claude reads,
    /// and the inventory is the one place that makes every config file discoverable and
    /// openable in an editor.
    /// <para>
    /// Credentials (<c>PlatformPaths.CredentialsPath</c>) are deliberately EXCLUDED: the file
    /// holds live auth tokens, and surfacing a one-click "open" for it in a browsable list is
    /// a needless disclosure risk. The backup pipeline gates it behind an explicit
    /// <c>IncludeCredentials</c> flag for the same reason.
    /// </para>
    /// </summary>
    Configuration,
}