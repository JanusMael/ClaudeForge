using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.AgentForge.Core.FileIO;

/// <summary>
/// The pre-Phase-2 writer: re-serialize the whole document with
/// <c>WriteIndented = true</c>.
/// </summary>
/// <remarks>
/// <para>
/// Kept as a one-release escape hatch behind <c>--writer legacy</c>, not as a
/// supported mode. It is lossy by construction — comments, blank lines, indentation
/// style, and line endings are all replaced by the serializer's own formatting,
/// because it never looks at the original text.
/// </para>
/// <para>
/// <b>Remove after one clean release.</b> The reason for the deadline is that keeping
/// two writers means every future save-path change has to be correct twice, and the
/// lossy one is the one nobody will remember to test.
/// </para>
/// </remarks>
public sealed class LegacySerializingWriter : IConfigWriter
{
    /// <inheritdoc/>
    public string Name => "legacy";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    /// <inheritdoc/>
    /// <remarks>
    /// Ignores <paramref name="originalText"/> and <paramref name="baselineRoot"/>
    /// entirely — that is precisely what makes it lossy, and why it is the fallback
    /// rather than the default.
    /// </remarks>
    public string Render(string? originalText, JsonObject? baselineRoot, JsonObject root, string? headerComment)
    {
        ArgumentNullException.ThrowIfNull(root);
        _ = originalText;
        _ = baselineRoot;

        JsonObject toSerialize;
        if (headerComment != null)
        {
            // "//" first so it lands at the top of the file where a reader will see it.
            toSerialize = new JsonObject { [MetadataKey] = headerComment };
            foreach (KeyValuePair<string, JsonNode?> kv in root)
            {
                if (kv.Key == MetadataKey)
                {
                    continue;
                }

                toSerialize[kv.Key] = kv.Value?.DeepClone();
            }
        }
        else
        {
            // DeepClone so a snapshot is serialized rather than the live object: without
            // it, a mutation on another thread between here and the write could emit
            // malformed JSON.
            toSerialize = root.DeepClone() as JsonObject ?? new JsonObject();
        }

        return toSerialize.ToJsonString(WriteOptions);
    }

    /// <summary>
    /// The tool-written provenance key. Valid JSON that merely looks like a comment —
    /// both Claude schemas allow unknown root properties — and stripped again on load.
    /// </summary>
    internal const string MetadataKey = "//";
}
