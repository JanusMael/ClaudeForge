using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Sdk.Env;

/// <summary>
/// Default <see cref="IEnvAccessor"/> implementation.  Reads through
/// <see cref="AgentConfigClientCore.GetEffectiveNode"/> for merged
/// snapshots and <see cref="AgentConfigClientCore.GetScopeValue"/>
/// for scope-targeted reads; writes via the public <c>SetValue</c> /
/// <c>RemoveValue</c> surface so the workspace lock and Changed event
/// are honoured uniformly with every other SDK mutation path.
/// </summary>
/// <remarks>
/// Path convention: dotted JsonPath (<c>env.&lt;KEY&gt;</c>) for
/// individual var reads / writes.  This matches how every other accessor
/// writes nested fields (the permissions accessor's <c>permissions.allow</c>,
/// for instance) — the underlying SettingsWorkspace handles the
/// parent-object merge correctly.
/// </remarks>
internal sealed class EnvAccessor : IEnvAccessor
{
    private readonly AgentConfigClientCore _client;

    public EnvAccessor(AgentConfigClientCore client)
    {
        _client = client;
    }

    // ── Generic dictionary surface ────────────────────────────────────

    public string? Get(string varName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(varName);
        return _client.GetEffective<string>($"env.{varName}");
    }

    public string? GetAt(string varName, ConfigScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(varName);
        if (_client.GetScopeValue($"env.{varName}", scope) is not JsonValue jv)
        {
            return null;
        }

        return jv.TryGetValue(out string? s) ? s : null;
    }

    public void Set(string varName, string? value)
    {
        SetAt(varName, value, _client.DefaultScope);
    }

    public void SetAt(string varName, string? value, ConfigScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(varName);
        if (string.IsNullOrEmpty(value))
        {
            _client.RemoveValue($"env.{varName}", scope);
        }
        else
        {
            _client.SetValue($"env.{varName}", value, scope);
        }
    }

    public IReadOnlyDictionary<string, string> All =>
        Materialize(_client.GetEffectiveNode("env") as JsonObject);

    public IReadOnlyDictionary<string, string> AllAt(ConfigScope scope)
    {
        return Materialize(_client.GetScopeValue("env", scope) as JsonObject);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Build a fresh <see cref="Dictionary{TKey, TValue}"/> snapshot
    /// from the env JsonObject.  Non-string values (e.g. a hand-edited
    /// settings.json with <c>"env": { "FOO": 42 }</c>) are coerced to
    /// strings — Claude Code's runtime expects strings here, so the
    /// representation we expose mirrors what the runtime would consume.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Materialize(JsonObject? envObj)
    {
        if (envObj is null)
        {
            return new Dictionary<string, string>(0, StringComparer.Ordinal);
        }

        Dictionary<string, string> dict = new(envObj.Count, StringComparer.Ordinal);
        foreach ((string key, JsonNode? val) in envObj)
        {
            if (val is JsonValue jv && jv.TryGetValue(out string? s))
            {
                dict[key] = s;
            }
            else if (val is not null)
            {
                dict[key] = val.ToJsonString(); // best-effort fallback for non-string values
            }
        }

        return dict;
    }
}