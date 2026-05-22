namespace Bennewitz.Ninja.ClaudeForge.Sdk.Diagnostics;

/// <summary>
/// Whether a key was added, removed, or modified between two JSON snapshots.
/// </summary>
public enum ChangeKind
{
    /// <summary>Key exists in current but not in baseline.</summary>
    Added,

    /// <summary>Key exists in baseline but not in current.</summary>
    Removed,

    /// <summary>Key exists in both, but the values differ.</summary>
    Modified,
}

/// <summary>
/// One structural diff between two JSON snapshots, produced by
/// <see cref="JsonDiff.Compute(System.Text.Json.Nodes.JsonObject?, System.Text.Json.Nodes.JsonObject)"/>.
/// </summary>
/// <param name="Key">
/// JSON path of the changed property — top-level key for primitives at the
/// root, dot-separated path (e.g. <c>"hooks.Stop"</c>) for nested objects,
/// and the array's path (no index) for added/removed array elements.
/// </param>
/// <param name="Kind">Whether the key was added, removed, or modified.</param>
/// <param name="OldValue">
/// Baseline value as a JSON string, or <see langword="null"/> for additions.
/// For nested arrays this carries just the removed element's JSON, not the
/// whole array.
/// </param>
/// <param name="NewValue">
/// Current value as a JSON string, or <see langword="null"/> for removals.
/// For nested arrays this carries just the added element's JSON.
/// </param>
public sealed record PropertyDiff(
    string Key,
    ChangeKind Kind,
    string? OldValue,
    string? NewValue);