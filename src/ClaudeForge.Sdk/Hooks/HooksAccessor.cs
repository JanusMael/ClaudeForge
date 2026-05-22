using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk.Internal;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Hooks;

/// <summary>
/// Default <see cref="IHooksAccessor"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// On-disk shape is a top-level <c>"hooks"</c> object keyed by event name.
/// Each value is an array of "outer" hook entries (<c>{ matcher: "...",
/// hooks: [{ type: "command", command: "..." }, ...] }</c>). The flattened
/// <see cref="IHooksAccessor.Events"/> list lifts every inner hook to a single
/// <see cref="HookEvent"/> record carrying its event-name + matcher +
/// command-type + command-value tuple.
/// </para>
/// <para>
/// Add / Remove preserve the on-disk grouping by event name + matcher: adding
/// an event with the same matcher as an existing entry appends to that
/// entry's inner <c>hooks</c> array; otherwise a new outer entry is created.
/// </para>
/// </remarks>
internal sealed class HooksAccessor : IHooksAccessor
{
    private readonly ClaudeConfigClientCore _client;

    public HooksAccessor(ClaudeConfigClientCore client)
    {
        _client = client;
    }

    public IReadOnlyList<HookEvent> Events =>
        new LazyReadOnlyList<HookEvent>(Materialize);

    public IReadOnlyList<HookEvent> EventsAt(ConfigScope scope)
    {
        return new LazyReadOnlyList<HookEvent>(() => MaterializeAt(scope));
    }

    public void Add(HookEvent hook)
    {
        ArgumentNullException.ThrowIfNull(hook);

        JsonObject hooksObj = (_client.GetScopeValue("hooks", _client.DefaultScope) as JsonObject)?.DeepClone() as JsonObject
                              ?? new JsonObject();

        JsonObject inner;
        if (hook.OpaqueInnerJson is { } opaque)
        {
            // When the original load encountered an
            // unknown type ("agent" / "http" / future schema additions),
            // the SDK preserved the inner JsonObject verbatim.  Emit it
            // back verbatim, bypassing typed serialisation entirely —
            // typed fields on the HookEvent record are best-effort and
            // would corrupt the type discriminator if used here.
            inner = (JsonObject)opaque.DeepClone();
        }
        else
        {
            // Build the inner hook record for the requested CommandType.
            inner = new JsonObject { ["type"] = FormatCommandType(hook.CommandType) };
            if (!string.IsNullOrEmpty(hook.CommandValue))
            {
                inner[ValueKeyFor(hook.CommandType)] = hook.CommandValue;
            }

            // emit typed sub-fields (timeout, headers,
            // allowedEnvVars). Schema validity is the consumer's responsibility:
            // headers / allowedEnvVars only validate against the http variant;
            // the SDK doesn't gate emission on CommandType because users may
            // legitimately want to round-trip those keys verbatim.
            if (hook.Timeout is { } timeout)
            {
                inner["timeout"] = timeout;
            }

            if (hook.Headers is { Count: > 0 } headers)
            {
                JsonObject headersObj = new();
                foreach ((string key, string value) in headers)
                {
                    headersObj[key] = value;
                }

                inner["headers"] = headersObj;
            }

            if (hook.AllowedEnvVars is { Count: > 0 } allowedEnvVars)
            {
                JsonArray arr = new();
                foreach (string name in allowedEnvVars)
                {
                    arr.Add(name);
                }

                inner["allowedEnvVars"] = arr;
            }

            // replay preserved per-hook fields (async,
            // statusMessage, model, etc.). Typed properties win on key
            // collision — type, the value-key, and the Stop-B fields are
            // the source of truth; the preserved bag is the fallback for
            // unknown fields.
            if (hook.PreservedFields is { Count: > 0 } extras)
            {
                foreach ((string key, JsonNode? value) in extras)
                {
                    if (inner.ContainsKey(key))
                    {
                        continue;
                    }

                    inner[key] = value?.DeepClone();
                }
            }
        }

        // Find or create the outer { matcher, hooks: [] } entry under the event name.
        if (hooksObj[hook.EventName] is not JsonArray outerArr)
        {
            outerArr = new JsonArray();
            hooksObj[hook.EventName] = outerArr;
        }

        // outer-group matching honors the no-matcher case.
        // hook.Matcher == empty string means "no matcher key on the outer
        // group" (see MaterializeFrom). The match must compare against
        // both an explicitly-empty matcher AND a missing matcher key,
        // because both round-trip to "" through MaterializeFrom.
        JsonObject? existingOuter = null;
        foreach (JsonNode? node in outerArr)
        {
            if (node is not JsonObject obj)
            {
                continue;
            }

            string nodeMatcher = obj["matcher"] is JsonValue m && m.TryGetValue(out string? ms)
                ? ms
                : string.Empty;
            if (nodeMatcher == hook.Matcher)
            {
                existingOuter = obj;
                break;
            }
        }

        if (existingOuter is null)
        {
            existingOuter = new JsonObject();
            // Only emit the matcher key when non-empty so the no-matcher
            // outer group preserves its shape on round-trip.
            if (!string.IsNullOrEmpty(hook.Matcher))
            {
                existingOuter["matcher"] = hook.Matcher;
            }

            existingOuter["hooks"] = new JsonArray();
            outerArr.Add(existingOuter);
        }

        if (existingOuter["hooks"] is not JsonArray innerArr)
        {
            innerArr = new JsonArray();
            existingOuter["hooks"] = innerArr;
        }

        // Skip if an exact-match equivalent already exists. Equality is
        // (CommandType, CommandValue); matcher is implied by the outer entry.
        if (!ContainsExact(innerArr, hook))
        {
            innerArr.Add(inner);
        }

        _client.SetValue("hooks", hooksObj);
    }

    public bool Remove(HookEvent hook)
    {
        ArgumentNullException.ThrowIfNull(hook);

        if (_client.GetScopeValue("hooks", _client.DefaultScope) is not JsonObject root)
        {
            return false;
        }

        JsonObject hooksObj = (JsonObject)root.DeepClone();

        if (hooksObj[hook.EventName] is not JsonArray outerArr)
        {
            return false;
        }

        bool removed = false;
        for (int oi = outerArr.Count - 1; oi >= 0; oi--)
        {
            if (outerArr[oi] is not JsonObject outer)
            {
                continue;
            }

            // missing-matcher tolerance — see MaterializeFrom
            // and Add. Treat missing matcher as empty string so removing
            // a hook from a no-matcher outer group works the same as
            // removing one from a matchered group.
            string matcher = outer["matcher"] is JsonValue mv && mv.TryGetValue(out string? ms)
                ? ms
                : string.Empty;
            if (matcher != hook.Matcher)
            {
                continue;
            }

            if (outer["hooks"] is JsonArray innerArr)
            {
                for (int ii = innerArr.Count - 1; ii >= 0; ii--)
                {
                    if (innerArr[ii] is JsonObject inner && InnerMatches(inner, hook))
                    {
                        innerArr.RemoveAt(ii);
                        removed = true;
                    }
                }

                if (innerArr.Count == 0)
                {
                    outerArr.RemoveAt(oi);
                }
            }
            else
            {
                // Malformed: outer entry without hooks array. Drop it on remove.
                outerArr.RemoveAt(oi);
                removed = true;
            }
        }

        if (!removed)
        {
            return false;
        }

        if (outerArr.Count == 0)
        {
            hooksObj.Remove(hook.EventName);
        }

        if (hooksObj.Count == 0)
        {
            _client.RemoveValue("hooks", _client.DefaultScope);
        }
        else
        {
            _client.SetValue("hooks", hooksObj);
        }

        return true;
    }

    public void Clear()
    {
        _client.RemoveValue("hooks", _client.DefaultScope);
    }

    // ── Internals ────────────────────────────────────────────────────────

    private IReadOnlyList<HookEvent> Materialize()
    {
        return MaterializeFrom(_client.GetEffectiveNode("hooks") as JsonObject);
    }

    private IReadOnlyList<HookEvent> MaterializeAt(ConfigScope scope)
    {
        return MaterializeFrom(_client.GetScopeValue("hooks", scope) as JsonObject);
    }

    private static IReadOnlyList<HookEvent> MaterializeFrom(JsonObject? hooksObj)
    {
        if (hooksObj is null)
        {
            return Array.Empty<HookEvent>();
        }

        List<HookEvent> result = new();
        foreach ((string eventName, JsonNode? outerNode) in hooksObj)
        {
            if (outerNode is not JsonArray outerArr)
            {
                continue;
            }

            foreach (JsonNode? outerEntry in outerArr)
            {
                if (outerEntry is not JsonObject outer)
                {
                    continue;
                }

                // round-trip preservation. The schema's
                // hookMatcher definition makes `matcher` OPTIONAL.
                // Several user configs (notably users of plugin-managed
                // hooks like everything-claude-code) keep the no-matcher
                // outer group AND a matcher="*" outer group at the same
                // event. Defaulting the missing matcher to "*" here would
                // silently MERGE the two outer groups on read, then on a
                // subsequent ToJsonValue flush all entries cluster under
                // matcher="*" — producing phantom entries that fail
                // schema validation (anyOf branch noise on entries that
                // shouldn't be there at all). Use empty string for
                // missing matcher; the editor's HookEventGroup.ToJson
                // omits the matcher key when the group's matcher is
                // empty, preserving the on-disk shape exactly.
                //
                // Tests:
                //   tests/ClaudeForge.Tests/ViewModels/Editors/
                //     HooksEditorLoadPathMutationTests.cs
                string matcher = outer["matcher"] is JsonValue m && m.TryGetValue(out string? ms)
                    ? ms
                    : string.Empty;

                if (outer["hooks"] is not JsonArray innerArr)
                {
                    continue;
                }

                foreach (JsonNode? innerEntry in innerArr)
                {
                    if (innerEntry is not JsonObject inner)
                    {
                        continue;
                    }

                    if (inner["type"] is not JsonValue tv || !tv.TryGetValue(out string? typeRaw))
                    {
                        continue;
                    }

                    // Detect unknown type discriminators
                    // (anything other than command / prompt / url) so the
                    // load path can preserve the inner JsonObject verbatim
                    // for round-trip via Add → emit.  ParseCommandType
                    // returns the Command fallback for unknown types; that
                    // value is set on the typed CommandType property as a
                    // best-effort but is overridden by OpaqueInnerJson on
                    // emit, so the type discriminator is preserved end-to-end.
                    string typeRawLower = typeRaw.ToLowerInvariant();
                    bool isOpaque = typeRawLower is not ("command" or "prompt" or "url");
                    JsonObject? opaqueInner = isOpaque ? (JsonObject)inner.DeepClone() : null;

                    HookCommandType type = ParseCommandType(typeRaw);
                    string valKey = ValueKeyFor(type);
                    string value = inner[valKey] is JsonValue vv && vv.TryGetValue(out string? vs)
                        ? vs
                        : string.Empty;

                    // extract typed sub-fields
                    // (timeout / headers / allowedEnvVars) so SDK consumers
                    // (MCP servers, CLI tools) can read them as typed
                    // properties without touching JsonNode.  These keys
                    // are also excluded from the preserved-fields capture
                    // below so they aren't double-emitted on save.
                    int? timeoutTyped = null;
                    if (inner["timeout"] is JsonValue tov && tov.TryGetValue(out int to))
                    {
                        timeoutTyped = to;
                    }

                    IReadOnlyDictionary<string, string>? headersTyped = null;
                    if (inner["headers"] is JsonObject headersObj)
                    {
                        Dictionary<string, string> headersDict = new(headersObj.Count, StringComparer.Ordinal);
                        foreach ((string key, JsonNode? val) in headersObj)
                        {
                            if (val is JsonValue hv && hv.TryGetValue(out string? hs))
                            {
                                headersDict[key] = hs;
                            }
                        }

                        if (headersDict.Count > 0)
                        {
                            headersTyped = headersDict;
                        }
                    }

                    IReadOnlyList<string>? envVarsTyped = null;
                    if (inner["allowedEnvVars"] is JsonArray envVarsArr)
                    {
                        List<string> envVarsList = new(envVarsArr.Count);
                        foreach (JsonNode? item in envVarsArr)
                        {
                            if (item is JsonValue ev && ev.TryGetValue(out string? es))
                            {
                                envVarsList.Add(es);
                            }
                        }

                        if (envVarsList.Count > 0)
                        {
                            envVarsTyped = envVarsList;
                        }
                    }

                    // capture per-hook fields the SDK doesn't
                    // model (async, statusMessage, model, etc.).  Replayed
                    // by Add when the hook is written back, so round-trips
                    // preserve user data for fields without typed
                    // properties.  Keys promoted to typed (Stop A/B) are
                    // excluded here — typed property is the source of
                    // truth post-promotion.
                    JsonObject? preserved = null;
                    foreach ((string key, JsonNode? val) in inner)
                    {
                        if (key == "type" || key == valKey)
                        {
                            continue;
                        }

                        if (key is "timeout" or "headers" or "allowedEnvVars")
                        {
                            continue;
                        }

                        preserved ??= new JsonObject();
                        preserved[key] = val?.DeepClone();
                    }

                    result.Add(new HookEvent(eventName, matcher, type, value)
                    {
                        Timeout = timeoutTyped,
                        Headers = headersTyped,
                        AllowedEnvVars = envVarsTyped,
                        PreservedFields = preserved,
                        OpaqueInnerJson = opaqueInner,
                    });
                }
            }
        }

        return result;
    }

    private static bool ContainsExact(JsonArray innerArr, HookEvent hook)
    {
        foreach (JsonNode? node in innerArr)
        {
            if (node is JsonObject inner && InnerMatches(inner, hook))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InnerMatches(JsonObject inner, HookEvent hook)
    {
        if (inner["type"] is not JsonValue tv || !tv.TryGetValue(out string? typeRaw))
        {
            return false;
        }

        if (ParseCommandType(typeRaw) != hook.CommandType)
        {
            return false;
        }

        string key = ValueKeyFor(hook.CommandType);
        string val = inner[key] is JsonValue vv && vv.TryGetValue(out string? s) ? s : string.Empty;
        return val == hook.CommandValue;
    }

    private static HookCommandType ParseCommandType(string raw)
    {
        return raw.ToLowerInvariant() switch
        {
            "prompt" => HookCommandType.Prompt,
            "url" => HookCommandType.Url,
            var _ => HookCommandType.Command,
        };
    }

    private static string FormatCommandType(HookCommandType type)
    {
        return type switch
        {
            HookCommandType.Prompt => "prompt",
            HookCommandType.Url => "url",
            var _ => "command",
        };
    }

    private static string ValueKeyFor(HookCommandType type)
    {
        return type switch
        {
            HookCommandType.Prompt => "prompt",
            HookCommandType.Url => "url",
            var _ => "command",
        };
    }
}