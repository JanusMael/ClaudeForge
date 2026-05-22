using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.ClaudeForge.Core.Settings;

/// <summary>
/// Holds all scope contributions for a single JSON path, and exposes the effective (winning) value.
/// </summary>
public sealed class LayeredValue
{
    private readonly List<ScopeEntry> _entries;

    public LayeredValue(string jsonPath, IEnumerable<ScopeEntry> entries)
    {
        JsonPath = jsonPath;
        _entries = entries.OrderBy(e => (int)e.Scope).ToList(); // lowest number = highest priority first
    }

    /// <summary>The dot-separated JSON path, e.g. "permissions.defaultMode".</summary>
    public string JsonPath { get; }

    /// <summary>All scope entries, ordered highest-priority first.</summary>
    public IReadOnlyList<ScopeEntry> Entries => _entries;

    /// <summary>
    /// The winning value after merge rules are applied.
    /// For arrays: union across all scopes. For non-arrays: highest-priority scope wins.
    /// </summary>
    public JsonNode? EffectiveValue { get; init; }

    /// <summary>Which scope provided the effective value (for non-arrays).</summary>
    public ConfigScope? EffectiveScope { get; init; }

    /// <summary>True when more than one scope defines this property.</summary>
    public bool IsOverridden => _entries.Count > 1;

    /// <summary>True when the Managed scope defines this property.</summary>
    public bool IsManagedLocked => _entries.Any(e => e.Scope == ConfigScope.Managed);

    /// <summary>Returns the value defined at the given scope, or null if not defined there.</summary>
    public JsonNode? GetValueAt(ConfigScope scope)
    {
        return _entries.FirstOrDefault(e => e.Scope == scope)?.Value;
    }

    /// <summary>True when a specific scope defines this property.</summary>
    public bool IsDefinedAt(ConfigScope scope)
    {
        return _entries.Any(e => e.Scope == scope);
    }
}