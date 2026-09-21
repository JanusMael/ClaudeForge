namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// What one footprint category MEANS for a product: its stable id, the locations it covers, the
/// path a "reveal" button should open, and whether the Standard backup mode preserves it.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>No display name and no description here, on purpose.</b> Every user-visible string in
/// this repo comes from a resx, and this assembly is product-neutral — an English label on this
/// record would be both an unlocalised string and a Claude-shaped one. The app layer maps
/// <see cref="Id"/> to <c>Strings.LabelFootprintCategory*</c>, exactly as AXAML brush lookups are
/// keyed by <c>ConfigScope.Id</c>.
/// </para>
/// <para>
/// ⚠ <b><see cref="Anchor"/> is not <see cref="Sources"/>[0].</b> Claude's <c>SessionMetadata</c>
/// walks three sibling directories but reveals their <i>parent</i>, so the user sees all three at
/// once. Deriving the anchor from the first source would silently open one third of the category.
/// </para>
/// </remarks>
/// <param name="Id">
/// Stable machine key — <c>"session-transcripts"</c>, <c>"prompt-history"</c>. Data, not
/// presentation: it keys the app's label lookup and is what a guard test asserts on. It never
/// changes once shipped, because a renamed id silently loses a category's label.
/// </param>
/// <param name="Sources">Every location the category covers. At least one.</param>
/// <param name="Anchor">The path a "reveal in file manager" action opens.</param>
/// <param name="IsInStandardBackup">
/// <see langword="true"/> when the Standard backup mode preserves this category. ⛔ This mirrors
/// the product's backup skip rules and must be changed in lockstep with them — the Memory page's
/// badge and the Backup/Restore page disagreeing is worse than either being wrong alone.
/// </param>
public sealed record FootprintCategoryDefinition(
    string Id,
    IReadOnlyList<FootprintSource> Sources,
    FootprintSource Anchor,
    bool IsInStandardBackup);
