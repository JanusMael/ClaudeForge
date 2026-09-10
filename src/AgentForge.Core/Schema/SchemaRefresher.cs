using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Serilog;

namespace Bennewitz.Ninja.AgentForge.Core.Schema;

/// <summary>
/// What one product's schema check found.
/// </summary>
public enum SchemaRefreshStatus
{
    /// <summary>Upstream answered and its copy hashes to what was already loaded.</summary>
    Unchanged,

    /// <summary>Upstream answered with a copy that differs from the one that was loaded.</summary>
    Updated,

    /// <summary>
    /// Upstream did not answer, so the load fell back to the bundled copy.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>This is not merely "nothing happened".</b> <see cref="SchemaRegistry.RefreshAsync"/>
    /// drops the cached copy before re-fetching, so a session that had a fetched schema and
    /// then failed to reach the network is left on the bundled one. Pressing the button can
    /// therefore move a registry backwards, and the surface must say so rather than reporting
    /// a bland "no updates".
    /// </remarks>
    Unavailable,

    /// <summary>No source could supply the schema at all.</summary>
    Failed,
}

/// <summary>One product's result from <see cref="SchemaRefresher.RefreshAsync"/>.</summary>
public sealed record SchemaRefreshResult(
    ProductDescriptor Product,
    SchemaRefreshStatus Status,
    SchemaProvenance? Provenance,
    string? Error);

/// <summary>
/// Re-fetches a set of products' schemas and reports, per product, whether upstream had
/// anything new. Backs the apps' <em>Check for schema updates</em> action.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>This mutates the registry it is given.</b> The check is performed by actually
/// re-loading, because that is the only way to learn what upstream is serving — and because
/// under network-first the freshly loaded copy is the one the app would use on its next
/// launch anyway. The consequence is that the pages already on screen were built from the
/// PREVIOUS copy: when anything comes back <see cref="SchemaRefreshStatus.Updated"/> the
/// surface must tell the user their editors do not yet reflect it.
/// </para>
/// <para>
/// ⛔ In an app that shares one registry between its window and its SDK clients — ClaudeForge
/// does — save-validation switches to the new copy immediately while the editors do not. A
/// tightened upstream constraint can therefore fail a save against a rule the visible tree
/// never showed. It surfaces as a validation error rather than silently, and reloading
/// resolves it, but it is the reason the result text says to reload rather than shrugging.
/// </para>
/// <para>
/// ⓘ Sequential, not parallel. There are two or three small documents per app, the
/// per-product log lines stay in a readable order, and a burst of concurrent requests to the
/// same host buys nothing measurable here.
/// </para>
/// </remarks>
public static class SchemaRefresher
{
    /// <summary>
    /// Whether a product has anywhere to check. <see langword="false"/> for a hand-maintained
    /// schema such as Claude Desktop's, whose descriptor URL is <c>bundled://…</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ Same test the loader applies, and deliberately so: a product this returns
    /// <see langword="false"/> for is one <see cref="SchemaRegistry.GetSchemaAsync"/> would
    /// never fetch either. Reporting it as "up to date" would imply a check that never
    /// happened, so <see cref="RefreshAsync"/> omits it from the results entirely.
    /// </remarks>
    public static bool IsCheckable(ProductDescriptor product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return product.SchemaUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Re-fetch each checkable product's schema and report what changed.
    /// </summary>
    /// <returns>
    /// One result per <em>checkable</em> product, in the order given. Products with no
    /// upstream are absent rather than present-and-unchanged.
    /// </returns>
    public static async Task<IReadOnlyList<SchemaRefreshResult>> RefreshAsync(
        SchemaRegistry registry,
        IEnumerable<ProductDescriptor> products,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(products);

        List<SchemaRefreshResult> results = [];

        foreach (ProductDescriptor product in products)
        {
            if (!IsCheckable(product))
            {
                continue;
            }

            // Captured BEFORE the refresh drops it. Null when this registry has not loaded
            // the schema yet, which makes the first check report Updated — correct, since
            // nothing was there to be unchanged from.
            string? before = registry.ProvenanceFor(product.SchemaFileName)?.ShortSha;

            try
            {
                _ = await registry
                    .RefreshAsync(product.SchemaUrl, product.SchemaFileName, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The caller's cancellation, not a schema problem. Let it out.
                throw;
            }
            catch (Exception ex) when (ex is SchemaUnavailableException
                                           or UnstrippableSchemaRefException
                                           or JsonException)
            {
                Log.Warning(ex, "[SchemaRefresh] {Product} could not be reloaded", product.Id);
                results.Add(new SchemaRefreshResult(product, SchemaRefreshStatus.Failed, null, ex.Message));
                continue;
            }

            SchemaProvenance? after = registry.ProvenanceFor(product.SchemaFileName);

            SchemaRefreshStatus status = after switch
            {
                // Materialise always records provenance for a copy it produced, so this
                // arm means the registry returned a schema it did not record — a contract
                // break rather than a network outcome, and not something to report as "fine".
                null => SchemaRefreshStatus.Failed,

                // The fetch step swallows connectivity failures and falls through to bundled,
                // so a bundled result here IS the "could not reach upstream" case. It cannot
                // mean anything else: this product was checkable, so bundled was not the
                // preferred source.
                { Source: SchemaSource.Bundled } => SchemaRefreshStatus.Unavailable,

                { ShortSha: var sha } when sha == before => SchemaRefreshStatus.Unchanged,

                var _ => SchemaRefreshStatus.Updated,
            };

            Log.Information(
                "[SchemaRefresh] {Product}: {Status} (was {Before}, now {After})",
                product.Id, status, before ?? "—", after?.ShortSha ?? "—");

            results.Add(new SchemaRefreshResult(product, status, after, null));
        }

        return results;
    }
}
