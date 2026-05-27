using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk.Internal;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;

/// <summary>
/// Default <see cref="IPermissionsAccessor"/> implementation. Reads via
/// <see cref="ClaudeConfigClientCore.GetEffectiveNode"/> for merged views and
/// <see cref="ClaudeConfigClientCore.GetScopeValue"/> for scope-targeted
/// mutations; writes via the public <c>SetValue</c> / <c>RemoveValue</c>
/// surface so the workspace lock and Changed event are honoured uniformly.
/// </summary>
internal sealed class PermissionsAccessor : IPermissionsAccessor
{
    private readonly ClaudeConfigClientCore _client;

    public PermissionsAccessor(ClaudeConfigClientCore client)
    {
        _client = client;
    }

    // ── DefaultMode ──────────────────────────────────────────────────────

    public PermissionDefaultMode? DefaultMode
    {
        get
        {
            string? raw = _client.GetEffective<string>("permissions.defaultMode");
            return raw is null ? null : ParseMode(raw);
        }
        set
        {
            if (value is null)
            {
                _client.RemoveValue("permissions.defaultMode", _client.DefaultScope);
            }
            else
            {
                _client.SetValue("permissions.defaultMode", FormatMode(value.Value));
            }
        }
    }

    // ── Allow / Deny / Ask ───────────────────────────────────────────────

    public IReadOnlyList<PermissionRule> Allow => Snapshot("permissions.allow");
    public IReadOnlyList<PermissionRule> Deny => Snapshot("permissions.deny");
    public IReadOnlyList<PermissionRule> Ask => Snapshot("permissions.ask");

    public IReadOnlyList<PermissionRule> AllowAt(ConfigScope scope)
    {
        return SnapshotAt("permissions.allow", scope);
    }

    public IReadOnlyList<PermissionRule> DenyAt(ConfigScope scope)
    {
        return SnapshotAt("permissions.deny", scope);
    }

    public IReadOnlyList<PermissionRule> AskAt(ConfigScope scope)
    {
        return SnapshotAt("permissions.ask", scope);
    }

    public PermissionDefaultMode? GetDefaultModeAt(ConfigScope scope)
    {
        if (_client.GetScopeValue("permissions.defaultMode", scope) is not JsonValue jv)
        {
            return null;
        }

        if (!jv.TryGetValue(out string? raw))
        {
            return null;
        }

        return ParseMode(raw);
    }

    public void AddAllow(PermissionRule rule)
    {
        Add("permissions.allow", rule);
    }

    public void AddDeny(PermissionRule rule)
    {
        Add("permissions.deny", rule);
    }

    public void AddAsk(PermissionRule rule)
    {
        Add("permissions.ask", rule);
    }

    public bool RemoveAllow(PermissionRule rule)
    {
        return Remove("permissions.allow", rule);
    }

    public bool RemoveDeny(PermissionRule rule)
    {
        return Remove("permissions.deny", rule);
    }

    public bool RemoveAsk(PermissionRule rule)
    {
        return Remove("permissions.ask", rule);
    }

    public void Clear()
    {
        // Removing the whole "permissions" key at the default scope clears every
        // sub-field in one shot — Allow, Deny, Ask, DefaultMode, et al. Matches
        // the existing GUI's "Reset" semantics for the permissions group.
        _client.RemoveValue("permissions", _client.DefaultScope);
    }

    // ── AdditionalDirectories ────────

    public IReadOnlyList<string> AdditionalDirectories =>
        new LazyReadOnlyList<string>(() =>
            MaterializeStrings(_client.GetEffectiveNode("permissions.additionalDirectories") as JsonArray));

    public IReadOnlyList<string> AdditionalDirectoriesAt(ConfigScope scope)
    {
        return new LazyReadOnlyList<string>(() =>
            MaterializeStrings(_client.GetScopeValue("permissions.additionalDirectories", scope) as JsonArray));
    }

    public void AddAdditionalDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        JsonArray? existing =
            _client.GetScopeValue("permissions.additionalDirectories", _client.DefaultScope) as JsonArray;
        JsonArray newArr = existing is not null ? (JsonArray)existing.DeepClone() : new JsonArray();

        // De-dup — schema declares the array uniqueItems:true.
        if (ContainsString(newArr, path))
        {
            return;
        }

        newArr.Add(JsonValue.Create(path));
        _client.SetValue("permissions.additionalDirectories", newArr);
    }

    public bool RemoveAdditionalDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (_client.GetScopeValue("permissions.additionalDirectories", _client.DefaultScope) is not JsonArray existing)
        {
            return false;
        }

        JsonArray newArr = (JsonArray)existing.DeepClone();
        bool removed = false;
        for (int i = newArr.Count - 1; i >= 0; i--)
        {
            if (newArr[i] is JsonValue jv && jv.TryGetValue(out string? s) && s == path)
            {
                newArr.RemoveAt(i);
                removed = true;
            }
        }

        if (!removed)
        {
            return false;
        }

        if (newArr.Count == 0)
        {
            _client.RemoveValue("permissions.additionalDirectories", _client.DefaultScope);
        }
        else
        {
            _client.SetValue("permissions.additionalDirectories", newArr);
        }

        return true;
    }

    // ── DisableBypassPermissionsMode ─

    public bool? DisableBypassPermissionsMode
    {
        get => _client.GetEffective<bool?>("permissions.disableBypassPermissionsMode");
        set
        {
            if (value is null)
            {
                _client.RemoveValue("permissions.disableBypassPermissionsMode", _client.DefaultScope);
            }
            else
            {
                _client.SetValue("permissions.disableBypassPermissionsMode", value.Value);
            }
        }
    }

    public bool? GetDisableBypassPermissionsModeAt(ConfigScope scope)
    {
        if (_client.GetScopeValue("permissions.disableBypassPermissionsMode", scope) is not JsonValue jv)
        {
            return null;
        }

        return jv.TryGetValue(out bool b) ? b : null;
    }

    private static IReadOnlyList<string> MaterializeStrings(JsonArray? arr)
    {
        if (arr is null)
        {
            return Array.Empty<string>();
        }

        List<string> result = new(arr.Count);
        foreach (JsonNode? item in arr)
        {
            if (item is JsonValue jv && jv.TryGetValue(out string? s))
            {
                result.Add(s);
            }
        }

        return result;
    }

    private static bool ContainsString(JsonArray arr, string value)
    {
        foreach (JsonNode? item in arr)
        {
            if (item is JsonValue jv && jv.TryGetValue(out string? s) && s == value)
            {
                return true;
            }
        }

        return false;
    }

    // ── Internals ────────────────────────────────────────────────────────

    private IReadOnlyList<PermissionRule> Snapshot(string path)
    {
        return new LazyReadOnlyList<PermissionRule>(() => MaterializeFrom(_client.GetEffectiveNode(path) as JsonArray));
    }

    private IReadOnlyList<PermissionRule> SnapshotAt(string path, ConfigScope scope)
    {
        return new LazyReadOnlyList<PermissionRule>(() =>
            MaterializeFrom(_client.GetScopeValue(path, scope) as JsonArray));
    }

    private static IReadOnlyList<PermissionRule> MaterializeFrom(JsonArray? arr)
    {
        if (arr is null)
        {
            return Array.Empty<PermissionRule>();
        }

        List<PermissionRule> result = new(arr.Count);
        foreach (JsonNode? item in arr)
        {
            if (item is JsonValue jv && jv.TryGetValue(out string? s))
            {
                result.Add(new PermissionRule(s));
            }
        }

        return result;
    }

    private void Add(string path, PermissionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        // Read THE DEFAULT SCOPE's current array (not the merged effective view)
        // so we mutate exactly the user's slice. Append, dedup, write back.
        JsonArray? existing = _client.GetScopeValue(path, _client.DefaultScope) as JsonArray;
        JsonArray newArr = existing is not null ? (JsonArray)existing.DeepClone() : new JsonArray();

        // De-dup by string equality. The schema treats permission rules as a set;
        // adding a rule that's already present is a no-op rather than a duplicate.
        if (ContainsRule(newArr, rule.Value))
        {
            return;
        }

        newArr.Add(JsonValue.Create(rule.Value));
        _client.SetValue(path, newArr);
    }

    private bool Remove(string path, PermissionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (_client.GetScopeValue(path, _client.DefaultScope) is not JsonArray existing)
        {
            return false;
        }

        JsonArray newArr = (JsonArray)existing.DeepClone();
        bool removed = false;
        for (int i = newArr.Count - 1; i >= 0; i--)
        {
            if (newArr[i] is JsonValue jv && jv.TryGetValue(out string? s) && s == rule.Value)
            {
                newArr.RemoveAt(i);
                removed = true;
            }
        }

        if (!removed)
        {
            return false;
        }

        if (newArr.Count == 0)
        {
            // Drop the empty array key entirely — keeps the on-disk shape clean
            // and matches what the GUI's existing PermissionsEditorViewModel does.
            _client.RemoveValue(path, _client.DefaultScope);
        }
        else
        {
            _client.SetValue(path, newArr);
        }

        return true;
    }

    private static bool ContainsRule(JsonArray arr, string value)
    {
        foreach (JsonNode? item in arr)
        {
            if (item is JsonValue jv && jv.TryGetValue(out string? s) && s == value)
            {
                return true;
            }
        }

        return false;
    }

    // ── Enum mapping ─────────────────────────────────────────────────────
    //
    // The CLI accepts camelCase strings; the SDK enum uses PascalCase. Bidirectional
    // mapping is hand-written rather than relying on JsonStringEnumConverter so
    // the mapping is trim-safe and visible to readers.

    private static PermissionDefaultMode? ParseMode(string raw)
    {
        return raw switch
        {
            "default" => PermissionDefaultMode.Default,
            "acceptEdits" => PermissionDefaultMode.AcceptEdits,
            "plan" => PermissionDefaultMode.Plan,
            "auto" => PermissionDefaultMode.Auto,
            "dontAsk" => PermissionDefaultMode.DontAsk,
            "bypassPermissions" => PermissionDefaultMode.BypassPermissions,
            "delegate" => PermissionDefaultMode.Delegate,
            var _ => null,
        };
    }

    private static string FormatMode(PermissionDefaultMode mode)
    {
        return mode switch
        {
            PermissionDefaultMode.Default => "default",
            PermissionDefaultMode.AcceptEdits => "acceptEdits",
            PermissionDefaultMode.Plan => "plan",
            PermissionDefaultMode.Auto => "auto",
            PermissionDefaultMode.DontAsk => "dontAsk",
            PermissionDefaultMode.BypassPermissions => "bypassPermissions",
            PermissionDefaultMode.Delegate => "delegate",
            var _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown PermissionDefaultMode"),
        };
    }
}