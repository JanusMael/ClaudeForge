using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// One agent product the shell hosts: what it is called, where its effective config lands
/// in an export, and the live client that holds its state.
/// </summary>
/// <remarks>
/// <para>
/// Replaces <c>MainWindowViewModel</c>'s two named fields, <c>ClaudeCodeSdk</c> and
/// <c>ClaudeDesktopSdk</c>. Those fields meant every lifecycle operation — save, validate,
/// snapshot, subscribe, dispose, export, search — was written twice, and adding a product
/// meant writing it a third time in each of a dozen methods. Iterating a list makes a new
/// product an entry rather than an edit.
/// </para>
/// <para>
/// <b>Scope of the list, honestly stated.</b> The shell's <i>lifecycle</i> is now N-product.
/// The <i>navigation tree</i> is not, and deliberately so: the two sections have different
/// icons, node ids, descriptions, and — the part that matters — Claude Code has pages
/// (Essentials, Environment, Effective settings, Permissions, Hooks) that Claude Desktop
/// has none of. That is two different page compositions sharing a header shape, not one
/// composition applied twice, and collapsing it would mean inventing a page-list
/// abstraction. Phase 5's shell extraction owns that.
/// </para>
/// <para>
/// <b>Still Claude-shaped in one respect:</b> <see cref="Client"/> is a
/// <see cref="ClaudeConfigClientBase"/>, not the neutral <c>AgentConfigClientCore</c>,
/// because the editor view-models this shell builds take <c>IClaudeConfigClient</c> for the
/// Claude-only accessors (Hooks, Permissions, Marketplaces, Plugins, Models). That is
/// correct for <i>this</i> app — its products are Claude's. A second app registering its own
/// products would parameterise this type; that is Phase 5, not this commit.
/// </para>
/// </remarks>
internal sealed class ProductSection
{
    /// <param name="product">Identifies the product and names the schema it validates against.</param>
    /// <param name="navTitle">
    /// Navigation-tree header title. A plain constant, not a resource — product names are
    /// not localized in the nav tree today.
    /// </param>
    /// <param name="workspaceDisplayName">
    /// Resolves the localized name used in save dialogs and change logs. A delegate rather
    /// than a string because these ARE resource-backed, and freezing the value at
    /// construction would show a stale name after a culture change.
    /// </param>
    /// <param name="exportEntryRelativePath">
    /// Where this product's stamped effective config lands <i>inside its own archive
    /// folder</i> — the part after <see cref="ProductDescriptor.ArchiveFolder"/>. Given
    /// relative rather than whole because the folder segment is not this type's to invent:
    /// <see cref="ExportManifest.Clients"/> lists the same folder names, and a reader uses
    /// that list to know which folders the archive contains. Composing the path from the
    /// descriptor makes the two agree structurally instead of by matching literals.
    /// </param>
    internal ProductSection(
        ProductDescriptor product,
        string navTitle,
        Func<string> workspaceDisplayName,
        string exportEntryRelativePath)
    {
        Product = product;
        NavTitle = navTitle;
        WorkspaceDisplayName = workspaceDisplayName;
        _exportEntryRelativePath = exportEntryRelativePath;
    }

    private readonly string _exportEntryRelativePath;

    internal ProductDescriptor Product { get; }

    internal string NavTitle { get; }

    /// <inheritdoc cref="ProductSection(ProductDescriptor, string, Func{string}, string)"/>
    internal Func<string> WorkspaceDisplayName { get; }

    /// <summary>
    /// Archive-relative path this product's stamped effective config is written to by the
    /// Export command, rooted at the product's <see cref="ProductDescriptor.ArchiveFolder"/>.
    /// </summary>
    internal string ExportEntryPath => $"{Product.ArchiveFolder}/{_exportEntryRelativePath}";

    /// <summary>
    /// The live client, or <see langword="null"/> before the first workspace load and after
    /// disposal. Settable because a reload disposes the previous client and installs a fresh
    /// one over the same section.
    /// </summary>
    internal ClaudeConfigClientBase? Client { get; set; }
}
