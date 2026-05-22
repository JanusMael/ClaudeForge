using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk.Internal;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Plugins;

/// <summary>
/// Default <see cref="IEnabledPluginsAccessor"/> implementation.
/// </summary>
/// <remarks>
/// On-disk shape is a top-level <c>"enabledPlugins"</c> object whose keys are
/// plugin identifiers in <c>plugin@marketplace</c> form and whose values are
/// booleans. The accessor projects each key/value pair into a flat
/// <see cref="EnabledPlugin"/> record.
/// </remarks>
internal sealed class EnabledPluginsAccessor : IEnabledPluginsAccessor
{
    private const string TopKey = "enabledPlugins";

    private readonly ClaudeConfigClientCore _client;

    public EnabledPluginsAccessor(ClaudeConfigClientCore client)
    {
        _client = client;
    }

    public IReadOnlyList<EnabledPlugin> All =>
        new LazyReadOnlyList<EnabledPlugin>(Materialize);

    public IReadOnlyList<EnabledPlugin> GetAt(ConfigScope scope)
    {
        return new LazyReadOnlyList<EnabledPlugin>(() => MaterializeAt(scope));
    }

    public EnabledPlugin? Get(string pluginRef)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginRef);
        if (_client.GetEffectiveNode(TopKey) is not JsonObject obj)
        {
            return null;
        }

        if (obj[pluginRef] is not JsonValue jv)
        {
            return null;
        }

        if (!jv.TryGetValue(out bool enabled))
        {
            return null;
        }

        return new EnabledPlugin(pluginRef, enabled);
    }

    public void Set(EnabledPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentException.ThrowIfNullOrEmpty(plugin.PluginRef);

        JsonObject root = (_client.GetScopeValue(TopKey, _client.DefaultScope) as JsonObject)?.DeepClone() as JsonObject
                          ?? new JsonObject();
        root[plugin.PluginRef] = plugin.Enabled;
        _client.SetValue(TopKey, root);
    }

    public bool Remove(string pluginRef)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginRef);
        if (_client.GetScopeValue(TopKey, _client.DefaultScope) is not JsonObject existing)
        {
            return false;
        }

        if (!existing.ContainsKey(pluginRef))
        {
            return false;
        }

        JsonObject root = (JsonObject)existing.DeepClone();
        root.Remove(pluginRef);

        if (root.Count == 0)
        {
            _client.RemoveValue(TopKey, _client.DefaultScope);
        }
        else
        {
            _client.SetValue(TopKey, root);
        }

        return true;
    }

    public void Clear()
    {
        _client.RemoveValue(TopKey, _client.DefaultScope);
    }

    // ── Internals ────────────────────────────────────────────────────────

    private IReadOnlyList<EnabledPlugin> Materialize()
    {
        return MaterializeFrom(_client.GetEffectiveNode(TopKey) as JsonObject);
    }

    private IReadOnlyList<EnabledPlugin> MaterializeAt(ConfigScope scope)
    {
        return MaterializeFrom(_client.GetScopeValue(TopKey, scope) as JsonObject);
    }

    private static IReadOnlyList<EnabledPlugin> MaterializeFrom(JsonObject? obj)
    {
        if (obj is null)
        {
            return Array.Empty<EnabledPlugin>();
        }

        List<EnabledPlugin> result = new(obj.Count);
        foreach ((string key, JsonNode? value) in obj)
        {
            if (value is JsonValue jv && jv.TryGetValue(out bool enabled))
            {
                result.Add(new EnabledPlugin(key, enabled));
            }
        }

        return result;
    }
}