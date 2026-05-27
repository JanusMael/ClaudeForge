using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.ClaudeForge.Core.Settings;

/// <summary>
/// Implements Claude Code's documented settings merge rules:
///  - Arrays: UNION across all scopes (all entries combined, duplicates removed)
///  - Non-arrays: highest-priority scope wins (Managed > Local > Project > User)
///  - Objects: deep merge — each key resolved independently by recursion
/// </summary>
public static class MergeEngine
{
    /// <summary>
    /// Compute the effective value for a set of scope entries.
    /// </summary>
    /// <param name="entries">Entries ordered highest-priority first (Managed first).</param>
    /// <param name="isArray">
    ///   True if the schema says this path holds an array (arrays UNION; non-arrays override).
    ///   When null, array-ness is inferred from the actual JSON values.
    /// </param>
    public static MergeResult Merge(
        IReadOnlyList<ScopeEntry> entries,
        bool? isArray = null)
    {
        return MergeCore(entries, isArray, arrayPaths: null, keyPrefix: string.Empty);
    }

    // Internal overload that carries the full arrayPaths set and the current key
    // prefix so recursive object merges can match dotted paths like
    // "permissions.allow" even when merging the nested "permissions" object.
    private static MergeResult MergeCore(
        IReadOnlyList<ScopeEntry> entries,
        bool? isArray,
        IReadOnlySet<string>? arrayPaths,
        string keyPrefix)
    {
        if (entries.Count == 0)
        {
            return new MergeResult(null, null);
        }

        // Filter out null/missing entries
        List<ScopeEntry> defined = entries.Where(e => e.Value != null).ToList();
        if (defined.Count == 0)
        {
            return new MergeResult(null, null);
        }

        // Determine whether this is an array merge
        bool treatAsArray = isArray ?? defined.Any(e => e.Value is JsonArray);

        if (treatAsArray)
        {
            return MergeArrays(defined);
        }

        // Check if all defined values are objects — if so, deep merge
        if (defined.All(e => e.Value is JsonObject))
        {
            return MergeObjects(defined, arrayPaths, keyPrefix);
        }

        // Non-array, non-object: highest-priority scope wins
        ScopeEntry winner = defined[0];
        return new MergeResult(winner.Value?.DeepClone(), winner.Scope);
    }

    private static MergeResult MergeArrays(List<ScopeEntry> defined)
    {
        // `seen` tracks already-included items by structural equality so semantically
        // equal objects with differently-ordered keys ({"a":1,"b":2} and {"b":2,"a":1})
        // are recognised as duplicates. The previous implementation used a
        // HashSet<string> keyed on JsonNode.ToJsonString(), which is property-order
        // sensitive and silently produced duplicate effective entries for object-array
        // paths. For the existing scalar array paths (permissions.allow/deny/ask, etc.)
        // the two strategies are equivalent — primitives serialise identically — but the
        // contract should hold for any future object-array path declared in ArrayPaths.
        //
        // Cost: O(n²) per array. Arrays merged here are tiny in practice (<100 items
        // across all scopes); no measurable difference vs. the hash-based version.
        List<JsonNode> seen = new();
        JsonArray result = new();

        foreach (ScopeEntry entry in defined)
        {
            if (entry.Value is not JsonArray arr)
            {
                continue;
            }

            foreach (JsonNode? item in arr)
            {
                if (item == null)
                {
                    continue;
                }

                if (seen.Any(s => JsonNode.DeepEquals(s, item)))
                {
                    continue;
                }

                JsonNode clone = item.DeepClone();
                seen.Add(clone);
                result.Add(clone);
            }
        }

        // Effective scope = the highest-priority scope that contributed items
        ConfigScope? effectiveScope = defined.FirstOrDefault(e => e.Value is JsonArray arr && arr.Count > 0)?.Scope;
        return new MergeResult(result, effectiveScope);
    }

    private static MergeResult MergeObjects(
        List<ScopeEntry> defined,
        IReadOnlySet<string>? arrayPaths,
        string keyPrefix)
    {
        JsonObject result = new();
        IEnumerable<string> allKeys = defined
                                      .SelectMany(e => ((JsonObject)e.Value!).Select(kv => kv.Key))
                                      .Distinct(StringComparer.Ordinal);

        foreach (string key in allKeys)
        {
            // Build the dotted path for this child so callers of ComputeEffective
            // who pass paths like "permissions.allow" get the right array treatment
            // when recursing into the "permissions" object.
            string childPath = string.IsNullOrEmpty(keyPrefix) ? key : $"{keyPrefix}.{key}";

            List<ScopeEntry> keyEntries = defined
                                          .Where(e => ((JsonObject)e.Value!).ContainsKey(key))
                                          .Select(e =>
                                              new ScopeEntry(e.Scope, ((JsonObject)e.Value!)[key], e.SourceFilePath))
                                          .ToList();

            // Let the caller-provided arrayPaths set govern array vs. override semantics for
            // nested keys — dotted paths resolve here.  Only pass `true` when the path is
            // explicitly listed; pass `null` (infer from actual values) otherwise.  Passing
            // `false` would force scalar-wins semantics even for actual JSON arrays that are
            // simply not listed in arrayPaths, silently dropping lower-scope contributions.
            bool? childIsArray = arrayPaths?.Contains(childPath) is true ? true : null;
            MergeResult childMerge = MergeCore(keyEntries, childIsArray, arrayPaths, childPath);
            if (childMerge.EffectiveValue != null)
            {
                result[key] = childMerge.EffectiveValue;
            }
        }

        ConfigScope effectiveScope = defined[0].Scope;
        return new MergeResult(result, effectiveScope);
    }

    /// <summary>
    /// Compute the full effective settings tree from multiple documents.
    /// Documents should be ordered highest-priority first.
    /// </summary>
    /// <param name="documents">Documents ordered highest-priority first.</param>
    /// <param name="arrayPaths">
    /// Set of dotted key paths that should be treated as arrays (union-merged).
    /// Supports nested paths such as <c>"permissions.allow"</c> — the engine
    /// threads this set recursively so nested objects also honour the hint.
    /// </param>
    public static JsonObject ComputeEffective(
        IReadOnlyList<SettingsDocument> documents,
        IReadOnlySet<string>? arrayPaths = null)
    {
        if (documents.Count == 0)
        {
            return new JsonObject();
        }

        // Collect all top-level keys across all documents
        IEnumerable<string> allKeys = documents
                                      .SelectMany(d => d.Root.Select(kv => kv.Key))
                                      .Distinct(StringComparer.Ordinal);

        JsonObject result = new();

        foreach (string key in allKeys)
        {
            List<ScopeEntry> entries = documents
                                       .Where(d => d.Root.ContainsKey(key))
                                       .Select(d => new ScopeEntry(d.Scope, d.Root[key], d.FilePath))
                                       .ToList();

            bool? isArray = arrayPaths?.Contains(key) is true ? true : null;
            MergeResult merged = MergeCore(entries, isArray, arrayPaths, key);
            if (merged.EffectiveValue != null)
            {
                result[key] = merged.EffectiveValue;
            }
        }

        return result;
    }
}

/// <summary>The effective value and the scope it came from, as returned by <see cref="MergeEngine.Merge"/>.</summary>
public sealed record MergeResult(JsonNode? EffectiveValue, ConfigScope? EffectiveScope);