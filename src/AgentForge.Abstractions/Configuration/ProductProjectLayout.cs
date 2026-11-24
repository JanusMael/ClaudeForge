namespace Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

/// <summary>
/// What a product keeps inside each of the user's projects, and how it knows which projects there
/// are: the part of a backup that a fixed archive path cannot describe.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Sections describe the product's own files; this describes the user's projects.</b> A
/// project's files are found by the project list and by git worktree discovery, not at a path the
/// product owns, so no <see cref="ProductArchiveSection"/> can name them. The backup engine writes
/// them under <c>&lt;ArchiveFolder&gt;/projects/&lt;name&gt;/</c> and
/// <c>&lt;ArchiveFolder&gt;/worktrees/&lt;name&gt;/</c>, and the restore engine reads them back from
/// the same two folders.
/// </para>
/// <para>
/// ⛔ <b>Both providers are also a restore's AUTHORISATION source.</b> A restore writes a project
/// only to a path this machine vouches for, and these are what vouch: a path the product's own
/// configuration names cannot have been written by the archive being restored. So they read this
/// machine, never an archive.
/// </para>
/// </remarks>
/// <param name="Items">
/// The files and directories under a project root that a backup captures, by name relative to the
/// root. A directory is captured whole.
/// </param>
/// <param name="RelatedProjects">
/// The project roots the product's own configuration names beside the open ones, given the roots
/// the host has open: for Claude Code, the <c>additionalDirectories</c> its settings files list.
/// Captured in every backup mode. May return roots that do not exist; the engine filters them.
/// </param>
/// <param name="KnownProjects">
/// Every project the product has ever opened on this machine. Captured only by a full backup, which
/// is meant to be comprehensive; the narrower modes keep to the open project.
/// </param>
public sealed record ProductProjectLayout(
    IReadOnlyList<string> Items,
    Func<IReadOnlyList<string>, IReadOnlyList<string>> RelatedProjects,
    Func<ProductKnownProjects> KnownProjects);

/// <summary>
/// The projects a product knows on this machine: the roots that still exist, and how many it named
/// that do not.
/// </summary>
/// <remarks>
/// The dropped roots are counted, not kept, so a caller can warn about the gap without holding or
/// logging paths that may name the user's directories.
/// </remarks>
/// <param name="ExistingProjectRoots">The known roots that exist on disk.</param>
/// <param name="SkippedNonExistentCount">How many known roots no longer exist.</param>
public sealed record ProductKnownProjects(
    IReadOnlyList<string> ExistingProjectRoots,
    int SkippedNonExistentCount)
{
    /// <summary>No known projects.</summary>
    public static ProductKnownProjects None { get; } = new([], 0);
}
