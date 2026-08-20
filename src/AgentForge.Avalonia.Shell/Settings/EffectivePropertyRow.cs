using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;

/// <summary>
/// One row of the effective-value view: a property, the value that wins, and where it came from.
/// </summary>
/// <remarks>
/// Extracted from the app's effective-settings view-model when the settings group editor became
/// neutral — the group editor produces these rows, so the type has to live where the producer
/// does. Every member was already product-neutral.
/// </remarks>
public sealed record EffectivePropertyRow(
    string Property,
    string DisplayValue,
    ConfigScope? Scope,
    bool IsOverridden,
    string? Description = null)
{
    /// <summary>
    /// Tooltip for the property-name cell: the schema description when known, else the
    /// raw path (so the cell always has a meaningful hover, matching the old behaviour).
    /// </summary>
    public string PropertyTooltip => string.IsNullOrWhiteSpace(Description) ? Property : Description!;
}
