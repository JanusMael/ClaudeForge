namespace Bennewitz.Ninja.AgentForge.Core.Backup;

/// <summary>
/// Ids for the phases the BACKUP engine names in words, as opposed to the files it names.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>One constant, and the asymmetry with <see cref="RestoreProgressIds"/> is real rather than
/// an oversight.</b> A backup's progress is almost entirely file names — data, with nothing to
/// translate — while a restore's is almost entirely phases. Exactly one backup report is a phrase,
/// and it is here because it is user-visible English emitted from a layer with no resources: the
/// same defect as the restore labels, in the same engine, found by searching for the shape rather
/// than by anyone reporting it.
/// </para>
/// </remarks>
public static class BackupProgressIds
{
    /// <summary>Scanning for per-project configuration before anything is written.</summary>
    public const string DiscoveringProjects = "backup.discovering-projects";

    /// <summary>Every id declared here, for a host checking its map covers them.</summary>
    public static IReadOnlyList<string> All { get; } = [DiscoveringProjects];
}
