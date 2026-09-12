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
/// <param name="IsProductHome">
/// <see langword="true"/> for the one directory section that is the product's home tree — the
/// subtree <see cref="ProductBackupLayout.SkippedSubdirs"/> applies to when archiving.
/// <para>
/// ⚠ <b>Needed because a product can have more than one directory section and they are not
/// alike.</b> Claude Code's <c>claude-dir</c> is a home tree whose walk skips <c>cache</c>,
/// <c>statsig</c> and the rest; Claude Desktop's <c>profiles</c> is an ordinary directory that is
/// archived whole. Applying the skip list to every directory section would be identical today —
/// Desktop's list is empty — and would quietly become wrong the first time a product gained a
/// second directory section.
/// </para>
/// </param>
/// <param name="RequiresCredentialOptIn">
/// <see langword="true"/> when this section holds live credentials, so it is archived only on an
/// explicit opt-in and never in the sharing-targeted mode.
/// <para>
/// ⚠ <b>The sibling of <see cref="ProductBackupLayout.CredentialFileName"/>, for a different
/// discovery route.</b> That one covers a credential file found while walking a product's home
/// tree; this covers one the product declares outright — OpenCode's <c>opencode.db</c>, whose
/// secrets are SQLite rows in a file that lives outside the config root. Two ways of finding such
/// a file, one policy.
/// </para>
/// </param>
/// <param name="IncludeWhen">
/// Consulted by the WRITER only: when non-<see langword="null"/> and it returns
/// <see langword="false"/>, this section is not archived.
/// <para>
/// ⛔ <b>Write-side only, and the asymmetry is the point.</b> The restorer never calls this. What a
/// restore may apply is decided by what is IN the archive, because the machine reading it is not
/// necessarily the machine that wrote it — gating the restore on the local environment would
/// silently drop files that are demonstrably present, which is the same class of failure this
/// predicate exists to fix.
/// </para>
/// <para>
/// ⚠ It exists for OpenCode's two config roots. <c>$OPENCODE_CONFIG_DIR</c> redirects the config
/// that loads, while the default root stays live for plugin discovery, so both belong in an
/// archive — but when the variable is unset the two resolve to the same directory and archiving
/// both would write every file twice under two names. A predicate says "only when they differ"
/// without the destination having to lie about where the section restores to.
/// </para>
/// </param>
public sealed record ProductArchiveSection(
    IReadOnlyList<string> SubPath,
    Func<string> Destination,
    bool IsDirectory,
    string ProgressLabel,
    bool IsProductHome = false,
    bool RequiresCredentialOptIn = false,
    Func<bool>? IncludeWhen = null)
{
    /// <summary>A single file beneath the product's archive folder.</summary>
    public static ProductArchiveSection File(string subPath, Func<string> destination, string progressLabel) =>
        new([subPath], destination, IsDirectory: false, progressLabel);

    /// <summary>A directory subtree beneath the product's archive folder, archived whole.</summary>
    public static ProductArchiveSection Directory(string subPath, Func<string> destination, string progressLabel) =>
        new([subPath], destination, IsDirectory: true, progressLabel);

    /// <summary>
    /// A directory subtree archived only when <paramref name="includeWhen"/> says so, and restored
    /// whenever the archive carries it.
    /// </summary>
    /// <remarks>
    /// See <see cref="IncludeWhen"/> for why the predicate is not consulted on the restore side.
    /// </remarks>
    public static ProductArchiveSection DirectoryWhen(
        string subPath,
        Func<string> destination,
        string progressLabel,
        Func<bool> includeWhen) =>
        new([subPath], destination, IsDirectory: true, progressLabel, IncludeWhen: includeWhen);

    /// <summary>
    /// The product's home tree — a directory section whose walk honours
    /// <see cref="ProductBackupLayout.SkippedSubdirs"/>.
    /// </summary>
    public static ProductArchiveSection Home(string subPath, Func<string> destination, string progressLabel) =>
        new([subPath], destination, IsDirectory: true, progressLabel, IsProductHome: true);

    /// <summary>
    /// A credential-bearing file: archived only on an explicit opt-in, never when sharing.
    /// </summary>
    public static ProductArchiveSection CredentialFile(
        string subPath,
        Func<string> destination,
        string progressLabel) =>
        new([subPath], destination, IsDirectory: false, progressLabel, RequiresCredentialOptIn: true);
}
