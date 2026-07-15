using Bennewitz.Ninja.ClaudeForge.Core.Catalog;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Models;

/// <summary>
/// Read-only accessor over the bundled model catalog — the public contract for
/// the allowed <c>model</c> / <c>effortLevel</c> / <c>permissions.defaultMode</c>
/// values and their inter-relationships. Mirrors the
/// <see cref="Bennewitz.Ninja.ClaudeForge.Sdk.Env.IEnvAccessor"/> shape: a typed,
/// Avalonia-free surface that non-GUI consumers (CLI, future MCP server) can use.
/// <para>
/// The relationship queries and the nearest-analog coercion rule live here (not
/// in any view-model) so they are headlessly testable and reusable. This accessor
/// does not read or write the workspace; it surfaces static catalog data plus
/// pure derivations. Unknown/custom model ids are treated leniently (all effort
/// levels allowed, auto disallowed) so a hand-typed model never blanks a dropdown.
/// </para>
/// </summary>
public interface IModelCatalogAccessor
{
    /// <summary>All catalogued models, in catalog order (includes legacy entries).</summary>
    IReadOnlyList<ModelInfo> AllModels { get; }

    /// <summary>All <c>permissions.defaultMode</c> values with their gating metadata.</summary>
    IReadOnlyList<DefaultModeCatalogInfo> AllDefaultModes { get; }

    /// <summary>All effort levels, ordered Faster→Smarter.</summary>
    IReadOnlyList<EffortLevelInfo> AllEffortLevels { get; }

    /// <summary>Resolve an id-or-alias (optionally carrying a <c>[1m]</c> suffix) to its model, or <c>null</c>.</summary>
    ModelInfo? Resolve(string? idOrAlias);

    /// <summary>Effort levels the model supports (raw). Unknown models → all levels; Haiku-style → empty.</summary>
    IReadOnlyList<string> SupportedEffortLevels(string? modelId);

    /// <summary>Supported effort levels that persist to settings.json (drops session-only <c>max</c>).</summary>
    IReadOnlyList<string> PersistableEffortLevels(string? modelId);

    /// <summary>True when <paramref name="effort"/> is in the model's raw supported set.</summary>
    bool IsEffortSupported(string? modelId, string? effort);

    /// <summary>The persistable effort to keep when the current one is invalid for the model (nearest analog), or <c>null</c> when the model has no effort.</summary>
    string? NearestAnalogEffort(string? modelId, string? effort);

    /// <summary>The model's declared default effort, or <c>null</c>.</summary>
    string? DefaultEffortLevel(string? modelId);

    /// <summary>True when <c>permissions.defaultMode = auto</c> is honoured for the model (false for unknown models).</summary>
    bool SupportsAutoMode(string? modelId);

    /// <summary>
    /// True when <paramref name="mode"/> can take effect for <paramref name="modelId"/>
    /// at <paramref name="scope"/> — i.e. not gated out by a model-capability or
    /// scope requirement (e.g. <c>auto</c> needs an auto-capable model AND User scope).
    /// Unknown modes return <see langword="true"/> (not hidden).
    /// </summary>
    bool IsDefaultModeAllowed(string mode, string? modelId, ConfigScope scope);

    /// <summary>Free-form model suggestions for an editable picker (aliases + ids + <c>[1m]</c> variants).</summary>
    IReadOnlyList<string> ModelSuggestions(bool includeLegacy = false, bool include1m = true);
}
