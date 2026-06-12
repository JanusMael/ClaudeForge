using Bennewitz.Ninja.ClaudeForge.Core.Catalog;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Models;

/// <summary>
/// Default <see cref="IModelCatalogAccessor"/> — a thin, stateless projection over
/// an immutable <see cref="ModelCatalog"/>. The gating rule for default modes
/// (<see cref="IsDefaultModeAllowed"/>) is the one piece of logic the catalog
/// can't express by itself because it depends on the editing scope.
/// </summary>
internal sealed class ModelCatalogAccessor : IModelCatalogAccessor
{
    private readonly ModelCatalog _catalog;

    public ModelCatalogAccessor(ModelCatalog catalog)
    {
        _catalog = catalog;
    }

    public IReadOnlyList<ModelInfo> AllModels => _catalog.Models;

    public IReadOnlyList<DefaultModeCatalogInfo> AllDefaultModes => _catalog.DefaultModes;

    public IReadOnlyList<EffortLevelInfo> AllEffortLevels => _catalog.EffortLevels;

    public ModelInfo? Resolve(string? idOrAlias) => _catalog.Resolve(idOrAlias);

    public IReadOnlyList<string> SupportedEffortLevels(string? modelId) =>
        _catalog.SupportedEffortLevels(modelId);

    public IReadOnlyList<string> PersistableEffortLevels(string? modelId) =>
        _catalog.PersistableEffortLevels(modelId);

    public bool IsEffortSupported(string? modelId, string? effort) =>
        _catalog.IsEffortSupported(modelId, effort);

    public string? NearestAnalogEffort(string? modelId, string? effort) =>
        _catalog.NearestAnalogEffort(modelId, effort);

    public string? DefaultEffortLevel(string? modelId) => _catalog.DefaultEffortLevel(modelId);

    public bool SupportsAutoMode(string? modelId) => _catalog.SupportsAutoMode(modelId);

    public bool IsDefaultModeAllowed(string mode, string? modelId, ConfigScope scope)
    {
        DefaultModeCatalogInfo? info = _catalog.DefaultMode(mode);
        if (info is null)
        {
            return true; // unknown mode — never hide it
        }

        // Gate on the model only when it's a KNOWN auto-incapable model (e.g.
        // Haiku). An unset/unknown model is lenient — the default model is
        // auto-capable, and a hand-typed custom id shouldn't hide a valid option.
        if (info.RequiresAutoCapableModel && _catalog.Resolve(modelId) is { SupportsAutoMode: false })
        {
            return false;
        }

        if (info.UserScopeOnly && scope != ConfigScope.User)
        {
            return false;
        }

        return true;
    }

    public IReadOnlyList<string> ModelSuggestions(bool includeLegacy = false, bool include1m = true) =>
        _catalog.ModelSuggestions(includeLegacy, include1m);
}
