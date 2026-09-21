namespace Bennewitz.Ninja.AgentForge.Core.Backup;

/// <summary>
/// Progress payload reported through <see cref="IProgress{T}"/> during a long-running
/// backup, restore, or zip-write operation. Values are safe to render on the UI thread.
/// </summary>
/// <param name="Current">Zero-based index of the entry currently being processed.</param>
/// <param name="Total">Total entry count known at the time of reporting. May be <c>0</c>
/// during early phases (e.g. discovery) — consumers should treat <c>0</c> as "indeterminate".</param>
/// <param name="CurrentItem">Human-readable description of the current entry (file name,
/// short relative path, or a phase label such as "Discovering projects").</param>
/// <param name="BytesDone">Cumulative uncompressed bytes processed so far.</param>
/// <param name="ItemId">
/// Stable id for <paramref name="CurrentItem"/> when it is a phase label, so a host can show it
/// in the user's language; <see langword="null"/> when the item is data rather than a phrase.
/// <para>
/// ⛔ <b>Null is the common case and is not a gap.</b> Most progress reports name a FILE — there
/// is nothing to translate about <c>settings.json</c>, and inventing a key per file would be a
/// resource set the size of the user's disk. Only the restore's phase labels carry an id: the
/// per-section ones from <c>ProductArchiveSection.ProgressLabelId</c>, and the engine's own four
/// from <see cref="RestoreProgressIds"/>.
/// </para>
/// <para>
/// ⚠ <b><paramref name="CurrentItem"/> stays populated either way</b>, in English. A host with no
/// key for an id, or no resources at all, shows it and is merely untranslated rather than blank.
/// </para>
/// </param>
public sealed record BackupProgress(
    int Current,
    int Total,
    string CurrentItem,
    long BytesDone,
    string? ItemId = null);