namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// One entry in the Essentials model picker's suggestion list. Carries both the
/// <see cref="Value"/> written to <c>model</c> (an id / alias / <c>[1m]</c> form) and
/// a friendly <see cref="Label"/> (the catalog brand name, e.g. <c>Opus 4.8</c>) so the
/// picker can display the friendly name while still committing a valid model id, and
/// the fuzzy filter can match either. <see cref="Detail"/> is an optional dim subtitle
/// (usually the raw value) shown under the label.
/// </summary>
public sealed record ModelSuggestionItem(string Value, string Label, string? Detail = null)
{
    /// <summary>The text the picker matches/commits — bound via <c>ValueMemberBinding</c>.</summary>
    public override string ToString()
    {
        return Value;
    }
}
