using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

namespace Bennewitz.Ninja.ClaudeForge.Adapters;

/// <summary>
/// Registers the Claude-specific specialized editors (hooks, MCP servers, permissions, plugins, marketplaces)
/// into a <see cref="CompositeEditorFactory"/>.
/// </summary>
/// <remarks>
/// Call <see cref="Register"/> or <see cref="CreateDefault"/> once per
/// product section at startup. Matching is by schema name; names are
/// Claude-specific and therefore live in App, not the library.
/// <para>
/// As editors migrate onto the SDK accessors the factory
/// closure injects the per-product <see cref="IClaudeConfigClient"/> so the
/// migrated editor can read typed records via <c>client.Plugins</c> /
/// <c>client.Hooks</c> / etc. The <paramref name="sdkClient"/> parameter is
/// optional: legacy registrations still work, and tests / fixtures that
/// don't construct an SDK client get the pre-migration fallback path.
/// </para>
/// </remarks>
public static class ClaudeEditorFactoryConfig
{
    /// <summary>
    /// Apply Claude-specific editor registrations to an existing <paramref name="factory"/>.
    /// Matchers are tried in registration order (hooks → mcp → permissions).
    /// </summary>
    /// <param name="factory">The factory to extend.</param>
    /// <param name="sdkClient">
    /// Optional SDK client for migrated editors. When non-null, the migrated
    /// editor receives the client and routes its load / save through the
    /// strongly-typed accessor surface. When <c>null</c>, the editor falls
    /// back to its legacy <c>JsonNode</c>-based load path.
    /// </param>
    public static void Register(CompositeEditorFactory factory, ClaudeConfigClientCore? sdkClient = null)
    {
        // Migrated onto SDK accessor.
        factory.Register(
            s => s.Name == "hooks",
            (s, scope) => new HooksEditorViewModel(s, scope, sdkClient));

        // Migrated onto SDK accessor.
        factory.Register(
            s => s.Name == "mcpServers",
            (s, scope) => new McpServersEditorViewModel(s, scope, sdkClient));

        // Migrated onto SDK accessor: when an SDK client is
        // supplied, the editor reads its initial state via client.Plugins.GetAt(scope)
        // instead of decoding the raw JsonObject. Writes still flow through the
        // legacy SettingsGroupEditorViewModel live-write loop pending the
        // _selfWriting plumbing migration in 4.3.6c.
        factory.Register(
            s => s.Name == "enabledPlugins",
            (s, scope) => new EnabledPluginsEditorViewModel(s, scope, sdkClient));

        // Migrated onto SDK accessor. See EnabledPlugins
        // factory comment for the matching pattern.
        factory.Register(
            s => s.Name == "extraKnownMarketplaces",
            (s, scope) => new MarketplacesEditorViewModel(s, scope, sdkClient));

        // Migrated onto SDK accessor. Final editor in the
        // 4.3.6 series; every specialized editor now drives reads through
        // the typed accessor surface.
        // The factory is threaded in so the editor can auto-surface every
        // permissions schema key it does not render bespoke-ly (e.g.
        // disableAutoMode) via the generic by-type / raw-JSON editors —
        // no schema property is silently dropped. Today's permissions children
        // (allow/deny/ask/defaultMode/disableBypassPermissionsMode/
        // disableAutoMode/additionalDirectories) match no registered bespoke
        // name, so the composite Create falls through to generic dispatch — no
        // re-entrancy TODAY. A future permissions child whose name DID match a
        // bespoke registration would be constructed here; verify that case is
        // safe before adding such a registration (the Phase-4 rollout should
        // consider routing auto-surfaced children through base/generic dispatch).
        factory.Register(
            s => s.Name == "permissions",
            (s, scope) => new PermissionsEditorViewModel(s, scope, sdkClient, factory));
    }

    /// <summary>
    /// Create a <see cref="CompositeEditorFactory"/> with Claude-specific editors pre-registered.
    /// </summary>
    /// <param name="sdkClient">
    /// Optional SDK client to inject into migrated editors. See
    /// <see cref="Register"/> for the migration semantics.
    /// </param>
    public static CompositeEditorFactory CreateDefault(ClaudeConfigClientCore? sdkClient = null)
    {
        CompositeEditorFactory factory = new();
        Register(factory, sdkClient);
        return factory;
    }
}