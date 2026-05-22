using System.Text.Json.Nodes;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Core.Settings;

/// <summary>
/// Represents one loaded configuration file at one scope level.
/// </summary>
public sealed class SettingsDocument
{
    public SettingsDocument(ConfigScope scope, string filePath, JsonObject root, bool isReadOnly)
    {
        Scope = scope;
        FilePath = filePath;
        Root = root;
        IsReadOnly = isReadOnly;

        // Snapshot the loaded state so SaveAsync can compute a meaningful change
        // summary. Per System.Text.Json's contract a JsonObject's DeepClone returns a
        // JsonObject — the `as` cast is paranoid. The previous version threw if the
        // cast came back null, which would have aborted workspace load (and the app
        // launch with it, since loading runs synchronously before the UI is up) for
        // any caller that one day constructs the document with a stand-in root.
        // Fall back to a fresh empty JsonObject and log instead so the workspace
        // remains usable; ConfigFileLoader already normalises non-object roots from
        // disk via `node as JsonObject ?? new JsonObject()`, so this is purely a
        // defence against future construction paths.
        if (root.DeepClone() is JsonObject cloned)
        {
            BaselineRoot = cloned;
        }
        else
        {
            Log.Warning(
                "[SettingsDocument] DeepClone() of {FilePath} root returned {Kind}; using empty baseline",
                filePath, root.GetValueKind());
            BaselineRoot = new JsonObject();
        }
    }

    public ConfigScope Scope { get; }
    public string FilePath { get; }

    /// <summary>The parsed JSON root object. Mutations go through SettingsWorkspace.SetValue.</summary>
    public JsonObject Root { get; private set; }

    /// <summary>
    /// Read-only snapshot of <see cref="Root"/> taken at load time and refreshed after
    /// each successful save. The "Save changes" summary dialog diffs current Root against
    /// this baseline to enumerate what the user actually changed.
    /// </summary>
    public JsonObject? BaselineRoot { get; private set; }

    public bool IsReadOnly { get; }
    public bool IsDirty { get; private set; }
    public DateTimeOffset? LastModified { get; private set; }

    /// <summary>
    /// Returns <see langword="true"/> when the current <see cref="Root"/> content
    /// differs from the <see cref="BaselineRoot"/> snapshot.
    /// <para>
    /// Unlike <see cref="IsDirty"/> (which is a write-latch set by any mutation and
    /// cleared only on save), this method performs a structural comparison so it
    /// correctly returns <see langword="false"/> after a set-then-reset cycle — i.e.,
    /// when the user changed a value and then pressed Reset, leaving the document
    /// content identical to what was loaded from disk.
    /// </para>
    /// <para>
    /// strips the tool-managed top-level <c>"//"</c> header-comment key
    /// before comparing.  That key carries a per-save timestamp and would otherwise
    /// produce a "structural diff" that the user-facing per-property dialog
    /// (<c>JsonDiff.Compute</c>) intentionally ignores.  Without this strip,
    /// <see cref="HasActualChanges"/> can report <see langword="true"/> while
    /// <c>JsonDiff.Compute</c> reports zero diffs — the GUI's Save button stays
    /// enabled but the save-changes summary dialog has no rows to show, leading
    /// to the "silent save" user-report shipped via diagnostic logging in
    /// <c>ef2bcb9</c>.  The two diff implementations now agree on what counts
    /// as a user-visible change.
    /// </para>
    /// </summary>
    public bool HasActualChanges()
    {
        if (BaselineRoot == null)
        {
            return CountIgnoringMetadata(Root) > 0;
        }

        return !DeepEqualsIgnoringMetadata(Root, BaselineRoot);
    }

    /// <summary>
    /// Count of top-level keys excluding the tool-managed <c>"//"</c> header.
    /// Used by the no-baseline branch of <see cref="HasActualChanges"/>.
    /// </summary>
    private static int CountIgnoringMetadata(JsonObject root)
    {
        int count = root.Count;
        if (root.ContainsKey(MetadataKey))
        {
            count--;
        }

        return count;
    }

    /// <summary>
    /// Structural equality on two <see cref="JsonObject"/>s with the
    /// tool-managed <c>"//"</c> header-comment key removed from BOTH sides
    /// before comparison.  Mirrors the strip behaviour of
    /// <c>ClaudeForge.Sdk.Diagnostics.JsonDiff.Compute</c> so the two
    /// implementations never disagree on whether content is "dirty".
    /// </summary>
    /// <remarks>
    /// Clones both inputs before stripping so the call doesn't mutate the
    /// live document tree.  The clones are short-lived (single comparison)
    /// so the GC cost is negligible.  Hot-path concern: this runs on every
    /// SDK <see cref="ClaudeConfigClientCore.Changed"/> event; if it ever
    /// shows up in a profile, switch to a per-key DeepEquals walk that
    /// skips the metadata key without cloning.
    /// </remarks>
    private static bool DeepEqualsIgnoringMetadata(JsonObject a, JsonObject b)
    {
        if (!a.ContainsKey(MetadataKey) && !b.ContainsKey(MetadataKey))
        {
            return JsonNode.DeepEquals(a, b);
        }

        // At least one side has the metadata key; clone + strip + compare.
        JsonObject aClean = (JsonObject)a.DeepClone();
        JsonObject bClean = (JsonObject)b.DeepClone();
        aClean.Remove(MetadataKey);
        bClean.Remove(MetadataKey);
        return JsonNode.DeepEquals(aClean, bClean);
    }

    /// <summary>
    /// The tool-managed top-level header-comment key written by
    /// <c>ConfigFileLoader.SaveAsync</c> on every save.  Stripped from
    /// dirty-check comparisons (here) and per-property diffs
    /// (<c>JsonDiff.Compute</c>) so timestamp churn doesn't produce
    /// spurious "dirty" state.
    /// </summary>
    private const string MetadataKey = "//";

    internal void MarkDirty()
    {
        IsDirty = true;
    }

    internal void MarkClean()
    {
        IsDirty = false;
        // Advance the baseline so the next save compares against the just-written state,
        // not the original load state.
        BaselineRoot = Root.DeepClone() as JsonObject;
    }

    internal void UpdateRoot(JsonObject root)
    {
        Root = root;
        LastModified = DateTimeOffset.UtcNow;
        // The incoming root is the freshly-reloaded on-disk state; treat it as the new
        // baseline and clear the dirty flag so the document doesn't appear modified.
        MarkClean();
    }
}