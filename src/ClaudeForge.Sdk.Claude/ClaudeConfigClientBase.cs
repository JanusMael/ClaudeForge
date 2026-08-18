using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Hooks;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Marketplaces;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Models;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Plugins;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

/// <summary>
/// The Claude half of the client: <see cref="AgentConfigClientCore"/>'s
/// product-neutral machinery plus the five Claude-only accessors that make up
/// <see cref="IClaudeConfigClient"/>. Both concrete Claude clients derive from
/// this and supply only file discovery, the schema discriminator, and the backup
/// client.
/// </summary>
/// <remarks>
/// <para>
/// This class exists so the two clients share one copy of the accessor wiring.
/// It sits between them and the shared core rather than merging into either,
/// which is what keeps the dependency one-directional:
/// <c>ClaudeForge.Sdk.Claude</c> references <c>AgentForge.Sdk</c> and never the
/// reverse, so a second agent product can derive its own equivalent from the same
/// core without inheriting any of this.
/// </para>
/// <para>
/// Accessors are created lazily and cached, matching the core's treatment of
/// <see cref="AgentConfigClientCore.McpServers"/> and
/// <see cref="AgentConfigClientCore.Env"/>. They are not thread-safe to
/// <i>create</i> concurrently — a benign race can construct two instances and
/// discard one — but the accessors themselves are stateless projections over the
/// workspace, so every read and write still serializes on the core's lock.
/// </para>
/// </remarks>
public abstract class ClaudeConfigClientBase : AgentConfigClientCore, IClaudeConfigClient
{
    /// <inheritdoc cref="AgentConfigClientCore(ConfigScope, SchemaRegistry?, SettingsWorkspace?, IConfigWriter?)"/>
    protected ClaudeConfigClientBase(
        ConfigScope defaultScope,
        SchemaRegistry? schemaRegistry,
        SettingsWorkspace? preLoadedWorkspace = null,
        IConfigWriter? configWriter = null)
        : base(defaultScope, schemaRegistry, preLoadedWorkspace, configWriter)
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Overridden here rather than per-client because both Claude products merge the same
    /// way: Desktop's config declares no array paths at all, so Claude Code's list cannot
    /// match one of its documents. <see cref="AgentConfigClientCore.Product"/> stays
    /// per-client because the two products genuinely name different schemas.
    /// </remarks>
    protected override IMergePolicy MergePolicy => ClaudeMergePolicy.Instance;

    private IPermissionsAccessor? _permissionsAccessor;
    private IHooksAccessor? _hooksAccessor;
    private IMarketplacesAccessor? _marketplacesAccessor;
    private IEnabledPluginsAccessor? _pluginsAccessor;

    /// <inheritdoc/>
    public IPermissionsAccessor Permissions => _permissionsAccessor ??= new PermissionsAccessor(this);

    /// <inheritdoc/>
    public IHooksAccessor Hooks => _hooksAccessor ??= new HooksAccessor(this);

    /// <inheritdoc/>
    public IMarketplacesAccessor Marketplaces => _marketplacesAccessor ??= new MarketplacesAccessor(this);

    /// <inheritdoc/>
    public IEnabledPluginsAccessor Plugins => _pluginsAccessor ??= new EnabledPluginsAccessor(this);

    /// <inheritdoc/>
    public IModelCatalogAccessor Models => ModelCatalogProvider.Default;

    // ── Schema-declared hook vocabulary ──────────────────────────────────────

    /// <summary>
    /// Hook lifecycle events from the currently-loaded settings schema — each
    /// event's name plus its schema description (the <c>hooks</c> node's child
    /// properties). The fresh, schema-driven set the GUI's schema tree is built
    /// from too. Empty before <see cref="AgentConfigClientCore.OpenAsync"/> or when
    /// the schema exposes no <c>hooks.properties</c>. Consumed by the Hooks
    /// accessor's <c>KnownEvents</c> so headless callers and the editor share one
    /// source of truth — including the descriptions, not just the names.
    /// </summary>
    internal IReadOnlyList<HookEventInfo> SchemaHookEvents()
    {
        SchemaNode? hooks = CachedSchemaNodes?.FirstOrDefault(n =>
            string.Equals(n.Name, "hooks", StringComparison.Ordinal));
        if (hooks is not null)
        {
            return hooks.Properties.Select(p => new HookEventInfo(p.Name, p.Description)).ToList();
        }

        // No cached schema tree — the client was constructed via FromExistingWorkspace
        // (the GUI's path) and never ran OpenAsync, so CachedSchemaNodes is null. Read the
        // event names + descriptions straight from the bundled schema (same source, same
        // descriptions) so KnownEvents — and thus the editor's per-event tooltips/labels —
        // stay populated regardless of how the client was built. Mirrors SchemaHookCommandVariants.
        // No product test needed: GetHookEvents returns empty for a schema with no hooks
        // section, which is exactly what Desktop's is. The former `IsClaudeCode ? … : []`
        // hardcoded that answer instead of reading it.
        return SchemaRegistry.GetHookEvents(Product.SchemaFileName);
    }

    /// <summary>
    /// Hook command variants from the settings schema's <c>$defs.hookCommand.anyOf</c> —
    /// each variant's <c>type</c> discriminator, description, and field descriptions. Read
    /// from the bundled merged schema JSON because the <c>anyOf</c> variants don't survive the
    /// flattened <see cref="SchemaNode"/> tree the GUI builds from (unlike <see cref="SchemaHookEvents"/>,
    /// which reads that tree); the bundled schema is the same source the tree derives from, so
    /// they stay consistent. Empty for Claude Desktop — hooks are a Claude Code concept.
    /// Consumed by the Hooks accessor's <c>KnownCommandTypes</c> so headless callers and the editor
    /// share one source for the per-type picker text and per-field descriptions.
    /// </summary>
    internal IReadOnlyList<HookCommandVariantInfo> SchemaHookCommandVariants() =>
        SchemaRegistry.GetHookCommandVariants(Product.SchemaFileName);
}
