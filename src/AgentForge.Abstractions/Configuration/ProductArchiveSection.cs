namespace Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

/// <summary>
/// One restorable section of a backup archive: where it lives inside the archive, and where its
/// contents belong on disk.
/// </summary>
/// <remarks>
/// The product folder is <b>not</b> repeated here — it comes from the
/// <see cref="ProductDescriptor.ArchiveFolder"/> of the descriptor this section hangs off, which is
/// the same property the writer uses. That is the whole point: a folder renamed on one side only
/// does not error, it silently stops matching, and a section that stops being found restores
/// nothing while reporting success.
/// </remarks>
/// <param name="SubPath">
/// Archive-relative path beneath the product folder, as path segments. Segments rather than a
/// joined string so the caller never picks a separator — they are combined with the platform's own.
/// </param>
/// <param name="Destination">
/// Where the section restores to.
/// <para>
/// ⛔ <b>A factory, not a string.</b> Destinations derive from the user profile, which honours an
/// <c>AsyncLocal</c> test override. A layout of resolved strings — held in a <c>static readonly</c>
/// descriptor — would capture whichever profile was current when the type initialiser ran, which in
/// a sequential suite sharing a process is another test's sandbox. That failure writes real files
/// into a real home directory and reads as flakiness rather than as a stale value.
/// </para>
/// </param>
/// <param name="IsDirectory">
/// Whether the section is a directory subtree or a single file. ⚠ Getting this wrong does not
/// throw: a file restored as a directory simply finds nothing and the restore still succeeds.
/// </param>
/// <param name="ProgressLabel">
/// Text shown on the progress bar while this section applies.
/// <para>
/// ⚠ <b>Unlocalised, and that is pre-existing debt rather than a new decision.</b> These strings
/// were English literals inside the restore engine before they became data. Moving them here makes
/// them more visible, not more wrong — but the repo's rule is that user-visible text comes from a
/// resx, so a later pass should key these by section id the way footprint labels are keyed by
/// <c>FootprintCategory.Id</c>.
/// </para>
/// </param>
public sealed record ProductArchiveSection(
    IReadOnlyList<string> SubPath,
    Func<string> Destination,
    bool IsDirectory,
    string ProgressLabel)
{
    /// <summary>A single file beneath the product's archive folder.</summary>
    public static ProductArchiveSection File(string subPath, Func<string> destination, string progressLabel) =>
        new([subPath], destination, IsDirectory: false, progressLabel);

    /// <summary>A directory subtree beneath the product's archive folder.</summary>
    public static ProductArchiveSection Directory(string subPath, Func<string> destination, string progressLabel) =>
        new([subPath], destination, IsDirectory: true, progressLabel);
}
