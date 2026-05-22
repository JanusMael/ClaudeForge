using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Diagnostics;

/// <summary>
/// Computes structural diffs between two <see cref="JsonObject"/> snapshots,
/// recursing into nested objects and computing a multi-set delta for nested
/// arrays so consumers (save-confirmation dialogs, audit logs, etc.) can
/// surface only what actually changed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Visibility:</b> declared <c>internal</c> because the inputs are
/// <see cref="JsonObject"/> — a <c>System.Text.Json.Nodes</c> type the
/// SDK keeps off its public surface (see <c>NoPublicApi_LeaksSystemTextJsonNodes</c>
/// in the SDK contract tests).  In-process consumers (the GUI app, the
/// in-tree test projects) reach this through <c>InternalsVisibleTo</c>;
/// out-of-process consumers should serialise to JSON strings and use a
/// future string-based wrapper if/when one is added.
/// </para>
/// <para>
/// The result type <see cref="PropertyDiff"/> remains public so the
/// produced diff list can flow through public surfaces (e.g. dialog
/// view-models, audit reports) without forcing every caller to define
/// its own DTO.
/// </para>
/// </remarks>
/// <remarks>
/// <para>
/// <b>Recursion contract:</b> when a key's value is a <see cref="JsonObject"/>
/// in both baseline and current, the algorithm recurses with
/// <c>{parent}.{key}</c> as the path prefix.  When the value is a
/// <see cref="JsonArray"/> in both, it emits one <see cref="PropertyDiff"/>
/// per added / removed element so callers see the surgical change, not the
/// whole array twice.  Primitive or type-mismatched values fall through to
/// the legacy "single Modified row" treatment.
/// </para>
/// <para>
/// <b>Why recursion matters:</b> Claude config has "container" keys
/// (<c>hooks</c>, <c>permissions</c>, <c>mcpServers</c>) whose values are
/// large objects.  Without recursion, a single hook removal emits an entire
/// <c>hooks</c> blob as one Modified row — the consumer then has to eyeball
/// thousands of characters to find the one entry that changed.  With
/// recursion they see a single Removed row at path <c>hooks.Stop</c>
/// containing only the removed hook's JSON.
/// </para>
/// <para>
/// <b>Array set-diff:</b> arrays don't carry stable per-element identity, so
/// each element's <see cref="JsonNode.ToJsonString"/> serves as the multiset
/// key.  Adds and removes are reported individually; pure reorders (same
/// multiset, different sequence) fall back to a single Modified row at the
/// array path so the change is still visible.
/// </para>
/// <para>
/// <b>Sort:</b> results are sorted with a composite key —
/// <see cref="ChangeKind"/> rank first (Modified → Added → Removed), then
/// alphabetical by path as the within-kind tiebreaker.  Modified rows
/// surface first because they carry the highest fat-finger risk — the user
/// is least likely to remember the prior value of a silently-edited field.
/// </para>
/// <para>
/// <b>Metadata key:</b> the tool-written <c>"//"</c> key is stripped at every
/// recursion level — it's a comment marker that always changes (timestamps)
/// and carries no semantic content.
/// </para>
/// </remarks>
internal static class JsonDiff
{
    /// <summary>
    /// Compute the sorted list of diffs between <paramref name="baseline"/>
    /// and <paramref name="current"/>.  A null baseline is treated as an
    /// empty object (every key in <paramref name="current"/> becomes
    /// <see cref="ChangeKind.Added"/>).
    /// </summary>
    internal static IReadOnlyList<PropertyDiff> Compute(JsonObject? baseline, JsonObject current)
    {
        List<PropertyDiff> raw = new();
        DiffObjectsCore(baseline, current, pathPrefix: string.Empty, raw);
        return
        [
            .. raw
               .OrderBy(x => KindSortRank(x.Kind))
               .ThenBy(x => x.Key, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// Maps <see cref="ChangeKind"/> to the rank used by <see cref="Compute"/>:
    /// Modified (0) → Added (1) → Removed (2).
    /// </summary>
    private static int KindSortRank(ChangeKind kind)
    {
        return kind switch
        {
            ChangeKind.Modified => 0,
            ChangeKind.Added => 1,
            ChangeKind.Removed => 2,
            var _ => 3,
        };
    }

    /// <summary>
    /// Recursive collector that walks the object tree and appends diffs to
    /// <paramref name="sink"/> without sorting; the public
    /// <see cref="Compute"/> sorts the final flat list.
    /// </summary>
    private static void DiffObjectsCore(
        JsonObject? baseline,
        JsonObject current,
        string pathPrefix,
        List<PropertyDiff> sink)
    {
        HashSet<string> baselineKeys = baseline?.Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal)
                                       ?? new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> currentKeys = current.Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);

        // Strip tool-written metadata at every level — it always changes (timestamps)
        // and carries no semantic content the consumer cares about.
        baselineKeys.Remove("//");
        currentKeys.Remove("//");

        foreach (string key in currentKeys.Except(baselineKeys))
        {
            // When a newly-added key holds a JsonObject, recurse into it with a
            // null baseline so each leaf surfaces as its own Added row
            // ("Added env.MAX_OUTPUT_TOKENS") rather than a single opaque blob
            // ("Added env").  This is the same treatment we give object keys that
            // exist on both sides — consistent and far more readable in the dialog.
            if (current[key] is JsonObject addedObj)
            {
                DiffObjectsCore(null, addedObj, Path(key), sink);
            }
            else
            {
                sink.Add(new PropertyDiff(Path(key), ChangeKind.Added, null, current[key]?.ToJsonString()));
            }
        }

        foreach (string key in baselineKeys.Except(currentKeys))
        {
            // Symmetric to the Added case: recurse into a removed object so the
            // dialog shows "Removed env.MAX_OUTPUT_TOKENS" not "Removed env".
            if (baseline![key] is JsonObject removedObj)
            {
                DiffObjectsCore(removedObj, new JsonObject(), Path(key), sink);
            }
            else
            {
                sink.Add(new PropertyDiff(Path(key), ChangeKind.Removed, baseline![key]?.ToJsonString(), null));
            }
        }

        foreach (string key in baselineKeys.Intersect(currentKeys))
        {
            JsonNode? oldNode = baseline![key];
            JsonNode? newNode = current[key];

            // Both sides are objects → recurse; surfaces just the leaf changes.
            if (oldNode is JsonObject oldObj && newNode is JsonObject newObj)
            {
                DiffObjectsCore(oldObj, newObj, Path(key), sink);
                continue;
            }

            // Both sides are arrays → element-wise set diff.
            if (oldNode is JsonArray oldArr && newNode is JsonArray newArr)
            {
                DiffArraysCore(oldArr, newArr, Path(key), sink);
                continue;
            }

            // Primitive value or type mismatch — fall through to a single Modified row.
            string? oldJson = oldNode?.ToJsonString();
            string? newJson = newNode?.ToJsonString();
            if (oldJson != newJson)
            {
                sink.Add(new PropertyDiff(Path(key), ChangeKind.Modified, oldJson, newJson));
            }
        }

        return;

        string Path(string key)
        {
            return string.IsNullOrEmpty(pathPrefix) ? key : $"{pathPrefix}.{key}";
        }
    }

    /// <summary>
    /// Element-wise multiset diff for two <see cref="JsonArray"/>s sharing a
    /// path.  Each element is keyed by its <see cref="JsonNode.ToJsonString"/>
    /// representation, so duplicates are counted correctly and reorders
    /// (same multiset, different sequence) fall through to a single Modified
    /// row instead of N spurious add/remove pairs.
    /// </summary>
    private static void DiffArraysCore(
        JsonArray baseline,
        JsonArray current,
        string path,
        List<PropertyDiff> sink)
    {
        List<string> oldItems = baseline.Select(n => n?.ToJsonString() ?? "null").ToList();
        List<string> newItems = current.Select(n => n?.ToJsonString() ?? "null").ToList();

        Dictionary<string, int> oldMultiset = new(StringComparer.Ordinal);
        foreach (string item in oldItems)
        {
            oldMultiset[item] = oldMultiset.GetValueOrDefault(item) + 1;
        }

        Dictionary<string, int> newMultiset = new(StringComparer.Ordinal);
        foreach (string item in newItems)
        {
            newMultiset[item] = newMultiset.GetValueOrDefault(item) + 1;
        }

        int addedCount = 0;
        int removedCount = 0;

        foreach ((string item, int count) in oldMultiset)
        {
            int newCount = newMultiset.GetValueOrDefault(item);
            for (int i = 0; i < count - newCount; i++)
            {
                sink.Add(new PropertyDiff(path, ChangeKind.Removed, item, null));
                removedCount++;
            }
        }

        foreach ((string item, int count) in newMultiset)
        {
            int oldCount = oldMultiset.GetValueOrDefault(item);
            for (int i = 0; i < count - oldCount; i++)
            {
                sink.Add(new PropertyDiff(path, ChangeKind.Added, null, item));
                addedCount++;
            }
        }

        // Multiset matched but sequence differs → element order changed.
        // Emit one Modified row at the array path so the change is still surfaced
        // (rare in practice; arrays in Claude config tend to be naturally ordered).
        if (addedCount == 0 && removedCount == 0 && !oldItems.SequenceEqual(newItems))
        {
            sink.Add(new PropertyDiff(path, ChangeKind.Modified,
                baseline.ToJsonString(), current.ToJsonString()));
        }
    }
}