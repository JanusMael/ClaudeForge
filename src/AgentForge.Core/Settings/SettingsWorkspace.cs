using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.AgentForge.Core.Settings;

/// <summary>
/// Aggregates all SettingsDocuments for one configuration file type and provides
/// layered read/write access with merge semantics.
/// </summary>
/// <remarks>
/// The merge rules arrive as an <see cref="IMergePolicy"/>. This class used to hold Claude's
/// list of union-merged paths as a private static, which made every workspace in the process
/// a Claude workspace no matter which product opened it. The policy is a required
/// constructor argument rather than a defaulted one, so a new product cannot inherit
/// Claude's rules by omission.
/// </remarks>
public sealed class SettingsWorkspace
{
    private readonly List<SettingsDocument> _documents;
    private readonly IMergePolicy _mergePolicy;

    public SettingsWorkspace(IEnumerable<SettingsDocument> documents, IMergePolicy mergePolicy)
    {
        ArgumentNullException.ThrowIfNull(mergePolicy);
        _mergePolicy = mergePolicy;
        // Sort highest-priority first, by the scope's own declared ordinal rather than a
        // cast, so a product with a different ladder orders correctly without changes here.
        _documents = documents.OrderBy(d => d.Scope.Ordinal).ToList();
    }

    /// <summary>The product's merge rules, as supplied at construction.</summary>
    public IMergePolicy MergePolicy => _mergePolicy;

    public IReadOnlyList<SettingsDocument> Documents => _documents;

    /// <summary>
    /// Documents whose file could not be read as JSON — see
    /// <see cref="SettingsDocument.LoadFailure"/>. Empty for a clean load.
    /// </summary>
    /// <remarks>
    /// Exists so a caller can decide whether this workspace is fit to swap into a live
    /// application <i>before</i> doing so. A workspace built over an unparseable file is
    /// structurally valid and looks merely empty, which is exactly how an unparseable file
    /// used to be swapped in and then saved over the user's real settings.
    /// </remarks>
    public IEnumerable<SettingsDocument> FailedDocuments =>
        _documents.Where(d => d.LoadFailure is not null);

    /// <summary>
    /// Get the layered value for a top-level JSON key.
    /// </summary>
    public LayeredValue GetLayeredValue(string key)
    {
        List<ScopeEntry> entries = _documents
                                   .Where(d => d.Root.ContainsKey(key))
                                   .Select(d => new ScopeEntry(d.Scope, d.Root[key], d.FilePath))
                                   .ToList();

        MergeResult merged = MergeEngine.Merge(entries, key, _mergePolicy);

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
        return MergeEngine.ComputeEffective(_documents, _mergePolicy);
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