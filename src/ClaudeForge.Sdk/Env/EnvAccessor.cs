using System.Globalization;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Env;

/// <summary>
/// Default <see cref="IEnvAccessor"/> implementation.  Reads through
/// <see cref="ClaudeConfigClientCore.GetEffectiveNode"/> for merged
/// snapshots and <see cref="ClaudeConfigClientCore.GetScopeValue"/>
/// for scope-targeted reads; writes via the public <c>SetValue</c> /
/// <c>RemoveValue</c> surface so the workspace lock and Changed event
/// are honoured uniformly with every other SDK mutation path.
/// </summary>
/// <remarks>
/// Path convention: dotted JsonPath (<c>env.&lt;KEY&gt;</c>) for
/// individual var reads / writes.  This matches how
/// <see cref="Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.PermissionsAccessor"/> writes
/// nested fields like <c>permissions.allow</c> — the underlying
/// SettingsWorkspace handles the parent-object merge correctly.
/// </remarks>
internal sealed class EnvAccessor : IEnvAccessor
{
    private readonly ClaudeConfigClientCore _client;

    public EnvAccessor(ClaudeConfigClientCore client)
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

    // ── Typed convenience properties for well-known keys ──────────────

    public int? MaxThinkingTokens
    {
        get => ParseInt(Get(EnvVarKey.MaxThinkingTokens));
        set => Set(EnvVarKey.MaxThinkingTokens, FormatInt(value));
    }

    public int? MaxOutputTokens
    {
        get => ParseInt(Get(EnvVarKey.MaxOutputTokens));
        set => Set(EnvVarKey.MaxOutputTokens, FormatInt(value));
    }

    public bool? DisableAutoMemory
    {
        get => ParseBool01(Get(EnvVarKey.DisableAutoMemory));
        set => Set(EnvVarKey.DisableAutoMemory, FormatBool01(value));
    }

    public bool? DisableAutoUpdater
    {
        get => ParseBool01(Get(EnvVarKey.DisableAutoUpdater));
        set => Set(EnvVarKey.DisableAutoUpdater, FormatBool01(value));
    }

    public string? AnthropicModel
    {
        get => Get(EnvVarKey.AnthropicModel);
        set => Set(EnvVarKey.AnthropicModel, value);
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

    /// <summary>
    /// Parse the stored env-string as a base-10 int.  Lenient: returns
    /// null on whitespace, missing key, or unparseable value.  Does not
    /// throw — callers of typed convenience properties should not have
    /// to defend against hand-edited bad values.
    /// </summary>
    private static int? ParseInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? n
            : null;
    }

    private static string? FormatInt(int? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// Parse the Claude Code <c>"1"</c> / <c>"0"</c> tri-state boolean
    /// convention.  Anything else (including <c>"true"</c> / <c>"false"</c>
    /// — those aren't what Claude Code uses) yields null.  Strict so a
    /// hand-edited typo doesn't masquerade as a deliberate <c>false</c>.
    /// </summary>
    private static bool? ParseBool01(string? raw)
    {
        return raw switch
        {
            "1" => true,
            "0" => false,
            var _ => null,
        };
    }

    private static string? FormatBool01(bool? value)
    {
        return value switch
        {
            true => "1",
            false => "0",
            null => null,
        };
    }
}