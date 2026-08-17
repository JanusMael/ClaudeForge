using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Serilog;

namespace Bennewitz.Ninja.AgentForge.Core.FileIO;

/// <summary>
/// Loads and saves SettingsDocument instances from/to disk.
/// </summary>
public static class ConfigFileLoader
{
    /// <summary>
    /// The writer used when a caller does not supply one: comment- and
    /// formatting-preserving.
    /// </summary>
    /// <remarks>
    /// Stateless and thread-safe, so one shared instance is fine. A caller wanting the
    /// old whole-document re-serializer passes a <see cref="LegacySerializingWriter"/>
    /// explicitly — see <c>--writer legacy</c>.
    /// </remarks>
    public static IConfigWriter DefaultWriter { get; } = new JsoncEditWriter();

    /// <summary>
    /// Comments are skipped and trailing commas allowed on read.
    /// </summary>
    /// <remarks>
    /// <b>This is a bug fix, not a nicety.</b> The previous reader used the default
    /// options, which <i>throw</i> on a comment. That exception was caught below and
    /// turned into an empty root — so a single comment (or one stray character) made the
    /// file look like empty settings, and the next save wrote that emptiness over it.
    /// Skipping comments here means such a file loads correctly; preserving them on the
    /// way back out is the writer's job.
    /// </remarks>
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Load a SettingsDocument from a DiscoveredFile.
    /// Returns a document with an empty root if the file does not exist.
    /// </summary>
    public static async Task<SettingsDocument> LoadAsync(DiscoveredFile file, CancellationToken ct = default)
    {
        JsonObject root;
        string? originalText = null;

        if (!file.Exists)
        {
            root = new JsonObject();
        }
        else
        {
            try
            {
                originalText = await File.ReadAllTextAsync(file.FilePath, ct).ConfigureAwait(false);
                JsonNode? node = JsonNode.Parse(originalText, documentOptions: ReadOptions);
                root = node as JsonObject ?? new JsonObject();
                // Strip the tool-written metadata stamp so it is invisible to the editor
                // and gets replaced fresh each save with an up-to-date timestamp.
                root.Remove(LegacySerializingWriter.MetadataKey);
            }
            catch (UnauthorizedAccessException)
            {
                // File exists but the current user lacks read permission — treat as empty
                // rather than crashing; the UI will show the file as missing settings.
                root = new JsonObject();
                originalText = null;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Still resilient to a genuinely corrupt file, but no longer silent: this
                // path means the next save will overwrite whatever is there, which the
                // user deserves to have a record of.
                Log.Warning(ex, "[Config] {Path} could not be parsed; loading it as empty. "
                                + "A subsequent save will overwrite the file's current contents.",
                            file.FilePath);
                root = new JsonObject();
                originalText = null;
            }
        }

        SettingsDocument document = new(file.Scope, file.FilePath, root, file.IsReadOnly);
        document.SetOriginalText(originalText);
        return document;
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
    /// <param name="writer">
    /// Writer to render with. Defaults to <see cref="DefaultWriter"/> (comment-preserving);
    /// pass a <see cref="LegacySerializingWriter"/> to restore the pre-Phase-2 behaviour.
    /// </param>
    public static async Task SaveAsync(SettingsDocument document, string? headerComment = null,
                                       CancellationToken ct = default, IConfigWriter? writer = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.IsReadOnly)
        {
            throw new InvalidOperationException($"Cannot save read-only document: {document.FilePath}");
        }

        string? dir = Path.GetDirectoryName(document.FilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // DeepClone so a snapshot is rendered rather than the live object: a mutation on
        // another thread between here and the write could otherwise emit malformed JSON.
        JsonObject snapshot = document.Root.DeepClone() as JsonObject ?? new JsonObject();

        // The stamp is written only when something else changed, so a save with no pending
        // edits reproduces the file byte for byte. Writing it unconditionally — as this
        // did before — embeds DateTime.Now to the second and therefore guarantees a
        // one-line diff on every save, which makes "no edits, identical bytes" impossible
        // to state honestly. Maintainer decision, recorded in docs/JSONC-WRITER.md.
        string? effectiveHeader = document.HasActualChanges() ? headerComment : null;

        string json = (writer ?? DefaultWriter)
            .Render(document.OriginalText, document.BaselineRoot, snapshot, effectiveHeader);

        if (string.Equals(json, document.OriginalText, StringComparison.Ordinal))
        {
            // Byte-identical: skip the write entirely rather than rewriting the same
            // content. Keeps the file's mtime stable, which matters because a file-watcher
            // reload triggered by our own no-op write would be pure churn.
            document.MarkClean();
            return;
        }

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

        // The text just written becomes the baseline the next save preserves against.
        // Forgetting this would make every save after the first fall back to
        // re-serializing, since the writer would be diffing against stale text.
        document.SetOriginalText(json);
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
    /// <param name="writer">Forwarded to each <see cref="SaveAsync"/> call.</param>
    public static async Task SaveDirtyAsync(SettingsWorkspace workspace, string? headerComment = null,
                                            CancellationToken ct = default, IConfigWriter? writer = null)
    {
        foreach (SettingsDocument doc in workspace.DirtyDocuments())
        {
            await SaveAsync(doc, headerComment, ct, writer);
        }
    }
}