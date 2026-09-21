namespace Bennewitz.Ninja.AgentForge.Core.Backup;

/// <summary>
/// Ids for the restore phases the engine drives itself, as opposed to the per-product sections
/// whose ids come from <c>ProductArchiveSection.ProgressLabelId</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Constants rather than literals at the report sites</b>, because these ids are one half of
/// a pair: the other half is a resource key in each app, and a typo in either simply falls back to
/// English. That failure is invisible — a correct-looking progress bar in the wrong language —
/// which is exactly the class of bug a shared constant removes.
/// </para>
/// <para>
/// ⚠ <b>Namespaced with a <c>restore.</c> prefix so they cannot collide with a section id.</b>
/// Section ids are archive sub-paths, and nothing stops a product from one day declaring a
/// section called <c>projects</c> — a collision would silently relabel the engine's own step with
/// that product's wording. The prefix is not decoration.
/// </para>
/// </remarks>
public static class RestoreProgressIds
{
    /// <summary>The apply phase is starting; extraction is already done.</summary>
    public const string Applying = "restore.applying";

    /// <summary>Per-project files are being placed.</summary>
    public const string Projects = "restore.projects";

    /// <summary>Worktree files are being placed.</summary>
    public const string Worktrees = "restore.worktrees";

    /// <summary>Everything has been applied.</summary>
    public const string Complete = "restore.complete";

    /// <summary>Every id declared here, for a host that wants to check its map covers them.</summary>
    /// <remarks>
    /// ⚠ Exists so a guard test can assert coverage without re-listing the ids, which would make
    /// the test agree with a copy of the truth rather than with the truth.
    /// </remarks>
    public static IReadOnlyList<string> All { get; } = [Applying, Projects, Worktrees, Complete];
}
