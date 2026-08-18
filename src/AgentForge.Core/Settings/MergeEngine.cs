using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.AgentForge.Core.Settings;

/// <summary>
/// Executes layered-configuration merging. The rules that vary by product live in the
/// <see cref="IMergePolicy"/> every entry point requires:
///  - Unioned paths: contributions combined across scopes, duplicates removed, in the
///    policy's <see cref="IMergePolicy.UnionOrder"/>
///  - Everything else scalar: highest-priority scope wins (for Claude, Managed > Local >
///    Project > User)
///  - Objects: deep merge — each key resolved independently by recursion
/// </summary>
/// <remarks>
/// The engine never names a scope; it relies only on entries arriving highest-priority
/// first, which is <c>SettingsWorkspace</c>'s job. There is deliberately <b>no</b> overload
/// that omits the policy: a defaulted policy would silently give a new product Claude
/// Code's merge rules, which is precisely the failure this seam exists to prevent.
/// </remarks>
public static class MergeEngine
{
    /// <summary>
    /// Compute the effective value for a set of scope entries at <paramref name="path"/>.
    /// </summary>
    /// <param name="entries">Entries ordered highest-priority first (Managed first).</param>
    /// <param name="path">Dotted path these entries were read from, for the policy to rule on.</param>
    /// <param name="policy">The product's merge rules.</param>
    public static MergeResult Merge(
        IReadOnlyList<ScopeEntry> entries,
        string path,
        IMergePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return MergeCore(entries, path, policy);
    }

    // Carries the current dotted path so recursive object merges can rule on paths like
    // "permissions.allow" even while merging the enclosing "permissions" object.
    private static MergeResult MergeCore(
        IReadOnlyList<ScopeEntry> entries,
        string path,
        IMergePolicy policy)
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

        // Ask the policy whether this path unions. `everyValueIsArray` is handed over
        // because a product may infer union-ness when its schema has not declared the path
        // — but ONLY when EVERY defined scope value is an array. A MIXED set (e.g. one
        // scope holds a bool and another an array for the same key — legal for
        // `enabledPlugins`, whose values are anyOf[array, bool]) is NOT a uniform array
        // path, and a policy that infers will see false and fall through to
        // highest-priority-wins rather than letting MergeArrays silently drop the
        // non-array (higher-priority) value into a union. A policy that declares the path
        // unions regardless of the values, which is how schema-declared array paths keep
        // their behaviour when one scope holds something odd.
        if (policy.UnionsAt(path, defined.All(e => e.Value is JsonArray)))
        {
            return MergeArrays(defined, policy.UnionOrder);
        }

        // Check if all defined values are objects — if so, deep merge
        if (defined.All(e => e.Value is JsonObject))
        {
            return MergeObjects(defined, path, policy);
        }

        // Non-array, non-object: highest-priority scope wins
        ScopeEntry winner = defined[0];
        return new MergeResult(winner.Value?.DeepClone(), winner.Scope);
    }

    private static MergeResult MergeArrays(List<ScopeEntry> defined, MergeUnionOrder order)
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

        // `defined` arrives highest-priority first. A policy that unions lowest-first walks
        // it backwards — which changes the ORDER of the result, and for a product whose
        // last matching rule wins, order is semantics rather than presentation. Dedupe
        // still keeps the FIRST occurrence encountered, so the surviving copy of a
        // duplicated entry belongs to whichever end the policy starts from.
        IEnumerable<ScopeEntry> contributions = order == MergeUnionOrder.LowestPriorityFirst
            ? Enumerable.Reverse(defined)
            : defined;

        foreach (ScopeEntry entry in contributions)
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

        // Effective scope = the highest-priority scope that contributed items. Independent
        // of UnionOrder on purpose: the order describes where the result STARTS, not which
        // scope is credited with it.
        ConfigScope? effectiveScope = defined.FirstOrDefault(e => e.Value is JsonArray arr && arr.Count > 0)?.Scope;
        return new MergeResult(result, effectiveScope);
    }

    private static MergeResult MergeObjects(
        List<ScopeEntry> defined,
        string keyPrefix,
        IMergePolicy policy)
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

            // The policy rules on the child's dotted path, which is why the prefix is
            // threaded: "permissions.allow" has to be recognisable while merging the
            // enclosing "permissions" object.
            MergeResult childMerge = MergeCore(keyEntries, childPath, policy);
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
    /// <param name="policy">
    /// The product's merge rules. Consulted per dotted key path, including nested ones such
    /// as <c>"permissions.allow"</c> — the engine threads the path recursively so a policy
    /// can rule on nested keys too.
    /// </param>
    public static JsonObject ComputeEffective(
        IReadOnlyList<SettingsDocument> documents,
        IMergePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
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

            MergeResult merged = MergeCore(entries, key, policy);
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