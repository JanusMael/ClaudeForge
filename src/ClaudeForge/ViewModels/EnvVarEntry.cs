namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// Immutable snapshot of a single environment variable across all layers.
/// Priority order (highest wins): Process &gt; Claude &gt; User &gt; Machine.
/// </summary>
public sealed class EnvVarEntry
{
    public string Name { get; init; } = string.Empty;
    public string? MachineValue { get; init; }
    public string? UserValue { get; init; }
    public string? ClaudeValue { get; init; }
    public string? ProcessValue { get; init; }

    /// <summary>
    /// True when this entry was discovered from a schema description hint rather than
    /// observed in any real environment layer.  Such entries are always shown in the
    /// Environment editor (regardless of the ShowAll filter) and are never persisted
    /// with an empty value.
    /// </summary>
    public bool IsFromSuggestion { get; init; }

    /// <summary>The highest-priority value in effect.</summary>
    public string? EffectiveValue =>
        ProcessValue ?? ClaudeValue ?? UserValue ?? MachineValue;

    /// <summary>Human-readable name of the layer that wins.</summary>
    public string EffectiveSource =>
        ProcessValue != null ? "Process" :
        ClaudeValue != null ? "Claude" :
        UserValue != null ? "User" :
        MachineValue != null ? "Machine" : "(not set)";

    /// <summary>True when a higher-priority layer overrides at least one lower-priority layer.</summary>
    public bool IsOverridden =>
        (ProcessValue != null && (ClaudeValue != null || UserValue != null || MachineValue != null)) ||
        (ClaudeValue != null && (UserValue != null || MachineValue != null)) ||
        (UserValue != null && MachineValue != null);
}