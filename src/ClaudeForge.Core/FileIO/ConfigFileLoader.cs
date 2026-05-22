using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Core.FileIO;

/// <summary>
/// Loads and saves SettingsDocument instances from/to disk.
/// </summary>
public static class ConfigFileLoader
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Load a SettingsDocument from a DiscoveredFile.
    /// Returns a document with an empty root if the file does not exist.
    /// </summary>
    public static async Task<SettingsDocument> LoadAsync(DiscoveredFile file, CancellationToken ct = default)
    {
        JsonObject root;

        if (!file.Exists)
        {
            root = new JsonObject();
        }
        else
        {
            try
            {
                await using FileStream stream = File.OpenRead(file.FilePath);
                JsonNode? node = await JsonNode.ParseAsync(stream, cancellationToken: ct);
                root = node as JsonObject ?? new JsonObject();
                // Strip the tool-written metadata stamp so it is invisible to the editor
                // and gets replaced fresh each save with an up-to-date timestamp.
                root.Remove("//");
            }
            catch (UnauthorizedAccessException)
            {
                // File exists but the current user lacks read permission — treat as empty
                // rather than crashing; the UI will show the file as missing settings.
                root = new JsonObject();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Treat corrupt/unreadable files as empty rather than crashing.
                root = new JsonObject();
                _ = ex; // suppress IDE warning; error is intentionally swallowed for resilience
            }
        }

        return new SettingsDocument(file.Scope, file.FilePath, root, file.IsReadOnly);
    }

    /// <summary>
    /// Save a dirty SettingsDocument back to disk.
    /// Creates parent directories if they don't exist.
    /// </summary>
    /// <param name="document">Document to persist.</param>
    /// <param name="headerComment">
    /// Optional string written as a <c>"//"</c> top-level key so it appears
    /// visually as a comment when the file is opened in a text editor.
    /// The key is valid JSON (both schemas allow unknown root properties) and
    /// is stripped on the next <see cref="LoadAsync"/> call so it stays
    /// invisible inside this tool.  Pass <c>null</c> to omit the stamp.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task SaveAsync(SettingsDocument document, string? headerComment = null,
                                       CancellationToken ct = default)
    {
        if (document.IsReadOnly)
        {
            throw new InvalidOperationException($"Cannot save read-only document: {document.FilePath}");
        }

        string? dir = Path.GetDirectoryName(document.FilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Build the serialization object: "//" first so it appears at the top of
        // the file, then all real keys (skipping any stale "//" already in root).
        JsonObject toSerialize;
        if (headerComment != null)
        {
            toSerialize = new JsonObject { ["//"] = headerComment };
            foreach (KeyValuePair<string, JsonNode?> kv in document.Root)
            {
                if (kv.Key == "//")
                {
                    continue;
                }

                toSerialize[kv.Key] = kv.Value?.DeepClone();
            }
        }
        else
        {
            // DeepClone so we serialise a snapshot rather than the live object.
            // This prevents a race where the main thread mutates Root between
            // the ToJsonString call and the write, which could produce malformed JSON.
            toSerialize = document.Root.DeepClone() as JsonObject ?? new JsonObject();
        }

        string json = toSerialize.ToJsonString(WriteOptions);

        // Atomic write: write to a temp file first, then rename into place so a
        // crash or cancellation mid-write never leaves a corrupt or truncated file.
        string tmp = $"{document.FilePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
            File.Move(tmp, document.FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // Catch *everything* (including OperationCanceledException) so the temp
            // file is removed before propagating the original failure. We re-throw
            // immediately afterwards, satisfying the project rule "log or re-throw".
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
            {
                _ = cleanupEx;
            }

            throw;
        }

        document.MarkClean();
    }

    /// <summary>
    /// Load all files in the given workspace definition, returning a populated workspace.
    /// </summary>
    public static async Task<SettingsWorkspace> LoadWorkspaceAsync(
        IReadOnlyList<DiscoveredFile> files,
        CancellationToken ct = default)
    {
        List<SettingsDocument> documents = new(files.Count);
        foreach (DiscoveredFile file in files)
        {
            SettingsDocument doc = await LoadAsync(file, ct);
            documents.Add(doc);
        }

        return new SettingsWorkspace(documents);
    }

    /// <summary>
    /// Save all dirty documents in a workspace.
    /// </summary>
    /// <param name="workspace">Workspace whose dirty documents should be persisted.</param>
    /// <param name="headerComment">Forwarded to each <see cref="SaveAsync"/> call.  See that
    /// method for details.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task SaveDirtyAsync(SettingsWorkspace workspace, string? headerComment = null,
                                            CancellationToken ct = default)
    {
        foreach (SettingsDocument doc in workspace.DirtyDocuments())
        {
            await SaveAsync(doc, headerComment, ct);
        }
    }
}