using System.Collections;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.McpServers;

/// <summary>
/// Default <see cref="IMcpServersAccessor"/> implementation.
/// </summary>
/// <remarks>
/// On-disk shape is a top-level <c>"mcpServers"</c> object keyed by server
/// name. Each value is one of:
/// <list type="bullet">
///   <item><c>{ "command": "...", "args": [...], "env": {...} }</c> — Stdio transport.</item>
///   <item><c>{ "type": "sse", "url": "...", "headers": {...} }</c> — Sse transport.</item>
///   <item><c>{ "type": "streamable-http", "url": "...", "headers": {...} }</c> — Streamable HTTP transport.</item>
/// </list>
/// The accessor projects each entry into a <see cref="McpServer"/> record
/// keyed by name.
/// </remarks>
internal sealed class McpServersAccessor : IMcpServersAccessor
{
    private readonly ClaudeConfigClientCore _client;

    public McpServersAccessor(ClaudeConfigClientCore client)
    {
        _client = client;
    }

    public IReadOnlyDictionary<string, McpServer> All =>
        new LazySnapshotDictionary(Materialize);

    public IReadOnlyDictionary<string, McpServer> GetAt(ConfigScope scope)
    {
        return new LazySnapshotDictionary(() => MaterializeAt(scope));
    }

    public McpServer? Get(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (_client.GetEffectiveNode("mcpServers") is not JsonObject obj)
        {
            return null;
        }

        if (obj[name] is not JsonObject server)
        {
            return null;
        }

        return ProjectServer(name, server);
    }

    public void Set(string name, McpServer server)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(server);

        JsonObject root = (_client.GetScopeValue("mcpServers", _client.DefaultScope) as JsonObject)?.DeepClone() as JsonObject
                          ?? new JsonObject();
        root[name] = ToJson(server);
        _client.SetValue("mcpServers", root);
    }

    public bool Remove(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (_client.GetScopeValue("mcpServers", _client.DefaultScope) is not JsonObject existing)
        {
            return false;
        }

        if (!existing.ContainsKey(name))
        {
            return false;
        }

        JsonObject root = (JsonObject)existing.DeepClone();
        root.Remove(name);

        if (root.Count == 0)
        {
            _client.RemoveValue("mcpServers", _client.DefaultScope);
        }
        else
        {
            _client.SetValue("mcpServers", root);
        }

        return true;
    }

    public void Clear()
    {
        _client.RemoveValue("mcpServers", _client.DefaultScope);
    }

    // ── Internals ────────────────────────────────────────────────────────

    private IReadOnlyDictionary<string, McpServer> Materialize()
    {
        return MaterializeFrom(_client.GetEffectiveNode("mcpServers") as JsonObject);
    }

    private IReadOnlyDictionary<string, McpServer> MaterializeAt(ConfigScope scope)
    {
        return MaterializeFrom(_client.GetScopeValue("mcpServers", scope) as JsonObject);
    }

    private static IReadOnlyDictionary<string, McpServer> MaterializeFrom(JsonObject? obj)
    {
        Dictionary<string, McpServer> result = new(StringComparer.Ordinal);
        if (obj is null)
        {
            return result;
        }

        foreach ((string name, JsonNode? value) in obj)
        {
            if (value is JsonObject server)
            {
                result[name] = ProjectServer(name, server);
            }
        }

        return result;
    }

    /// <summary>Keys the SDK models natively. Anything else on the server
    /// JsonObject is captured into <see cref="McpServer.PreservedFields"/>
    /// so round-trips don't silently drop fields. 2026-05-01: <c>description</c>
    /// promoted to a typed property — kept in this list so it's not also
    /// captured as opaque (typed property is the single source of truth).</summary>
    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "type", "command", "args", "env", "url", "headers", "description",
    };

    private static McpServer ProjectServer(string name, JsonObject server)
    {
        McpTransport transport = ParseTransport(server["type"] is JsonValue tv && tv.TryGetValue(out string? t) ? t : null);

        string? command = server["command"] is JsonValue cv && cv.TryGetValue(out string? cs) ? cs : null;
        string? url = server["url"] is JsonValue uv && uv.TryGetValue(out string? us) ? us : null;

        IReadOnlyList<string>? args = null;
        if (server["args"] is JsonArray argsArr)
        {
            List<string> list = new(argsArr.Count);
            foreach (JsonNode? item in argsArr)
            {
                if (item is JsonValue av && av.TryGetValue(out string? s))
                {
                    list.Add(s);
                }
            }

            args = list;
        }

        IReadOnlyDictionary<string, string>? env = ProjectStringDict(server["env"] as JsonObject);
        IReadOnlyDictionary<string, string>? headers = ProjectStringDict(server["headers"] as JsonObject);
        string? description = server["description"] is JsonValue dv && dv.TryGetValue(out string? ds) ? ds : null;

        // preserve unknown fields verbatim so round-trips don't
        // drop them. The user-reported bug 2026-04-30: every Save was
        // wiping the `description` field from every MCP server entry,
        // because ProjectServer didn't capture it and ToJson didn't emit
        // it. Generalise to ANY field the SDK doesn't model — anything
        // not in KnownKeys gets stashed in PreservedFields and replayed
        // by ToJson.
        JsonObject? preserved = null;
        foreach ((string key, JsonNode? value) in server)
        {
            if (KnownKeys.Contains(key))
            {
                continue;
            }

            preserved ??= new JsonObject();
            preserved[key] = value?.DeepClone();
        }

        return new McpServer(name, transport, command, args, env, url, headers, description)
        {
            PreservedFields = preserved,
        };
    }

    private static IReadOnlyDictionary<string, string>? ProjectStringDict(JsonObject? obj)
    {
        if (obj is null)
        {
            return null;
        }

        Dictionary<string, string> dict = new(StringComparer.Ordinal);
        foreach ((string k, JsonNode? v) in obj)
        {
            if (v is JsonValue jv && jv.TryGetValue(out string? s))
            {
                dict[k] = s;
            }
        }

        return dict;
    }

    private static JsonObject ToJson(McpServer server)
    {
        JsonObject obj = new();

        // Stdio is the implicit default — only emit "type" for non-stdio transports.
        if (server.Transport != McpTransport.Stdio)
        {
            obj["type"] = FormatTransport(server.Transport);
        }

        if (server.Command is not null)
        {
            obj["command"] = server.Command;
        }

        if (server.Url is not null)
        {
            obj["url"] = server.Url;
        }

        if (server.Args is { Count: > 0 } args)
        {
            JsonArray arr = new();
            foreach (string a in args)
            {
                arr.Add(JsonValue.Create(a));
            }

            obj["args"] = arr;
        }

        if (server.Env is { Count: > 0 } env)
        {
            JsonObject envObj = new();
            foreach ((string k, string v) in env)
            {
                envObj[k] = v;
            }

            obj["env"] = envObj;
        }

        if (server.Headers is { Count: > 0 } headers)
        {
            JsonObject hObj = new();
            foreach ((string k, string v) in headers)
            {
                hObj[k] = v;
            }

            obj["headers"] = hObj;
        }

        // typed Description (promoted from PreservedFields).
        // Emit when non-null. Empty-string is intentionally preserved
        // since the schema permits any string.
        if (server.Description is not null)
        {
            obj["description"] = server.Description;
        }

        // round-trip preservation — replay any fields the
        // SDK doesn't natively model (e.g. `description`). Captured by
        // ProjectServer above. Skip any preserved key that collides with
        // a key already emitted by the typed properties — the typed value
        // is the source of truth (preserved is a fallback for unknowns).
        if (server.PreservedFields is { Count: > 0 } preserved)
        {
            foreach ((string key, JsonNode? value) in preserved)
            {
                if (obj.ContainsKey(key))
                {
                    continue;
                }

                obj[key] = value?.DeepClone();
            }
        }

        return obj;
    }

    private static McpTransport ParseTransport(string? raw)
    {
        return raw?.ToLowerInvariant() switch
        {
            null => McpTransport.Stdio,
            "stdio" => McpTransport.Stdio,
            "sse" => McpTransport.Sse,
            "streamablehttp" => McpTransport.StreamableHttp,
            "streamable-http" => McpTransport.StreamableHttp,
            // The GUI's McpServerEntry.Type uses "http" as the user-facing form (per
            // McpServersEditorViewModel.FormatTransport: StreamableHttp → "http").
            // McpServerEntry.ToJson emits Type verbatim, so the SDK reads
            // "http" back on reload.  Without this synonym the SDK silently
            // downcasted to Stdio, and the editor's reload showed Type="stdio"
            // — a round-trip data-loss bug.
            "http" => McpTransport.StreamableHttp,
            var _ => McpTransport.Stdio, // Forward-compatible default
        };
    }

    private static string FormatTransport(McpTransport transport)
    {
        return transport switch
        {
            McpTransport.Stdio => "stdio",
            McpTransport.Sse => "sse",
            McpTransport.StreamableHttp => "streamable-http",
            var _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown McpTransport"),
        };
    }

    // ── LazySnapshotDictionary ───────────────────────────────────────────
    //
    // Mirrors LazyReadOnlyList<T> for IReadOnlyDictionary returns (the §6.6
    // contract applies to all snapshot views, not just lists).

    private sealed class LazySnapshotDictionary : IReadOnlyDictionary<string, McpServer>
    {
        private readonly Func<IReadOnlyDictionary<string, McpServer>> _materialize;
        private IReadOnlyDictionary<string, McpServer>? _snapshot;

        public LazySnapshotDictionary(Func<IReadOnlyDictionary<string, McpServer>> materialize)
        {
            _materialize = materialize;
        }

        private IReadOnlyDictionary<string, McpServer> Snapshot => _snapshot ??= _materialize();

        public McpServer this[string key] => Snapshot[key];
        public IEnumerable<string> Keys => Snapshot.Keys;
        public IEnumerable<McpServer> Values => Snapshot.Values;
        public int Count => Snapshot.Count;

        public bool ContainsKey(string key)
        {
            return Snapshot.ContainsKey(key);
        }

        public bool TryGetValue(string key, out McpServer value)
        {
            return Snapshot.TryGetValue(key, out value!);
        }

        public IEnumerator<KeyValuePair<string, McpServer>> GetEnumerator()
        {
            return Snapshot.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}