using Bennewitz.Ninja.AgentForge.Core.Catalog;
using Bennewitz.Ninja.AgentForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Models;

/// <summary>
/// Shared access to the bundled model catalog for consumers that don't hold a
/// full <see cref="IAgentConfigClient"/> (e.g. a view-model constructed without
/// a client in tests). The catalog is immutable and the loader memoizes, so a
/// single shared accessor is safe. Lets the app stay SDK-first — view-models
/// reach the catalog through this SDK type rather than the Core loader directly.
/// </summary>
public static class ModelCatalogProvider
{
    /// <summary>The default accessor over the bundled (overlay-merged) catalog.</summary>
    public static IModelCatalogAccessor Default { get; } =
        new ModelCatalogAccessor(ModelCatalogLoader.Load());
}
