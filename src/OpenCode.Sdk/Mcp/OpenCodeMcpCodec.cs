using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.OpenCode.Sdk.Mcp;

/// <summary>
/// Reads and writes the <c>mcp</c> map in the editor library's value currency:
/// <c>null | bool | string | long | double | IReadOnlyList&lt;object?&gt; |
/// IReadOnlyDictionary&lt;string, object?&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>Nothing here drops an entry it does not understand.</b> An unrecognised server keeps its
/// value verbatim in <see cref="OpenCodeMcpServer.Raw"/> and is written back byte-for-byte, and
/// unsurfaced fields on a recognised server survive in
/// <see cref="OpenCodeMcpServer.Extras"/>.
/// </para>
/// <para>
/// ⛔ <b>The plan told me to copy <c>MarketplaceListEditorViewModel</c> for this, on the stated
/// grounds that it "echoes an unknown variant back unchanged rather than dropping it". It does
/// not.</b> Its <c>TryHydrateEntry</c> returns <see langword="null"/> for an unknown
/// <c>source</c> and the caller does <c>continue</c>; its <c>ToVariantObject</c> carries the
/// comment <c>// unknown source — drop on save</c>. So the row vanishes on load and again on
/// save. The plan's <i>reasoning</i> was right and its evidence was wrong, which is the more
/// dangerous combination — the recommended template has the opposite of the recommended
/// behaviour. What it genuinely does have is per-variant field preservation across a variant
/// switch, via its <c>ExtraFields</c>.
/// </para>
/// <para>
/// So the preservation pattern here follows the permission grid instead, which holds a value it
/// cannot parse and echoes it back — except at <b>per-entry</b> granularity rather than
/// whole-value. One server written by a newer OpenCode should not make the other twelve
/// read-only.
/// </para>
/// <para>
/// Deliberately no <c>JsonNode</c> in any signature. The currency conversion has no case for one,
/// so a <c>JsonNode</c> handed back to the editor library is stringified — a whole MCP block would
/// land in the user's config as a single quoted string.
/// </para>
/// </remarks>
public static class OpenCodeMcpCodec
{
    /// <summary>Fields <see cref="OpenCodeMcpServer"/> surfaces for a local server.</summary>
    private static readonly HashSet<string> LocalKeys = new(StringComparer.Ordinal)
    {
        "type", "command", "cwd", "environment", "enabled", "timeout",
    };

    /// <summary>Fields <see cref="OpenCodeMcpServer"/> surfaces for a remote server.</summary>
    private static readonly HashSet<string> RemoteKeys = new(StringComparer.Ordinal)
    {
        "type", "url", "headers", "oauth", "enabled", "timeout",
    };

    /// <summary>
    /// Parse the whole <c>mcp</c> map, preserving entry order.
    /// </summary>
    /// <param name="value">The value at the editing scope, or <see langword="null"/>.</param>
    /// <returns>Server entries keyed by name, in file order. Empty when there is nothing to read.</returns>
    /// <remarks>
    /// ⚠ Order comes from the map passed in, which this type cannot verify:
    /// <see cref="Dictionary{TKey,TValue}"/> promises no enumeration order. Callers must hand over
    /// a map that enumerates in file order — the shell's currency conversion does.
    /// </remarks>
    public static IReadOnlyList<KeyValuePair<string, OpenCodeMcpServer>> ReadMap(object? value)
    {
        if (value is not IReadOnlyDictionary<string, object?> map)
        {
            return [];
        }

        List<KeyValuePair<string, OpenCodeMcpServer>> servers = [];
        foreach ((string name, object? entry) in map)
        {
            servers.Add(new KeyValuePair<string, OpenCodeMcpServer>(name, ReadServer(entry)));
        }

        return servers;
    }

    /// <summary>
    /// Classify and read one server entry.
    /// </summary>
    /// <remarks>
    /// Permissive on read by design. A <c>local</c> entry missing its required <c>command</c> is
    /// still read as local with an empty command, so the editor can show it and the user can fix
    /// it. Treating it as unrecognised would make the one thing they need to repair the one thing
    /// they cannot touch.
    /// </remarks>
    public static OpenCodeMcpServer ReadServer(object? value)
    {
        if (value is not IReadOnlyDictionary<string, object?> map)
        {
            return new OpenCodeMcpServer { Kind = OpenCodeMcpKind.Unrecognised, Raw = value };
        }

        string? type = map.TryGetValue("type", out object? t) ? t as string : null;

        return type switch
        {
            "local" => ReadLocal(map),
            "remote" => ReadRemote(map),
            // No discriminator. A lone `enabled` is the schema's third arm — a toggle for a server
            // some other scope declares. Anything else is not ours to reshape.
            null when IsEnabledOnly(map) => new OpenCodeMcpServer
            {
                Kind = OpenCodeMcpKind.EnabledOverride,
                Enabled = map["enabled"] as bool?,
            },
            // A `type` this build has never heard of is exactly the forward-compatibility case:
            // held verbatim, never guessed at.
            var _ => new OpenCodeMcpServer { Kind = OpenCodeMcpKind.Unrecognised, Raw = value },
        };
    }

    /// <summary>
    /// Serialise the whole map, or <see langword="null"/> when there is nothing to write.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> rather than an empty object, so the workspace removes the key instead
    /// of persisting an <c>"mcp": {}</c> that configures nothing. Entries with a blank name are
    /// skipped — a half-added row is not yet a server.
    /// </remarks>
    public static object? WriteMap(IReadOnlyList<KeyValuePair<string, OpenCodeMcpServer>> servers)
    {
        ArgumentNullException.ThrowIfNull(servers);

        OrderedPropertyMap map = new();
        foreach ((string name, OpenCodeMcpServer server) in servers)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            map.Set(name, WriteServer(server));
        }

        return map.Count == 0 ? null : map;
    }

    /// <summary>
    /// Serialise one entry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ An <see cref="OpenCodeMcpKind.Unrecognised"/> entry returns
    /// <see cref="OpenCodeMcpServer.Raw"/> — the value exactly as read. That is the whole point:
    /// a server this build cannot classify survives a save untouched instead of disappearing.
    /// </para>
    /// <para>
    /// Surfaced keys are emitted in the schema's own declaration order, so repeated saves are
    /// stable. ⚠ <b>That does mean the first save can reorder the keys of an entry a human wrote
    /// by hand.</b> Accepted deliberately: an <c>mcp</c> entry has at most six keys and their order
    /// carries no meaning, whereas <c>environment</c> and <c>headers</c> — the maps big enough for
    /// a reshuffle to make a diff unreadable — do keep their file order.
    /// </para>
    /// </remarks>
    public static object? WriteServer(OpenCodeMcpServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (server.Kind == OpenCodeMcpKind.Unrecognised)
        {
            return server.Raw;
        }

        OrderedPropertyMap map = new();

        if (server.Kind == OpenCodeMcpKind.EnabledOverride)
        {
            // The schema's third arm forbids every other key, so this arm writes exactly one.
            map.Set("enabled", server.Enabled ?? true);
            return map;
        }

        if (server.Kind == OpenCodeMcpKind.Local)
        {
            map.Set("type", "local");

            // Required by the schema, but an entry can legitimately be mid-edit. Writing
            // `"command": []` would be just as invalid as omitting it and harder to spot.
            if (server.Command.Count > 0)
            {
                map.Set("command", server.Command.Select(c => (object?)c).ToList());
            }

            SetIfPresent(map, "cwd", server.WorkingDirectory);
            SetMapIfPresent(map, "environment", server.Environment);
            SetIfPresent(map, "enabled", server.Enabled);
            SetIfPresent(map, "timeout", server.TimeoutMs);
        }
        else
        {
            map.Set("type", "remote");
            SetIfPresent(map, "url", server.Url);
            SetIfPresent(map, "enabled", server.Enabled);
            SetMapIfPresent(map, "headers", server.Headers);

            if (server.OAuthDisabled)
            {
                map.Set("oauth", false);
            }
            else if (server.OAuth is { } oauth)
            {
                // Emitted even when every field is null: `"oauth": {}` is valid and means "on,
                // with defaults", which is a different statement from the key being absent.
                map.Set("oauth", WriteOAuth(oauth));
            }

            SetIfPresent(map, "timeout", server.TimeoutMs);
        }

        // Anything this model does not surface, replayed in the order it arrived.
        foreach ((string key, object? value) in server.Extras)
        {
            map.Set(key, value);
        }

        return map;
    }

    private static OrderedPropertyMap WriteOAuth(OpenCodeMcpOAuth oauth)
    {
        OrderedPropertyMap map = new();
        SetIfPresent(map, "clientId", oauth.ClientId);
        SetIfPresent(map, "clientSecret", oauth.ClientSecret);
        SetIfPresent(map, "scope", oauth.Scope);
        SetIfPresent(map, "callbackPort", oauth.CallbackPort);
        SetIfPresent(map, "redirectUri", oauth.RedirectUri);
        return map;
    }

    /// <remarks>
    /// A whitespace-only string counts as absent. The alternative persists <c>"cwd": ""</c>, which
    /// OpenCode resolves as the workspace root rather than treating as unset — so an empty box
    /// would silently mean something.
    /// </remarks>
    private static void SetIfPresent(OrderedPropertyMap map, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            map.Set(key, value);
        }
    }

    private static void SetIfPresent(OrderedPropertyMap map, string key, bool? value)
    {
        if (value is { } b)
        {
            map.Set(key, b);
        }
    }

    private static void SetIfPresent(OrderedPropertyMap map, string key, long? value)
    {
        if (value is { } l)
        {
            map.Set(key, l);
        }
    }

    private static void SetMapIfPresent(
        OrderedPropertyMap map,
        string key,
        IReadOnlyList<KeyValuePair<string, string>> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        OrderedPropertyMap inner = new();
        foreach ((string k, string v) in entries)
        {
            if (!string.IsNullOrWhiteSpace(k))
            {
                inner.Set(k, v);
            }
        }

        if (inner.Count > 0)
        {
            map.Set(key, inner);
        }
    }

    private static bool IsEnabledOnly(IReadOnlyDictionary<string, object?> map) =>
        map.Count == 1 && map.TryGetValue("enabled", out object? e) && e is bool;

    private static OpenCodeMcpServer ReadLocal(IReadOnlyDictionary<string, object?> map) =>
        new()
        {
            Kind = OpenCodeMcpKind.Local,
            Command = ReadStringList(map, "command"),
            WorkingDirectory = Str(map, "cwd"),
            Environment = ReadStringMap(map, "environment"),
            Enabled = Bool(map, "enabled"),
            TimeoutMs = Int(map, "timeout"),
            Extras = ReadExtras(map, LocalKeys),
        };

    private static OpenCodeMcpServer ReadRemote(IReadOnlyDictionary<string, object?> map)
    {
        map.TryGetValue("oauth", out object? oauth);

        return new OpenCodeMcpServer
        {
            Kind = OpenCodeMcpKind.Remote,
            Url = Str(map, "url"),
            Headers = ReadStringMap(map, "headers"),
            // `false` is the only boolean the schema allows here, and it means something different
            // from the key being absent: off, versus auto-detect.
            OAuthDisabled = oauth is false,
            OAuth = oauth is IReadOnlyDictionary<string, object?> o ? ReadOAuth(o) : null,
            Enabled = Bool(map, "enabled"),
            TimeoutMs = Int(map, "timeout"),
            Extras = ReadExtras(map, RemoteKeys),
        };
    }

    private static OpenCodeMcpOAuth ReadOAuth(IReadOnlyDictionary<string, object?> map) =>
        new(
            ClientId: Str(map, "clientId"),
            ClientSecret: Str(map, "clientSecret"),
            Scope: Str(map, "scope"),
            CallbackPort: Int(map, "callbackPort"),
            RedirectUri: Str(map, "redirectUri"));

    private static string? Str(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? v) ? v as string : null;

    private static bool? Bool(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? v) && v is bool b ? b : null;

    private static long? Int(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? v)
            ? v switch
            {
                long l => l,
                // A JSON number that arrived as a double is still the integer the schema asked
                // for when it has no fractional part; refusing it would silently blank a timeout.
                double d when d == Math.Floor(d) => (long)d,
                var _ => null,
            }
            : null;

    private static IReadOnlyList<string> ReadStringList(
        IReadOnlyDictionary<string, object?> map,
        string key)
    {
        if (!map.TryGetValue(key, out object? v) || v is not IReadOnlyList<object?> list)
        {
            return [];
        }

        return [.. list.OfType<string>()];
    }

    private static IReadOnlyList<KeyValuePair<string, string>> ReadStringMap(
        IReadOnlyDictionary<string, object?> map,
        string key)
    {
        if (!map.TryGetValue(key, out object? v)
            || v is not IReadOnlyDictionary<string, object?> inner)
        {
            return [];
        }

        List<KeyValuePair<string, string>> pairs = [];
        foreach ((string k, object? value) in inner)
        {
            if (value is string s)
            {
                pairs.Add(new KeyValuePair<string, string>(k, s));
            }
        }

        return pairs;
    }

    private static IReadOnlyList<KeyValuePair<string, object?>> ReadExtras(
        IReadOnlyDictionary<string, object?> map,
        HashSet<string> surfaced)
    {
        List<KeyValuePair<string, object?>> extras = [];
        foreach ((string key, object? value) in map)
        {
            if (!surfaced.Contains(key))
            {
                extras.Add(new KeyValuePair<string, object?>(key, value));
            }
        }

        return extras;
    }
}
