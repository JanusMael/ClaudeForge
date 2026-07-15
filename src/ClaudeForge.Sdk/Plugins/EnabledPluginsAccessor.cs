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
/// booleans OR (per the schema's anyOf) an array-of-strings selecting specific
/// plugin components. The accessor projects each key/value pair into a flat
/// <see cref="EnabledPlugin"/> record — the array form populates
/// <see cref="EnabledPlugin.Components"/>.
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

        return Project(pluginRef, obj[pluginRef]);
    }

    public void Set(EnabledPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentException.ThrowIfNullOrEmpty(plugin.PluginRef);

        JsonObject root = (_client.GetScopeValue(TopKey, _client.DefaultScope) as JsonObject)?.DeepClone() as JsonObject
                          ?? new JsonObject();
        root[plugin.PluginRef] = ToNode(plugin);
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
            if (Project(key, value) is { } plugin)
            {
                result.Add(plugin);
            }
        }

        return result;
    }

    /// <summary>
    /// Projects one <c>enabledPlugins</c> key/value pair into an
    /// <see cref="EnabledPlugin"/>. A bool maps to <see cref="EnabledPlugin.Enabled"/>;
    /// the schema's array-of-strings form maps to <see cref="EnabledPlugin.Components"/>
    /// (Enabled = true). Returns <see langword="null"/> for null / <c>not:{}</c> /
    /// otherwise-unrepresentable values so they are skipped rather than coerced.
    /// </summary>
    private static EnabledPlugin? Project(string key, JsonNode? value)
    {
        return value switch
        {
            JsonValue jv when jv.TryGetValue(out bool enabled) => new EnabledPlugin(key, enabled),
            JsonArray arr => new EnabledPlugin(key, Enabled: true, Components: ReadStringItems(arr)),
            var _ => null,
        };
    }

    private static IReadOnlyList<string> ReadStringItems(JsonArray arr)
    {
        List<string> items = new(arr.Count);
        foreach (JsonNode? item in arr)
        {
            if (item is JsonValue iv && iv.TryGetValue(out string? s) && s is not null)
            {
                items.Add(s);
            }
        }

        return items;
    }

    private static JsonNode ToNode(EnabledPlugin plugin)
    {
        if (plugin.Components is not null)
        {
            JsonArray arr = new();
            foreach (string component in plugin.Components)
            {
                // Cast to JsonNode? — JsonArray.Add<T>(T) is IL2026 under trim.
                arr.Add((JsonNode?)JsonValue.Create(component));
            }

            return arr;
        }

        return JsonValue.Create(plugin.Enabled)!;
    }
}