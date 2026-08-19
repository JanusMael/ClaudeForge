using Bennewitz.Ninja.AgentForge.Sdk;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Search;

/// <summary>
/// Pairs a navigation section title with the SDK
/// <see cref="IAgentConfigClient.SearchSchema"/> delegate for the product behind
/// that section. Consumed by <see cref="SearchViewModel"/> to surface
/// property-level results for specialised editors (permissions, hooks, MCP
/// servers) whose schema nodes are not enumerable through
/// <see cref="ISchemaGroupEditor.SchemaNodes"/>.
/// </summary>
public sealed record SchemaSearchProvider(
    string SectionTitle,
    Func<string, IReadOnlyList<SchemaSearchResult>> Search);
