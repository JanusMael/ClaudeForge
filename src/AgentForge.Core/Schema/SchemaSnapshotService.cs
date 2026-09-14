using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.AgentForge.Core.Schema;

/// <summary>
/// Tracks which schema paths the user has already seen across sessions so the UI
/// can badge genuinely new settings.
/// <para>
/// Snapshots are stored as a flat JSON array of dot-separated paths at
/// <c>{ClaudeHome}/cache/schema-snapshot-{schemaName}.json</c>. On first run the
/// file does not exist, so <see cref="LoadSnapshot"/> returns an empty set and
/// every property is considered new. After the user closes the app,
/// <see cref="SaveSnapshot"/> persists the full current path set, clearing the
/// NEW badges on the following launch. Adding a property to a schema between
/// runs will surface only that property as new.
/// </para>
/// </summary>
public sealed class SchemaSnapshotService
{
    private readonly string _directory;

    /// <summary>
    /// The directory snapshots live in. The caller names it — there is no default.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>There used to be a parameterless constructor defaulting to
    /// <c>{ClaudeHome}/cache</c>, and it had no callers at all.</b> A neutral type whose default
    /// resolves to Claude data couples a second product to Claude without naming Claude anywhere
    /// near the call site — the exact shape that made both OpenCode clients report Claude's disk
    /// footprint as their own. Same reasoning as <c>SchemaRegistry</c>'s null cache directory
    /// meaning NO DISK: there is no neutral default, because <c>~/.claude/cache</c> is Claude's
    /// answer and OpenCode's schemas do not belong beneath it.
    /// <c>NeutralLayerDefaultsTests</c> is what keeps it from coming back.
    /// </remarks>
    public SchemaSnapshotService(string directory)
    {
        _directory = directory;
    }

    /// <summary>
    /// Load the persisted path set for <paramref name="schemaName"/> (e.g.
    /// <c>"claude-code-settings"</c>). Returns an empty set if the snapshot
    /// file does not exist or cannot be parsed — first-run behaviour badges
    /// everything as new.
    /// </summary>
    public HashSet<string> LoadSnapshot(string schemaName)
    {
        string path = GetSnapshotPath(schemaName);
        if (!File.Exists(path))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        try
        {
            string json = File.ReadAllText(path);
            // Use source-generated context for trimming compatibility.
            string[]? paths = JsonSerializer.Deserialize(json, CoreJsonContext.Default.StringArray);
            return paths is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(paths, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt / unreadable snapshot: treat as first run rather than crash.
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Persist the current full path set for <paramref name="schemaName"/>.
    /// Call at application exit; failures are swallowed so a filesystem hiccup
    /// cannot block shutdown — the worst case is the user sees the same NEW
    /// badges again next launch.
    /// </summary>
    public void SaveSnapshot(string schemaName, IEnumerable<string> paths)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            string[] ordered = paths.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            // Use source-generated context for trimming compatibility.
            string json = JsonSerializer.Serialize(ordered, CoreJsonContext.Default.StringArray);
            File.WriteAllText(GetSnapshotPath(schemaName), json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Non-fatal: snapshot persistence is best-effort.
        }
    }

    /// <summary>Snapshot file path for a given schema name.</summary>
    public string GetSnapshotPath(string schemaName)
    {
        return Path.Combine(_directory, $"schema-snapshot-{schemaName}.json");
    }
}