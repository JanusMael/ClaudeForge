namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

/// <summary>
/// Tier 2 footprint categories — files Claude has SEEN but does not actively
/// read on every session. The Memory page surfaces these for audit /
/// privacy / cleanup; deletion is per-category and goes through
/// <see cref="FootprintService.DeleteAsync"/>.
/// </summary>
/// <remarks>
/// These are user behavioural footprint, not user-authored memory. The
/// backup engine's <c>ShouldSkipHomeSubdir</c> decision tree treats most of
/// these as "skipped from Standard backup, included in Full" — the Memory
/// page surfaces the same axis via
/// <see cref="FootprintCategoryStats.IsInStandardBackup"/>.
/// </remarks>
public enum FootprintCategory
{
    /// <summary><c>~/.claude/projects/&lt;mangled&gt;/*.jsonl</c> — every session transcript.</summary>
    SessionTranscripts,

    /// <summary><c>~/.claude/sessions/</c>, <c>session-data/</c>, <c>session-env/</c> — session metadata.</summary>
    SessionMetadata,

    /// <summary><c>~/.claude/history.jsonl</c> — interactive prompt history.</summary>
    PromptHistory,

    /// <summary><c>~/.claude/bash-commands.log</c> — log of bash invocations.</summary>
    BashCommandLog,

    /// <summary><c>~/.claude/cost-tracker.log</c> — token-cost telemetry.</summary>
    CostTrackerLog,

    /// <summary><c>~/.claude/todos/</c> — todo-list snapshots.</summary>
    Todos,

    /// <summary><c>~/.claude/file-history/</c> — file-edit before/after snapshots.</summary>
    FileEditHistory,
}