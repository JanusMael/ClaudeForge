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
    string ArchiveFolder);
