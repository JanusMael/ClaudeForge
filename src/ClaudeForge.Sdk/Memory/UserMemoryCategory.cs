namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

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
}