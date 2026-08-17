using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Jsonc;
using Serilog;

namespace Bennewitz.Ninja.AgentForge.Core.FileIO;

/// <summary>
/// Writes by editing the original text in place: only the paths that actually changed
/// are rewritten, so comments, blank lines, key order, indentation, and line endings
/// survive a save.
/// </summary>
/// <remarks>
/// <para>
/// The insight this rests on is that the desired root and the load-time baseline
/// together <i>are</i> a path-level change set. Diffing them yields exactly the
/// set-at-path / remove-at-path operations <see cref="JsoncEditor"/> consumes, so no
/// change-tracking has to be added anywhere else.
/// </para>
/// <para>
/// <b>Falls back to the legacy writer, loudly, in three cases</b> — no original text
/// (a new file), an unparseable original, or a missing baseline. Falling back is a real
/// loss of formatting, so each one logs; silently degrading would make the feature look
/// broken rather than defensive.
/// </para>
/// <para>
/// Arrays are replaced wholesale rather than diffed element-wise. Deliberate: an array
/// in these config files is a list the user edits as a unit (permission rules, allowed
/// directories), and an element-level diff would produce a pile of index-addressed edits
/// whose combined effect is harder to verify than one replacement.
/// </para>
/// </remarks>
public sealed class JsoncEditWriter : IConfigWriter
{
    /// <inheritdoc/>
    public string Name => "jsonc";

    private readonly LegacySerializingWriter _fallback = new();

    /// <inheritdoc/>
    public string Render(string? originalText, JsonObject? baselineRoot, JsonObject root, string? headerComment)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (string.IsNullOrEmpty(originalText))
        {
            // A file that does not exist yet has no formatting to preserve.
            return _fallback.Render(originalText, baselineRoot, root, headerComment);
        }

        if (baselineRoot is null)
        {
            Log.Debug("[Jsonc] No baseline for this document; re-serializing rather than "
                      + "assuming nothing changed.");
            return _fallback.Render(originalText, baselineRoot, root, headerComment);
        }

        JsoncDocument document = JsoncDocument.Parse(originalText);
        if (!document.IsEditable)
        {
            Log.Warning("[Jsonc] Original text did not parse cleanly ({Errors}); re-serializing. "
                        + "Comments and formatting in this file will be lost.",
                        string.Join("; ", document.Errors));
            return _fallback.Render(originalText, baselineRoot, root, headerComment);
        }

        if (document.Root is not null && document.Root.Kind != JsoncValueKind.Object)
        {
            Log.Warning("[Jsonc] Original root is {Kind}, not an object; re-serializing.",
                        document.Root.Kind);
            return _fallback.Render(originalText, baselineRoot, root, headerComment);
        }

        List<ConfigChange> changes = [];
        DiffObjects(baselineRoot, root, prefix: null, changes);

        // The provenance stamp rides along as an ordinary path change, which is what makes
        // it possible to omit it on a no-op save — see the header-comment contract on
        // ConfigFileLoader.SaveAsync.
        if (headerComment is not null)
        {
            changes.Add(new ConfigChange(LegacySerializingWriter.MetadataKey,
                                        JsonValue.Create(headerComment)));
        }

        if (changes.Count == 0)
        {
            // Nothing changed: hand back the original bytes untouched. This is the
            // byte-identical no-op save.
            return originalText;
        }

        return Apply(originalText, changes);
    }

    /// <summary>
    /// Apply changes one at a time, re-parsing between each.
    /// </summary>
    /// <remarks>
    /// Re-parsing per change rather than batching all the edits is the deliberate slower
    /// choice. Batched edits computed against one parse can conflict — inserting two new
    /// members into the same object yields two edits at the same offset, which
    /// <see cref="TextEdit.Apply"/> correctly rejects as overlapping. Config documents are
    /// kilobytes and saves are user-initiated, so re-parsing costs nothing measurable and
    /// removes a whole class of ordering bug.
    /// </remarks>
    private static string Apply(string originalText, List<ConfigChange> changes)
    {
        string text = originalText;

        foreach (ConfigChange change in changes)
        {
            text = change.IsRemoval
                ? JsoncEditor.Remove(text, change.Path)
                : JsoncEditor.SetValue(text, change.Path, change.Value);
        }

        return text;
    }

    /// <summary>
    /// Walk <paramref name="baseline"/> against <paramref name="current"/>, emitting the
    /// narrowest change for each difference.
    /// </summary>
    /// <remarks>
    /// Recursing into objects present on both sides is what keeps edits minimal: changing
    /// <c>permissions.defaultMode</c> emits one leaf change rather than replacing the
    /// whole <c>permissions</c> object and destroying any comments inside it.
    /// </remarks>
    private static void DiffObjects(JsonObject baseline, JsonObject current, string? prefix,
                                   List<ConfigChange> sink)
    {
        foreach (KeyValuePair<string, JsonNode?> kv in current)
        {
            // The stamp is written explicitly by the caller and stripped on load, so it
            // must never be inferred as a user change.
            if (prefix is null && kv.Key == LegacySerializingWriter.MetadataKey)
            {
                continue;
            }

            string path = prefix is null ? kv.Key : $"{prefix}.{kv.Key}";
            baseline.TryGetPropertyValue(kv.Key, out JsonNode? before);

            if (before is JsonObject beforeObj && kv.Value is JsonObject afterObj)
            {
                DiffObjects(beforeObj, afterObj, path, sink);
                continue;
            }

            if (!JsonNode.DeepEquals(before, kv.Value))
            {
                sink.Add(new ConfigChange(path, kv.Value));
            }
        }

        foreach (KeyValuePair<string, JsonNode?> kv in baseline)
        {
            if (prefix is null && kv.Key == LegacySerializingWriter.MetadataKey)
            {
                continue;
            }

            if (!current.ContainsKey(kv.Key))
            {
                string path = prefix is null ? kv.Key : $"{prefix}.{kv.Key}";
                sink.Add(ConfigChange.Removal(path));
            }
        }
    }

    /// <summary>One set-at-path or remove-at-path operation.</summary>
    private readonly record struct ConfigChange(string Path, JsonNode? Value, bool IsRemoval = false)
    {
        public static ConfigChange Removal(string path) => new(path, null, IsRemoval: true);
    }
}
