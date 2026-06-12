using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.ClaudeForge.Core.Settings;

/// <summary>
/// Aggregates all SettingsDocuments for one configuration file type and provides
/// layered read/write access with merge semantics.
/// </summary>
public sealed class SettingsWorkspace
{
    // Array paths per Claude Code documentation — these MERGE across scopes rather than override
    private static readonly HashSet<string> ArrayPaths = new(StringComparer.Ordinal)
    {
        "claudeMdExcludes",
        "availableModels",
        "httpHookAllowedEnvVars",
        "allowedHttpHookUrls",
        "permissions.allow",
        "permissions.deny",
        "permissions.ask",
        "permissions.additionalDirectories",
        "enabledMcpjsonServers",
        "disabledMcpjsonServers",
        "companyAnnouncements",
    };

    private readonly List<SettingsDocument> _documents;

    public SettingsWorkspace(IEnumerable<SettingsDocument> documents)
    {
        // Sort highest-priority (Managed=0) first
        _documents = documents.OrderBy(d => (int)d.Scope).ToList();
    }

    public IReadOnlyList<SettingsDocument> Documents => _documents;

    /// <summary>
    /// Get the layered value for a top-level JSON key.
    /// </summary>
    public LayeredValue GetLayeredValue(string key)
    {
        List<ScopeEntry> entries = _documents
                                   .Where(d => d.Root.ContainsKey(key))
                                   .Select(d => new ScopeEntry(d.Scope, d.Root[key], d.FilePath))
                                   .ToList();

        bool? isArray = ArrayPaths.Contains(key) ? true : null;
        MergeResult merged = MergeEngine.Merge(entries, isArray);

        return new LayeredValue(key, entries)
        {
            EffectiveValue = merged.EffectiveValue,
            EffectiveScope = merged.EffectiveScope,
        };
    }

    /// <summary>
    /// Returns all top-level keys defined across all documents.
    /// </summary>
    public IEnumerable<string> AllDefinedKeys()
    {
        return _documents.SelectMany(d => d.Root.Select(kv => kv.Key)).Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// Raised after any successful in-memory mutation (<see cref="SetValue"/> or
    /// <see cref="RemoveValue"/>). Listeners can use this to track unsaved changes
    /// without polling <see cref="DirtyDocuments"/>.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Set a top-level value at a specific scope.
    /// Throws if the document for that scope is read-only or not loaded.
    /// </summary>
    public void SetValue(string key, JsonNode? value, ConfigScope scope)
    {
        SettingsDocument doc = GetWritableDocument(scope);
        JsonNode? incoming = value?.DeepClone();

        // True no-op guard: re-setting a key to a value it already holds is not a
        // change. Writing it anyway would MarkDirty + raise Changed for nothing —
        // a "ghost change" surfaced by spurious control events (an AutoCompleteBox
        // reasserting its Text, an ItemsSource swap). JsonNode.DeepEquals is the
        // canonical structural comparison and treats two absent/null nodes as equal.
        if (JsonNode.DeepEquals(doc.Root[key], incoming))
        {
            return;
        }

        doc.Root[key] = incoming;
        doc.MarkDirty();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Remove a top-level key from a specific scope (revert to inheriting from lower scopes).
    /// No-op — and no <see cref="Changed"/> event — when the key is not present, so
    /// spurious dirty-marks are avoided when a reset targets a scope that was already clean.
    /// </summary>
    public void RemoveValue(string key, ConfigScope scope)
    {
        SettingsDocument doc = GetWritableDocument(scope);
        if (!doc.Root.ContainsKey(key))
        {
            return;
        }

        doc.Root.Remove(key);
        doc.MarkDirty();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Compute the full effective merged JSON for this workspace.
    /// </summary>
    public JsonObject ComputeEffective()
    {
        return MergeEngine.ComputeEffective(_documents, ArrayPaths);
    }

    /// <summary>
    /// Returns all documents that have unsaved changes.
    /// </summary>
    public IEnumerable<SettingsDocument> DirtyDocuments()
    {
        return _documents.Where(d => d.IsDirty);
    }

    private SettingsDocument GetWritableDocument(ConfigScope scope)
    {
        SettingsDocument doc = _documents.FirstOrDefault(d => d.Scope == scope)
                               ?? throw new InvalidOperationException($"No document loaded for scope {scope}.");

        if (doc.IsReadOnly)
        {
            throw new InvalidOperationException($"The {scope} scope document is read-only.");
        }

        return doc;
    }
}