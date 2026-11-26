using Bennewitz.Ninja.AgentForge.Artifacts;

namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// Where one product keeps the files its memory and footprint surfaces show: the artifact sources
/// behind the inventory and the editor, the footprint categories, and the roots they resolve
/// against.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The product implements this; the services only read it.</b>
/// <see cref="UserMemoryService"/>, <see cref="EditableMemoryService"/> and
/// <see cref="FootprintService"/> stat, shape and delete what a layout declares, and none of them
/// names a directory of its own. A second product is a second layout, not an edit to the services.
/// </para>
/// <para>
/// ⛔ <b>Every member is read per use, and an implementation whose paths come from process state
/// must resolve them on each read.</b> The profile root honours an <c>AsyncLocal</c> test override,
/// and a footprint service lives as long as its client. A layout that captured its paths when it
/// was built would pin whichever sandbox was current then, and a test that set its override
/// afterwards would silently read another test's directory.
/// </para>
/// </remarks>
public interface IArtifactLayout
{
    /// <summary>The sources behind the memory inventory, in walk order.</summary>
    /// <param name="projectRoot">
    /// The open project's root, or <see langword="null"/> when none is open. A layout creates no
    /// project sources without one.
    /// </param>
    IReadOnlyList<CategorisedArtifactSource> InventorySources(string? projectRoot);

    /// <summary>The sources behind the agents, commands and skills editor, in walk order.</summary>
    /// <param name="projectRoot">
    /// The open project's root, or <see langword="null"/> when none is open.
    /// </param>
    IReadOnlyList<ScopedArtifactSource> EditorSources(string? projectRoot);

    /// <summary>The product's footprint categories.</summary>
    FootprintCatalog FootprintCatalog { get; }

    /// <summary>The named roots <see cref="FootprintCatalog"/> is expressed against.</summary>
    FootprintRoots FootprintRoots { get; }

    /// <summary>
    /// The directory holding one subdirectory of session transcripts per project, or
    /// <see langword="null"/> when the product keeps none.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>A directory walk, so only a product that stores transcripts as files has one.</b> A
    /// product that keeps sessions in a database answers <see langword="null"/>, and the
    /// per-project transcript rows are then empty rather than another product's.
    /// </remarks>
    string? ProjectTranscriptsDirectory { get; }
}

/// <summary>
/// One artifact source plus the inventory category its entries belong to.
/// </summary>
/// <param name="Source">The source itself.</param>
/// <param name="Category">Which <see cref="UserMemoryCategory"/> the entries land in.</param>
/// <param name="NameSuffix">
/// Text appended to each entry's display name, or empty. A layout sets it where two sources
/// declare files of the same name, so the two rows can be told apart inside one group.
/// </param>
/// <remarks>
/// ⚠ <b><see cref="UserMemoryCategory"/> conflates kind and scope, and
/// <see cref="ArtifactRef"/> deliberately does not.</b> Three categories —
/// <see cref="UserMemoryCategory.PrimaryMemory"/>, <see cref="UserMemoryCategory.ProjectMemory"/>
/// and <see cref="UserMemoryCategory.CrossToolMemory"/> — are all
/// <see cref="ArtifactKind.Memory"/>, separated only by where they came from. So the category
/// cannot be recovered from an entry alone; it is carried here, on the source that produces it,
/// which is the one place that knows both halves.
/// </remarks>
public sealed record CategorisedArtifactSource(
    IArtifactSource Source, UserMemoryCategory Category, string NameSuffix = "");

/// <summary>
/// One artifact source plus the editing scope its entries belong to.
/// </summary>
/// <param name="Source">The source itself.</param>
/// <param name="Scope">
/// Which <see cref="EditableMemoryScope"/> the entries land in — and therefore whether the editor
/// offers to save them.
/// </param>
/// <remarks>
/// ⚠ <b>Not recoverable from the entry, and for the opposite reason to
/// <see cref="CategorisedArtifactSource"/>.</b> There the tag was coarser than the entry's kind;
/// here it is coarser than the entry's <see cref="ArtifactScope"/> — a plugin source yields one
/// scope per installed plugin, and every one of them is
/// <see cref="EditableMemoryScope.Plugin"/>. Reading writability off a scope id would mean parsing
/// a string prefix, which is how a typo becomes an editable plugin file.
/// </remarks>
public sealed record ScopedArtifactSource(IArtifactSource Source, EditableMemoryScope Scope);
