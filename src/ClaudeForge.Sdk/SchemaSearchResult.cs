namespace Bennewitz.Ninja.ClaudeForge.Sdk;

/// <summary>
/// One match returned by <see cref="IClaudeConfigClient.SearchSchema"/>.
/// Contains the dotted JSON path, display metadata, and a context snippet
/// centred around the first occurrence of the search query in
/// <see cref="Description"/>.
/// </summary>
/// <param name="JsonPath">
/// Dotted JSON path, e.g. <c>permissions.allow</c>.
/// This is the key used by <see cref="IClaudeConfigClient.GetEffective{T}"/>
/// and <see cref="IClaudeConfigClient.SetValue{T}(string, T)"/>.
/// </param>
/// <param name="Name">Leaf property name, e.g. <c>allow</c>.</param>
/// <param name="Title">Human-readable title from the schema, e.g. <c>Allow</c>.</param>
/// <param name="Description">Full description text from the schema.</param>
/// <param name="Snippet">
/// Short excerpt (≤ ~120 chars) centred around the first query match in
/// <see cref="Description"/>. Padded with <c>…</c> when the description was
/// truncated. Empty when the description is empty or the match was only on
/// the path / name / title.
/// </param>
public sealed record SchemaSearchResult(
    string JsonPath,
    string Name,
    string Title,
    string Description,
    string Snippet);