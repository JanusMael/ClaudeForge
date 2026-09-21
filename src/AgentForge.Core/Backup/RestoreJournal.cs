namespace Bennewitz.Ninja.AgentForge.Core.Backup;

/// <summary>
/// Everything one restore run records as it goes: what it refused, what failed, and every
/// sidecar it wrote. Passed down the restore helpers in place of the three separate lists
/// they used to take, because a fourth one would have pushed them past the positional-parameter
/// limit — and because these three are one thing: the account of a single run.
/// </summary>
/// <remarks>
/// ⭐ <b><see cref="Sidecars"/> holds the exact paths this run created</b>, not a pattern to
/// search for later. That is what lets the sweep at the end of a successful restore delete
/// precisely what it wrote and nothing else — no enumeration of a 200 MB tree, and no chance of
/// matching a sidecar an earlier restore left behind, which is someone else's undo trail.
/// </remarks>
internal sealed class RestoreJournal
{
    /// <summary>
    /// Projects and worktrees this run declined, each with the reason in parentheses.
    /// Surfaced to the user in the result message.
    /// </summary>
    public List<string> Skipped { get; } = [];

    /// <summary>Per-file failures — a locked or unreadable destination. Never aborts the run.</summary>
    public List<string> FileFailures { get; } = [];

    /// <summary>
    /// Every <c>*.pre-restore-{stamp}.bak</c> this run wrote, in the order written.
    /// </summary>
    public List<string> Sidecars { get; } = [];
}
