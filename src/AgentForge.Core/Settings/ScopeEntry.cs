using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.AgentForge.Core.Settings;

/// <summary>
/// One scope's contribution to a setting value at a given JSON path.
/// </summary>
public sealed record ScopeEntry(
    ConfigScope Scope,
    JsonNode? Value,
    string SourceFilePath);