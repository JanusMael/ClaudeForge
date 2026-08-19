using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Search;

/// <summary>
/// Implemented by an editor that renders a flat list of schema-driven properties,
/// letting search match each property individually instead of only the page title.
///
/// <para>
/// Search used to answer this question with <c>editor is SettingsGroupEditorViewModel</c>,
/// which was defensible while the walk and the editors lived in the same assembly.
/// The walk is neutral now, so the question is asked the only way that survives a
/// second product: the editor answers it.
/// </para>
/// </summary>
public interface ISchemaGroupEditor
{
    /// <summary>Page / group name, used as the second breadcrumb segment of a hit.</summary>
    string GroupName { get; }

    /// <summary>
    /// The properties this page renders, top level only — search flattens nested
    /// object properties itself so a match on a nested description still surfaces.
    /// </summary>
    IReadOnlyList<SchemaNode> SchemaNodes { get; }
}

/// <summary>
/// Implemented by a specialised editor that owns one JSON subtree rather than a
/// flat property list (a permissions page, a hooks page, an MCP-servers page).
/// Search cannot enumerate its properties, so it asks the product's schema
/// client instead and keeps the hits whose path falls inside this prefix.
///
/// <para>
/// An editor that is not rooted at a single path simply does not implement this,
/// and search falls back to matching its page title.
/// </para>
/// </summary>
public interface IJsonPathScopedEditor
{
    /// <summary>
    /// The JSON path this editor owns — <c>"permissions"</c> matches the
    /// <c>permissions</c> node itself and everything below it, but never
    /// <c>hooks.permissions</c>.
    /// </summary>
    string OwnedJsonPathPrefix { get; }
}
