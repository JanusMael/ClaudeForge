using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Builds the merged *effective* JSON for a workspace, stamped with a human-readable
/// <c>"//"</c> header-comment key. Extracted from the original <c>MainWindowViewModel.ExportAsync</c>
/// so that both Export and (future) Backup callers can share a single implementation.
/// </summary>
public static class EffectiveConfigBuilder
{
    /// <summary>
    /// Composes the merged effective settings for <paramref name="workspace"/>.
    /// The <c>"//"</c> key is written first so it appears at the top of the file
    /// when the resulting JSON is opened in a text editor — both Claude Code and
    /// Claude Desktop ignore unknown root properties.
    /// </summary>
    /// <param name="workspace">Workspace whose effective merged state should be serialised.</param>
    /// <param name="headerComment">Text written into the leading <c>"//"</c> key.</param>
    /// <returns>A freshly built <see cref="JsonObject"/>. Callers own it and may mutate further.</returns>
    public static JsonObject BuildEffective(SettingsWorkspace workspace, string headerComment)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return Stamp(workspace.ComputeEffective(), headerComment);
    }

    /// <summary>
    /// Stamp <paramref name="effective"/> with a leading <c>"//"</c> header
    /// comment. Returns a freshly built <see cref="JsonObject"/>; the input
    /// is not mutated.
    /// </summary>
    /// <param name="effective">
    /// Pre-computed merged effective JSON — typically the result of
    /// <see cref="SettingsWorkspace.ComputeEffective"/> or the SDK's
    /// equivalent snapshot helper.
    /// </param>
    /// <param name="headerComment">Text written into the leading <c>"//"</c> key.</param>
    /// <remarks>
    /// 4.3.7 step 12: extracted so the GUI's export flow can run the merge
    /// via the SDK (<c>client.ComputeEffectiveSnapshot()</c>) and then
    /// stamp without taking a workspace dependency on this helper.
    /// </remarks>
    public static JsonObject Stamp(JsonObject effective, string headerComment)
    {
        ArgumentNullException.ThrowIfNull(effective);

        JsonObject stamped = new() { ["//"] = headerComment };
        foreach (KeyValuePair<string, JsonNode?> kv in effective)
        {
            stamped[kv.Key] = kv.Value?.DeepClone();
        }

        return stamped;
    }
}