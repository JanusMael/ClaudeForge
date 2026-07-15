namespace Bennewitz.Ninja.ClaudeForge.Core.Catalog;

/// <summary>One model entry in the catalog (the structural source of truth).</summary>
/// <param name="Id">Canonical model id, e.g. <c>claude-opus-4-8</c>.</param>
/// <param name="Alias">Short alias that auto-resolves to the latest build, e.g. <c>opus</c>; <c>null</c> for legacy pins.</param>
/// <param name="Label">Brand display name, e.g. <c>Opus 4.8</c> (a product name — never localized).</param>
/// <param name="Legacy">True for older pinned builds that should not appear in the default suggestion list.</param>
/// <param name="Supports1m">True when a <c>[1m]</c> extended-context suffix is valid for this model.</param>
/// <param name="SupportedEffortLevels">Raw effort capability (subset of the catalog's effort ids). Empty = effort not applicable.</param>
/// <param name="DefaultEffortLevel">The effort level Claude defaults to for this model; <c>null</c> when effort is not applicable.</param>
/// <param name="SupportsAutoMode">True when <c>permissions.defaultMode = auto</c> is honoured for this model.</param>
public sealed record ModelInfo(
    string Id,
    string? Alias,
    string Label,
    bool Legacy,
    bool Supports1m,
    IReadOnlyList<string> SupportedEffortLevels,
    string? DefaultEffortLevel,
    bool SupportsAutoMode);

/// <summary>One effort level and its ordering / persistence semantics.</summary>
/// <param name="Id">Effort id, e.g. <c>high</c>.</param>
/// <param name="Order">Faster→Smarter ordinal (low = 0). Used for nearest-analog selection.</param>
/// <param name="Persists">False for session-only levels (e.g. <c>max</c>) that are never written to settings.json.</param>
public sealed record EffortLevelInfo(string Id, int Order, bool Persists);

/// <summary>One <c>permissions.defaultMode</c> value and its gating metadata.</summary>
/// <param name="Id">Mode id, e.g. <c>bypassPermissions</c>.</param>
/// <param name="Experimental">True for not-yet-GA modes (e.g. <c>delegate</c>).</param>
/// <param name="RequiresAutoCapableModel">True when the mode only takes effect on an auto-capable model (e.g. <c>auto</c>).</param>
/// <param name="UserScopeOnly">True when the mode is silently ignored outside User scope (e.g. <c>auto</c>).</param>
public sealed record DefaultModeCatalogInfo(
    string Id,
    bool Experimental,
    bool RequiresAutoCapableModel,
    bool UserScopeOnly);

/// <summary>
/// Immutable, structural source of truth for the inter-related model /
/// effortLevel / permissions.defaultMode value lists. Pure domain — no UI, no
/// localization. Loaded by <see cref="ModelCatalogLoader"/> and surfaced to the
/// app through the SDK <c>IModelCatalogAccessor</c>; never consumed by
/// view-models directly (SDK-first).
/// </summary>
public sealed class ModelCatalog
{
    private static readonly StringComparer Ord = StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, int> _effortOrder;

    public ModelCatalog(
        IReadOnlyList<ModelInfo> models,
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlyList<EffortLevelInfo> effortLevels,
        IReadOnlyList<DefaultModeCatalogInfo> defaultModes)
    {
        Models = models;
        // Case-insensitive so the alias-map fallback in Resolve matches the rest of
        // Resolve (the [1m] strip and direct id/alias match are both OrdinalIgnoreCase).
        // Fixing it here also covers the Parse test seam and any direct construction.
        Aliases = new Dictionary<string, string>(aliases, Ord);
        EffortLevels = effortLevels.OrderBy(e => e.Order).ToList();
        DefaultModes = defaultModes;
        _effortOrder = EffortLevels.ToDictionary(e => e.Id, e => e.Order, Ord);
    }

    /// <summary>An empty, valid catalog — the fail-open result when the bundled data is missing/malformed.</summary>
    public static ModelCatalog Empty { get; } = new([], new Dictionary<string, string>(), [], []);

    /// <summary>Maps the deserialized wire shape to the immutable domain model, skipping entries with no id.</summary>
    internal static ModelCatalog FromDto(ModelCatalogDto dto)
    {
        List<ModelInfo> models = (dto.Models ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Id))
            .Select(m => new ModelInfo(
                m.Id!,
                string.IsNullOrWhiteSpace(m.Alias) ? null : m.Alias,
                string.IsNullOrWhiteSpace(m.Label) ? m.Id! : m.Label!,
                m.Legacy,
                m.Supports1m,
                m.SupportedEffortLevels ?? [],
                string.IsNullOrWhiteSpace(m.DefaultEffortLevel) ? null : m.DefaultEffortLevel,
                m.SupportsAutoMode))
            .ToList();

        Dictionary<string, string> aliases = dto.Aliases ?? new Dictionary<string, string>();

        List<EffortLevelInfo> efforts = (dto.EffortLevels ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .Select(e => new EffortLevelInfo(e.Id!, e.Order, e.Persists))
            .ToList();

        List<DefaultModeCatalogInfo> modes = (dto.DefaultModes ?? [])
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .Select(d => new DefaultModeCatalogInfo(d.Id!, d.Experimental, d.RequiresAutoCapableModel, d.UserScopeOnly))
            .ToList();

        return new ModelCatalog(models, aliases, efforts, modes);
    }

    public IReadOnlyList<ModelInfo> Models { get; }
    public IReadOnlyDictionary<string, string> Aliases { get; }
    /// <summary>Effort levels ordered Faster→Smarter (low first).</summary>
    public IReadOnlyList<EffortLevelInfo> EffortLevels { get; }
    public IReadOnlyList<DefaultModeCatalogInfo> DefaultModes { get; }

    /// <summary>All effort ids that persist to settings.json (omits session-only levels such as <c>max</c>).</summary>
    public IReadOnlyList<string> PersistableEffortIds =>
        EffortLevels.Where(e => e.Persists).Select(e => e.Id).ToList();

    /// <summary>
    /// Resolve an id-or-alias (optionally carrying a trailing <c>[1m]</c> suffix)
    /// to its <see cref="ModelInfo"/>, or <c>null</c> for an unknown/custom value.
    /// </summary>
    public ModelInfo? Resolve(string? idOrAlias)
    {
        if (string.IsNullOrWhiteSpace(idOrAlias))
        {
            return null;
        }

        string key = StripContextSuffix(idOrAlias.Trim());

        ModelInfo? direct = Models.FirstOrDefault(
            m => Ord.Equals(m.Id, key) || (m.Alias is not null && Ord.Equals(m.Alias, key)));
        if (direct is not null)
        {
            return direct;
        }

        return Aliases.TryGetValue(key, out string? id)
            ? Models.FirstOrDefault(m => Ord.Equals(m.Id, id))
            : null;
    }

    /// <summary>
    /// Effort levels the model supports (raw capability). Unknown/custom models
    /// are lenient — all effort ids are returned so a hand-typed model id never
    /// blanks the effort dropdown. A known model with no effort support (e.g.
    /// Haiku) returns an empty list.
    /// </summary>
    public IReadOnlyList<string> SupportedEffortLevels(string? modelId)
    {
        ModelInfo? m = Resolve(modelId);
        return m is null ? EffortLevels.Select(e => e.Id).ToList() : m.SupportedEffortLevels;
    }

    /// <summary>Supported effort levels intersected with the persistable set (drops <c>max</c>, etc.).</summary>
    public IReadOnlyList<string> PersistableEffortLevels(string? modelId)
    {
        HashSet<string> persistable = new(PersistableEffortIds, Ord);
        return SupportedEffortLevels(modelId).Where(persistable.Contains).ToList();
    }

    public bool IsEffortSupported(string? modelId, string? effort) =>
        effort is not null && SupportedEffortLevels(modelId).Contains(effort, Ord);

    public string? DefaultEffortLevel(string? modelId) => Resolve(modelId)?.DefaultEffortLevel;

    /// <summary>Unknown/custom models report <c>false</c> (conservative — auto is not assumed).</summary>
    public bool SupportsAutoMode(string? modelId) => Resolve(modelId)?.SupportsAutoMode ?? false;

    public DefaultModeCatalogInfo? DefaultMode(string? id) =>
        id is null ? null : DefaultModes.FirstOrDefault(d => Ord.Equals(d.Id, id));

    /// <summary>
    /// The persistable effort value to keep when <paramref name="effort"/> is
    /// invalid for <paramref name="modelId"/>: the supported level nearest by
    /// ordinal distance, tie-broken toward the lower (Faster) level. Returns the
    /// input unchanged when it's already valid, and <c>null</c> when the model
    /// supports no persistable effort at all (caller should clear the setting).
    /// </summary>
    public string? NearestAnalogEffort(string? modelId, string? effort)
    {
        IReadOnlyList<string> persistable = PersistableEffortLevels(modelId);
        if (persistable.Count == 0)
        {
            return null;
        }

        if (effort is not null && persistable.Contains(effort, Ord))
        {
            return effort;
        }

        // When the requested level has no known order (custom string), prefer the
        // model's declared default if it persists, else the highest supported level.
        if (effort is null || !_effortOrder.TryGetValue(effort, out int target))
        {
            string? def = DefaultEffortLevel(modelId);
            if (def is not null && persistable.Contains(def, Ord))
            {
                return def;
            }

            return persistable.OrderByDescending(id => _effortOrder.GetValueOrDefault(id)).First();
        }

        return persistable
            .OrderBy(id => Math.Abs(_effortOrder.GetValueOrDefault(id) - target))
            .ThenBy(id => _effortOrder.GetValueOrDefault(id))
            .First();
    }

    /// <summary>
    /// Free-form model suggestions for the editable model picker, in catalog
    /// order: each model's alias (preferred) and id, plus <c>[1m]</c> variants
    /// when supported. Legacy models are omitted unless <paramref name="includeLegacy"/>.
    /// </summary>
    public IReadOnlyList<string> ModelSuggestions(bool includeLegacy = false, bool include1m = true)
    {
        List<string> result = new();
        HashSet<string> seen = new(Ord);

        void Add(string value)
        {
            if (seen.Add(value))
            {
                result.Add(value);
            }
        }

        foreach (ModelInfo m in Models)
        {
            if (m.Legacy && !includeLegacy)
            {
                continue;
            }

            string primary = m.Alias ?? m.Id;
            if (m.Alias is not null)
            {
                Add(m.Alias);
            }

            Add(m.Id);

            if (include1m && m.Supports1m)
            {
                Add(primary + "[1m]");
            }
        }

        return result;
    }

    private static string StripContextSuffix(string value)
    {
        const string suffix = "[1m]";
        return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? value[..^suffix.Length]
            : value;
    }
}
