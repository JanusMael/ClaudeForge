namespace Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

/// <summary>
/// Identifies an agent product and names the schema its config is validated against.
/// </summary>
/// <param name="Id">
/// Stable machine identifier — <c>"claude-code"</c>, <c>"claude-desktop"</c>. Used for
/// logging and for keying per-product state; never shown to users.
/// </param>
/// <param name="DisplayName">Human-facing name, e.g. <c>"Claude Code"</c>.</param>
/// <param name="SchemaUrl">
/// Where the schema is fetched from when it is not already bundled or cached. May be a
/// <c>bundled://</c> pseudo-URL for products whose schema ships with the app and has no
/// upstream to refresh from.
/// </param>
/// <param name="SchemaFileName">
/// File name used for the bundled resource and the on-disk cache entry, e.g.
/// <c>"claude-code-settings.json"</c>. Also the key for schema-derived lookups such as
/// hook events and hook command variants.
/// </param>
/// <param name="ArchiveFolder">
/// Name this product's files live under inside a backup archive, e.g. <c>"ClaudeCode"</c> —
/// and, by construction, the string listed in the archive manifest's <c>clients</c> array.
/// <para>
/// ⚠ <b>PERSISTED.</b> It is written into every archive users already have on disk, and read
/// back by the restore browser. Changing an existing product's value silently orphans old
/// archives: their folders stop being found and their manifest entries stop being recognised.
/// Choose it once per product and leave it alone.
/// </para>
/// <para>
/// It is deliberately NOT derived from <see cref="Id"/>. The two vocabularies differ —
/// <c>claude-code</c> versus <c>ClaudeCode</c> — because the ids were chosen for code and
/// the folder names were already on disk. Recording both is what stops a mapping table,
/// or a second set of literals, from having to exist somewhere else.
/// </para>
/// </param>
/// <param name="BackupLayout">
/// What this product contributes to a backup archive, and what a backup skips.
/// <see langword="null"/> for a product with no backup support yet — the engines treat that as
/// contributing nothing rather than as an error, so a product can be schema-editable long before
/// it is backup-able.
/// <para>
/// ⭐ <b>This is the sixth positional parameter, which is the documented ceiling.</b> The backup
/// data is grouped into one <see cref="ProductBackupLayout"/> rather than added as three more
/// parameters for exactly that reason. A seventh concern takes an options record instead.
/// </para>
/// <para>
/// ⚠ <b>Not persisted, unlike <paramref name="ArchiveFolder"/>.</b> The layout describes how to
/// build and read an archive; only the folder name it yields ends up on disk. Changing a section's
/// destination changes where a restore puts files — which matters — but it orphans nothing.
/// </para>
/// </param>
/// <remarks>
/// <para>
/// Replaces <c>AgentConfigClientCore.IsClaudeCode</c>, a <see langword="bool"/> that meant
/// "Claude Code, else Claude Desktop" and so could only ever describe two products. Every
/// use of it was really asking one of two questions — <i>which schema validates me?</i> and
/// <i>which schema do I read hook metadata from?</i> — and both are answered by naming the
/// schema instead of naming the product.
/// </para>
/// <para>
/// <b>Adding a product does not mean adding a case.</b> The former boolean forced a
/// ternary at every call site, so a third product would have had to become an enum and
/// every ternary a switch. A descriptor is data: the call sites do not change.
/// </para>
/// </remarks>
public sealed record ProductDescriptor(
    string Id,
    string DisplayName,
    string SchemaUrl,
    string SchemaFileName,
    string ArchiveFolder,
    ProductBackupLayout? BackupLayout = null)
{
    /// <summary>
    /// This product's backup layout, or <see cref="ProductBackupLayout.Empty"/> when it has none.
    /// </summary>
    /// <remarks>
    /// Saves every caller a null check, and makes "no backup support" behave as "contributes
    /// nothing" rather than as a crash on the first product that lacks one.
    /// </remarks>
    public ProductBackupLayout Backup => BackupLayout ?? ProductBackupLayout.Empty;

    /// <summary>
    /// Two descriptors are the same product when their <see cref="Id"/> matches. Nothing else is
    /// compared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔⛔ <b>The synthesized record equality was never meaningful for this type, and relying on
    /// it silently stopped working when the Claude Code descriptor became environment-bound.</b>
    /// <see cref="ProductBackupLayout"/> holds <c>Func</c> destinations, and delegates compare by
    /// REFERENCE — so two descriptors built from identical inputs, one line apart, were already
    /// unequal. That was harmless only while each product was a single static instance, where
    /// reference equality happened to give the right answer by accident.
    /// </para>
    /// <para>
    /// ⭐ <b>This states the contract the codebase already assumed.</b> Both places that needed to
    /// match products — <c>BackupRequest.Includes</c> and the navigation's <c>SectionFor</c> —
    /// compared <see cref="Id"/> by hand and said in their remarks that a separately-constructed
    /// descriptor must still match. Anything reaching for <c>==</c>,
    /// <see cref="System.Linq.Enumerable.Contains{T}(IEnumerable{T}, T)"/>, <c>Distinct</c> or a
    /// dictionary key now gets that same answer instead of a silent miss.
    /// </para>
    /// <para>
    /// ⚠ The consequence, stated plainly: two descriptors for one product that differ in
    /// <see cref="BackupLayout"/> or <see cref="SchemaUrl"/> ARE equal. That is intended — they
    /// are the same product configured differently — but it means equality cannot be used to
    /// detect such a difference. Compare the member you care about.
    /// </para>
    /// </remarks>
    public bool Equals(ProductDescriptor? other) =>
        other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);

    /// <inheritdoc cref="Equals(ProductDescriptor)"/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);
}
