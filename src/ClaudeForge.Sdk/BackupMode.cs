namespace Bennewitz.Ninja.ClaudeForge.Sdk;

/// <summary>
/// Controls how much of the Claude footprint a backup includes.
/// </summary>
/// <remarks>
/// <para>
/// Numeric values mirror <c>ClaudeForge.Core.Backup.BackupMode</c> for the
/// duration of the migration. The Core copy retains its
/// <c>JsonStringEnumConverter</c> attribution so on-disk backup manifests
/// continue to round-trip through the existing serializer; this Sdk-level
/// type is the one consumers see in <c>BackupRequest</c>.
/// </para>
/// </remarks>
public enum BackupMode
{
    /// <summary>
    /// Default. Includes <c>~/.claude.json</c>, settings / hooks / agents /
    /// slash-commands, per-project <c>.claude</c> folders, external worktrees,
    /// and Claude Desktop config — but excludes <c>~/.claude/projects/</c>
    /// (session transcripts, memory artefacts) which can be hundreds of megabytes
    /// and is safely rebuildable from the transcripts.
    /// </summary>
    SettingsOnly = 0,

    /// <summary>
    /// Everything <see cref="SettingsOnly"/> includes, plus
    /// <c>~/.claude/projects/</c>. Suitable for full disaster-recovery and
    /// machine-to-machine migrations.
    /// </summary>
    Full = 1,
}