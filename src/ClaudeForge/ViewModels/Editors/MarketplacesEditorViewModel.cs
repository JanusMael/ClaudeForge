using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.JsonHelpers;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketplaceSourceKind = Bennewitz.Ninja.ClaudeForge.Sdk.Marketplaces.MarketplaceSourceKind;

// Alias the SDK namespaces — both ConfigScope and MarketplaceEntry collide
// with names defined in this file. Reaching the SDK types via Sdk.* keeps
// the disambiguation explicit and visible at every call site.

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>Display info for a single marketplace source type — value + human description.</summary>
public sealed record MarketplaceSourceInfo(string Value, string Description)
{
    public string AccessibleName => $"{Value}: {Description}";
}

/// <summary>Observable row representing a single marketplace entry.</summary>
public partial class MarketplaceEntry : ObservableObject
{
    public MarketplaceEntry(string name, string sourceType, string sourceValue)
    {
        _name = name;
        _sourceType = sourceType;
        _sourceValue = sourceValue;
    }

    [ObservableProperty] private string _name;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SelectedSourceTypeInfo))]
    private string _sourceType; // url | github | npm | localFile | localDirectory

    [ObservableProperty] private string _sourceValue; // the URL, repo, path, or package

    /// <summary>
    /// outer-marketplace fields the editor doesn't natively
    /// render (e.g. <c>description</c>, future top-level additions).
    /// Captured by the load path; replayed by ToJsonValue. Mirrors the
    /// <c>McpServerEntry._extraFields</c> pattern.
    /// </summary>
    private readonly JsonObject _extraOuterFields = new();

    /// <summary>
    /// inner <c>source</c>-object fields the editor doesn't
    /// natively render (future schema additions). Captured + replayed
    /// alongside <see cref="_extraOuterFields"/>.
    /// </summary>
    private readonly JsonObject _extraSourceFields = new();

    /// <summary>Bridge for the SDK-backed load path.</summary>
    internal void IngestPreservedFields(JsonObject? outer, JsonObject? inner)
    {
        if (outer is not null)
        {
            foreach (KeyValuePair<string, JsonNode?> kv in outer)
            {
                _extraOuterFields[kv.Key] = kv.Value?.DeepClone();
            }
        }

        if (inner is not null)
        {
            foreach (KeyValuePair<string, JsonNode?> kv in inner)
            {
                _extraSourceFields[kv.Key] = kv.Value?.DeepClone();
            }
        }
    }

    /// <summary>Read-only view for ToJsonValue and tests.</summary>
    internal IReadOnlyDictionary<string, JsonNode?> ExtraOuterFields =>
        _extraOuterFields.ToDictionary(kv => kv.Key, kv => kv.Value);

    internal IReadOnlyDictionary<string, JsonNode?> ExtraSourceFields =>
        _extraSourceFields.ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>All valid source types with descriptions — used as ItemsSource in source ComboBoxes.</summary>
    /// <remarks>
    /// The list MUST cover every <c>SourceType</c> that <see cref="LoadFromLayered"/> can
    /// produce, including <c>"git"</c>. The Claude CLI accepts a <c>"git"</c> source
    /// discriminator in its on-disk schema (with the URL stored in the <c>"url"</c>
    /// field), and a marketplace authored externally may use it. Without an entry here
    /// the ComboBox would resolve to <c>SelectedItem == null</c> for that row and the
    /// user would see an empty dropdown next to a working SourceValue.
    /// <see cref="ToJsonValue"/> already preserves <c>"git"</c> on round-trip via
    /// the matching switch arm.
    /// </remarks>
    public static readonly IReadOnlyList<MarketplaceSourceInfo> SourceTypeInfos =
    [
        new("url", "Direct HTTP/HTTPS URL"),
        new("git", "Git repository URL"),
        new("github", "GitHub repository (owner/repo)"),
        new("npm", "npm package name"),
        new("localFile", "Path to a single local file"),
        new("localDirectory", "Path to a local directory"),
    ];

    /// <summary>Typed ComboBox binding on existing-item rows — keeps <see cref="SourceType"/> as source of truth.</summary>
    public MarketplaceSourceInfo? SelectedSourceTypeInfo
    {
        get => SourceTypeInfos.FirstOrDefault(s => s.Value == SourceType);
        set
        {
            string v = value?.Value ?? "url";
            if (SourceType == v)
            {
                return;
            }

            SourceType = v;
            OnPropertyChanged();
        }
    }
}

/// <summary>
/// Editor for the "extraKnownMarketplaces" object — a dictionary of marketplace
/// configurations, each with a source type and value.
/// </summary>
public partial class MarketplacesEditorViewModel : PropertyEditorViewModel
{
    // SDK client for typed reads. Optional: when null, fall back to the
    // legacy JsonObject-based load path so unit-test fixtures continue to
    // work unchanged. Mirrors the EnabledPluginsEditorViewModel migration.
    private readonly IClaudeConfigClient? _client;

    // Suppresses IsModified during LoadFromLayered bulk population.
    private bool _isLoading;

    // Stored so OnResetToInherited can restore the on-disk state instead of clearing
    // the user's marketplace list. Same pattern as McpServersEditorViewModel /
    // PermissionsEditorViewModel / HooksEditorViewModel / EnabledPluginsEditorViewModel.
    private LayeredValue? _lastLayered;
    private ConfigScope _lastScope;

    // Reset-bug fix (mirrors HooksEditorViewModel._baselineHooksValue,
    // commit 6861748).  Captured at LoadFromLayered as a deep clone of the at-load
    // 'extraKnownMarketplaces' value, so OnResetToInherited can RESTORE the
    // workspace to baseline after the base class's IsModified=false setter
    // triggered a destructive RemoveValue through the live-write path.  Without
    // this, the SDK-backed read in LoadFromLayered (_client.Marketplaces.GetAt)
    // sees the post-removal empty state and the UI rebuilds with zero entries.
    private JsonNode? _baselineMarketplacesValue;

    public MarketplacesEditorViewModel(SchemaNode schema, ConfigScope editingScope)
        : this(schema, editingScope, client: null)
    {
    }

    public MarketplacesEditorViewModel(
        SchemaNode schema,
        ConfigScope editingScope,
        IClaudeConfigClient? client)
        : base(schema, editingScope)
    {
        _client = client;
        Marketplaces = [];
        Marketplaces.CollectionChanged += OnMarketplacesChanged;
    }

    public ObservableCollection<MarketplaceEntry> Marketplaces { get; }

    [ObservableProperty] private string _newName = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SelectedSourceTypeInfo))]
    private string _newSourceType = "url";

    [ObservableProperty] private string _newSourceValue = string.Empty;

    /// <summary>Typed ComboBox binding for the add-row — keeps <see cref="NewSourceType"/> as source of truth.</summary>
    public MarketplaceSourceInfo? SelectedSourceTypeInfo
    {
        get => MarketplaceEntry.SourceTypeInfos.FirstOrDefault(s => s.Value == NewSourceType);
        set
        {
            string v = value?.Value ?? "url";
            if (NewSourceType == v)
            {
                return;
            }

            NewSourceType = v;
            OnPropertyChanged();
        }
    }

    // -----------------------------------------------------------------------
    // Change tracking
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>Routes through the base <see cref="PropertyEditorViewModel.IsLoading"/>
    /// hook so the canonical <c>MarkModified</c> implementation
    /// suppresses spurious flagging during the <c>LoadFromLayered</c> bulk-load.</remarks>
    protected override bool IsLoading => _isLoading;

    private void OnMarketplacesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Subscribe new entries so inline Name/SourceType/SourceValue edits mark dirty.
        if (e.NewItems != null)
        {
            foreach (MarketplaceEntry item in e.NewItems)
            {
                item.PropertyChanged += OnEntryChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (MarketplaceEntry item in e.OldItems)
            {
                item.PropertyChanged -= OnEntryChanged;
            }
        }

        MarkModified();
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkModified();
    }

    // -----------------------------------------------------------------------
    // Commands
    // -----------------------------------------------------------------------

    [RelayCommand]
    private void AddMarketplace()
    {
        string name = NewName.Trim();
        string value = NewSourceValue.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value))
        {
            return;
        }

        if (Marketplaces.Any(m => m.Name == name))
        {
            return;
        }

        Marketplaces.Add(new MarketplaceEntry(name, NewSourceType, value));
        NewName = NewSourceValue = string.Empty;
    }

    [RelayCommand]
    private void RemoveMarketplace(MarketplaceEntry entry)
    {
        Marketplaces.Remove(entry);
    }

    // -----------------------------------------------------------------------
    // Serialization
    // -----------------------------------------------------------------------

    public override JsonNode? ToJsonValue()
    {
        if (Marketplaces.Count == 0)
        {
            return null;
        }

        JsonObject obj = new();
        foreach (MarketplaceEntry m in Marketplaces)
        {
            // Map source type → the JSON key under which the source value is stored.
            // MUST stay in sync with ExtractSourceValue's reverse mapping below — any new
            // SourceType added there needs a matching case here, or ToJsonValue and
            // LoadFromLayered will disagree about the on-disk shape and the round-trip
            // breaks. "git" was missing pre-fix and silently round-tripped to "url".
            string sourceKey = m.SourceType switch
            {
                "github" => "repository",
                "git" => "url",
                "npm" => "package",
                "localFile" => "path",
                "localDirectory" => "path",
                var _ => "url",
            };
            JsonObject sourceObj = new()
            {
                ["source"] = m.SourceType,
                [sourceKey] = m.SourceValue,
            };

            // replay preserved inner-source fields (future
            // schema additions). Typed properties win on collision.
            foreach ((string key, JsonNode? value) in m.ExtraSourceFields)
            {
                if (sourceObj.ContainsKey(key))
                {
                    continue;
                }

                sourceObj[key] = value?.DeepClone();
            }

            JsonObject outerEntry = new() { ["source"] = sourceObj };

            // replay preserved outer fields (description, etc.).
            foreach ((string key, JsonNode? value) in m.ExtraOuterFields)
            {
                if (outerEntry.ContainsKey(key))
                {
                    continue;
                }

                outerEntry[key] = value?.DeepClone();
            }

            obj[m.Name] = outerEntry;
        }

        return obj;
    }

    // -----------------------------------------------------------------------
    // Load / reset
    // -----------------------------------------------------------------------

    public override void LoadFromLayered(LayeredValue layered, ConfigScope editingScope)
    {
        _lastLayered = layered;
        _lastScope = editingScope;
        SetScopeState(layered, editingScope);

        _isLoading = true;
        try
        {
            Marketplaces.Clear();

            // Capture the at-load baseline so OnResetToInherited can write it back
            // to the workspace before reloading.  Deep-clone so subsequent edits
            // don't mutate this snapshot in place.  Re-captured on every
            // LoadFromLayered call — including during reset — so the snapshot
            // tracks the most recent CLEAN state.
            _baselineMarketplacesValue = layered.GetValueAt(editingScope)?.DeepClone();

            // SDK-backed read path. The SDK client and the
            // workspace share state (FromExistingWorkspace), so reading
            // through the typed accessor returns the same on-disk values
            // but as MarketplaceEntry records — including the schema-canonical
            // vs flat shape resolution that previously lived inline below.
            int countAtScope;
            if (_client is not null)
            {
                ConfigScope sdkScope = editingScope;
                IReadOnlyList<Sdk.Marketplaces.MarketplaceEntry> snapshot = _client.Marketplaces.GetAt(sdkScope);
                foreach (Sdk.Marketplaces.MarketplaceEntry sdkEntry in snapshot)
                {
                    MarketplaceEntry entry = new(
                        sdkEntry.Name,
                        FormatSourceKind(sdkEntry.SourceKind),
                        sdkEntry.SourceValue);
                    // bridge the SDK's preserved-fields stash
                    // into the editor's so unknown fields (description,
                    // future schema additions) survive the round-trip.
                    entry.IngestPreservedFields(
                        sdkEntry.PreservedFields,
                        sdkEntry.PreservedSourceFields);
                    Marketplaces.Add(entry);
                }

                countAtScope = snapshot.Count;
            }
            else
            {
                // Legacy path — used by unit-test fixtures that construct
                // the editor without an SDK client. Note this path also
                // handles the format-3 string-shorthand case (a bare URL
                // string as the value) which the SDK accessor's MaterializeFrom
                // currently skips because it requires a JsonObject.
                JsonObject? scopeValue = layered.GetValueAt(editingScope) as JsonObject;
                if (scopeValue != null)
                {
                    foreach (KeyValuePair<string, JsonNode?> kv in scopeValue)
                    {
                        string srcType;
                        string srcValue;

                        switch (kv.Value)
                        {
                            // Format 1 (schema-canonical): { source: { source: "url", url: "..." } }
                            case JsonObject entry when entry["source"] is JsonObject sourceObj:
                                // Prefer "source" discriminator; fall back to "type" (some CLI versions).
                                srcType = sourceObj["source"].AsStringOrNull()
                                          ?? sourceObj["type"].AsStringOrNull()
                                          ?? "url";
                                srcValue = ExtractSourceValue(srcType, sourceObj);
                                break;

                            // Format 2 (flat): { url: "...", command: "..." } — no nested source object.
                            case JsonObject entry:
                                srcType = entry["source"].AsStringOrNull()
                                          ?? entry["type"].AsStringOrNull()
                                          ?? "url";
                                srcValue = ExtractSourceValue(srcType, entry);
                                break;

                            // Format 3 (string shorthand): just "https://..." as the value.
                            case JsonValue jv when jv.TryGetValue(out string? urlStr):
                                srcType = "url";
                                srcValue = urlStr;
                                break;

                            default:
                                continue;
                        }

                        Marketplaces.Add(new MarketplaceEntry(kv.Key, srcType, srcValue));
                    }
                }

                countAtScope = scopeValue?.Count ?? 0;
            }

            // Empty object → not modified (see EnabledPluginsEditorViewModel for rationale).
            IsModified = countAtScope > 0;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// Mirror of <c>MarketplacesAccessor.FormatSourceKind</c> — the SDK enum
    /// must round-trip back through the editor's string-typed
    /// <see cref="MarketplaceEntry.SourceType"/> field. Kept private so the
    /// SDK enum doesn't leak into editor-facing code.
    /// </summary>
    private static string FormatSourceKind(MarketplaceSourceKind kind)
    {
        return kind switch
        {
            MarketplaceSourceKind.Url => "url",
            MarketplaceSourceKind.Git => "git",
            MarketplaceSourceKind.Github => "github",
            MarketplaceSourceKind.Npm => "npm",
            MarketplaceSourceKind.LocalFile => "localFile",
            MarketplaceSourceKind.LocalDirectory => "localDirectory",
            var _ => "url",
        };
    }

    private static string ExtractSourceValue(string srcType, JsonObject obj)
    {
        return srcType switch
        {
            "github" => obj["repository"].AsStringOrNull() ?? obj["repo"].AsStringOrNull() ?? string.Empty,
            "npm" => obj["package"].AsStringOrNull() ?? string.Empty,
            "localFile" => obj["path"].AsStringOrNull() ?? string.Empty,
            "localDirectory" => obj["path"].AsStringOrNull() ?? string.Empty,
            "git" => obj["url"].AsStringOrNull() ?? obj["repository"].AsStringOrNull() ?? string.Empty,
            var _ => obj["url"].AsStringOrNull() ?? string.Empty,
        };
    }

    protected override void OnResetToInherited()
    {
        // Reset-bug fix (mirrors HooksEditorViewModel + Permissions
        // + McpServers reset-restore pattern).  When the SDK client is wired,
        // the base class's IsModified=false → RemoveValue("extraKnownMarketplaces")
        // sequence emptied the workspace before this method ran; LoadFromLayered
        // then read live state via _client.Marketplaces.GetAt and rebuilt with
        // zero entries.  Restore the baseline first so the subsequent reload
        // sees the at-load state.
        if (_client is not null && _lastLayered is not null)
        {
            if (_baselineMarketplacesValue is not null)
            {
                _client.SetValue("extraKnownMarketplaces", _baselineMarketplacesValue.DeepClone(), _lastScope);
            }
            else
            {
                _client.RemoveValue("extraKnownMarketplaces", _lastScope);
            }
        }

        // Reload from the last-persisted scope value so the user's pre-edit marketplace list
        // is restored (undo unsaved additions/removals/edits). Fall back to clearing if we
        // have no stored value (first-load reset before LoadFromLayered ran).
        if (_lastLayered != null)
        {
            LoadFromLayered(_lastLayered, _lastScope);
        }
        else
        {
            _isLoading = true;
            try
            {
                Marketplaces.Clear();
            }
            finally
            {
                _isLoading = false;
            }

            IsModified = false;
        }
    }
}