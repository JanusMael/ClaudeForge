using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk.Internal;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Marketplaces;

/// <summary>
/// Default <see cref="IMarketplacesAccessor"/> implementation.
/// </summary>
/// <remarks>
/// On-disk shape is a top-level <c>"extraKnownMarketplaces"</c> object keyed
/// by marketplace name. Each value is a <c>{ source: { source: "...",
/// url|repository|package|path: "..." } }</c> object. The accessor projects
/// each entry into a flat <see cref="MarketplaceEntry"/> record carrying
/// (name, sourceKind, sourceValue).
/// </remarks>
internal sealed class MarketplacesAccessor : IMarketplacesAccessor
{
    /// <summary>
    /// Top-level JSON key the marketplaces live under. Mirrors the on-disk
    /// shape produced by the Claude Code CLI.
    /// </summary>
    private const string TopKey = "extraKnownMarketplaces";

    private readonly ClaudeConfigClientCore _client;

    public MarketplacesAccessor(ClaudeConfigClientCore client)
    {
        _client = client;
    }

    public IReadOnlyList<MarketplaceEntry> All =>
        new LazyReadOnlyList<MarketplaceEntry>(Materialize);

    public IReadOnlyList<MarketplaceEntry> GetAt(ConfigScope scope)
    {
        return new LazyReadOnlyList<MarketplaceEntry>(() => MaterializeAt(scope));
    }

    public MarketplaceEntry? Get(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (_client.GetEffectiveNode(TopKey) is not JsonObject obj)
        {
            return null;
        }

        if (obj[name] is not JsonObject entry)
        {
            return null;
        }

        return ProjectEntry(name, entry);
    }

    public void Set(MarketplaceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        JsonObject root = (_client.GetScopeValue(TopKey, _client.DefaultScope) as JsonObject)?.DeepClone() as JsonObject
                          ?? new JsonObject();
        root[entry.Name] = ToJson(entry);
        _client.SetValue(TopKey, root);
    }

    public bool Remove(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (_client.GetScopeValue(TopKey, _client.DefaultScope) is not JsonObject existing)
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

    private IReadOnlyList<MarketplaceEntry> Materialize()
    {
        return MaterializeFrom(_client.GetEffectiveNode(TopKey) as JsonObject);
    }

    private IReadOnlyList<MarketplaceEntry> MaterializeAt(ConfigScope scope)
    {
        return MaterializeFrom(_client.GetScopeValue(TopKey, scope) as JsonObject);
    }

    private static IReadOnlyList<MarketplaceEntry> MaterializeFrom(JsonObject? obj)
    {
        if (obj is null)
        {
            return Array.Empty<MarketplaceEntry>();
        }

        List<MarketplaceEntry> result = new(obj.Count);
        foreach ((string name, JsonNode? value) in obj)
        {
            if (value is JsonObject entry)
            {
                result.Add(ProjectEntry(name, entry));
            }
        }

        return result;
    }

    /// <summary>Outer-level keys the SDK models natively. Anything else
    /// goes into <see cref="MarketplaceEntry.PreservedFields"/>.</summary>
    private static readonly HashSet<string> OuterKnownKeys = new(StringComparer.Ordinal)
    {
        "source",
    };

    /// <summary>Inner source-object keys the SDK models natively. Anything
    /// else goes into <see cref="MarketplaceEntry.PreservedSourceFields"/>.</summary>
    private static readonly HashSet<string> InnerKnownKeys = new(StringComparer.Ordinal)
    {
        "source", "url", "repository", "repo", "package", "path",
    };

    private static MarketplaceEntry ProjectEntry(string name, JsonObject entry)
    {
        // Two on-disk shapes accepted (matches the existing GUI editor):
        //   1) Schema-canonical: { source: { source: "url", url: "..." } }
        //   2) Flat:             { source: "url", url: "..." }
        JsonObject sourceObj = entry["source"] as JsonObject ?? entry;

        MarketplaceSourceKind srcKind = sourceObj["source"] is JsonValue sv && sv.TryGetValue(out string? ss)
            ? ParseSourceKind(ss)
            : MarketplaceSourceKind.Url;

        string srcValue = ExtractSourceValue(srcKind, sourceObj);

        // preserve unknown fields at both levels.
        // Outer level: description and any future top-level marketplace fields.
        JsonObject? outerPreserved = null;
        foreach ((string key, JsonNode? value) in entry)
        {
            if (OuterKnownKeys.Contains(key))
            {
                continue;
            }

            outerPreserved ??= new JsonObject();
            outerPreserved[key] = value?.DeepClone();
        }

        // Inner level: future source-object fields.
        JsonObject? innerPreserved = null;
        if (entry["source"] is JsonObject canonicalSource)
        {
            foreach ((string key, JsonNode? value) in canonicalSource)
            {
                if (InnerKnownKeys.Contains(key))
                {
                    continue;
                }

                innerPreserved ??= new JsonObject();
                innerPreserved[key] = value?.DeepClone();
            }
        }

        return new MarketplaceEntry(name, srcKind, srcValue)
        {
            PreservedFields = outerPreserved,
            PreservedSourceFields = innerPreserved,
        };
    }

    private static string ExtractSourceValue(MarketplaceSourceKind kind, JsonObject source)
    {
        return kind switch
        {
            MarketplaceSourceKind.Github => GetString(source, "repository") ??
                                            GetString(source, "repo") ?? string.Empty,
            MarketplaceSourceKind.Npm => GetString(source, "package") ?? string.Empty,
            MarketplaceSourceKind.LocalFile => GetString(source, "path") ?? string.Empty,
            MarketplaceSourceKind.LocalDirectory => GetString(source, "path") ?? string.Empty,
            MarketplaceSourceKind.Git => GetString(source, "url") ?? GetString(source, "repository") ?? string.Empty,
            var _ => GetString(source, "url") ?? string.Empty,
        };
    }

    private static string? GetString(JsonObject obj, string key)
    {
        return obj[key] is JsonValue jv && jv.TryGetValue(out string? s) ? s : null;
    }

    private static JsonObject ToJson(MarketplaceEntry entry)
    {
        string sourceKey = ValueKeyFor(entry.SourceKind);
        JsonObject sourceObj = new()
        {
            ["source"] = FormatSourceKind(entry.SourceKind),
            [sourceKey] = entry.SourceValue,
        };

        // replay preserved inner-source fields. Typed properties
        // win on key collision (preserved is the fallback for unknowns).
        if (entry.PreservedSourceFields is { Count: > 0 } innerExtras)
        {
            foreach ((string key, JsonNode? value) in innerExtras)
            {
                if (sourceObj.ContainsKey(key))
                {
                    continue;
                }

                sourceObj[key] = value?.DeepClone();
            }
        }

        JsonObject outer = new() { ["source"] = sourceObj };

        // replay preserved outer fields (description, etc.).
        if (entry.PreservedFields is { Count: > 0 } outerExtras)
        {
            foreach ((string key, JsonNode? value) in outerExtras)
            {
                if (outer.ContainsKey(key))
                {
                    continue;
                }

                outer[key] = value?.DeepClone();
            }
        }

        return outer;
    }

    private static string ValueKeyFor(MarketplaceSourceKind kind)
    {
        return kind switch
        {
            MarketplaceSourceKind.Github => "repository",
            MarketplaceSourceKind.Npm => "package",
            MarketplaceSourceKind.LocalFile => "path",
            MarketplaceSourceKind.LocalDirectory => "path",
            MarketplaceSourceKind.Git => "url",
            var _ => "url",
        };
    }

    private static MarketplaceSourceKind ParseSourceKind(string raw)
    {
        return raw switch
        {
            "url" => MarketplaceSourceKind.Url,
            "git" => MarketplaceSourceKind.Git,
            "github" => MarketplaceSourceKind.Github,
            "npm" => MarketplaceSourceKind.Npm,
            "localFile" => MarketplaceSourceKind.LocalFile,
            "localDirectory" => MarketplaceSourceKind.LocalDirectory,
            var _ => MarketplaceSourceKind.Url,
        };
    }

    private static string FormatSourceKind(MarketplaceSourceKind kind)
    {
        return kind switch
        {
            MarketplaceSourceKind.Url => "url",
            MarketplaceSourceKind.Git => "git",
            MarketplaceSourceKind.Github => "github",
            MarketplaceSourceKind.Npm => "npm",
            MarketplaceSourceKind.LocalFile => "localFile",
            MarketplaceSourceKind.LocalDirectory => "localDirectory",
            var _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown MarketplaceSourceKind"),
        };
    }
}