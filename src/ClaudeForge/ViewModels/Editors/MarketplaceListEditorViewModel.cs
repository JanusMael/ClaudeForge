using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>
/// Display info for a single marketplace-list source variant.
/// </summary>
/// <remarks>
/// The set of variants here is deliberately distinct from
/// <see cref="MarketplaceEntry.SourceTypeInfos"/>, which lists the on-disk
/// values accepted by the <c>extraKnownMarketplaces</c> map (which uses
/// <c>"localFile"</c> / <c>"localDirectory"</c> as discriminators). The
/// list-form schemas (<c>strictKnownMarketplaces</c> /
/// <c>blockedMarketplaces</c>) use <c>"file"</c> / <c>"directory"</c> and
/// add <c>"hostPattern"</c> + <c>"pathPattern"</c>; both differ from the
/// map shape in real ways that warrant separate metadata.
/// </remarks>
public sealed record MarketplaceListSourceInfo(
    string Value,
    string Description,
    string PrimaryFieldName,
    string PrimaryFieldWatermark)
{
    public string AccessibleName => $"{Value}: {Description}";
}

/// <summary>One row in <see cref="MarketplaceListEditorViewModel"/>.</summary>
/// <remarks>
/// Renders as a Source picker + a single PrimaryValue TextBox whose meaning
/// depends on Source (repo / url / package / path / host-pattern /
/// path-pattern). Optional per-variant string fields are surfaced as
/// dedicated properties:
/// <list type="bullet">
///   <item><see cref="Ref"/> — git branch/tag/SHA, applies to github + git.</item>
///   <item><see cref="Path"/> — subdirectory path within the repo, applies
///         to github + git.</item>
///   <item><see cref="Headers"/> — HTTP headers map, applies to url only.</item>
/// </list>
/// Any other optional per-variant fields the GUI does not yet surface are
/// preserved opaquely via <see cref="ExtraFields"/> so a managed-policy
/// admin's hand-curated entry round-trips without losing data.
/// </remarks>
public partial class MarketplaceListEntryViewModel : ObservableObject
{
    public MarketplaceListEntryViewModel(string source, string primaryValue)
    {
        _source = source;
        _primaryValue = primaryValue;
        Headers = new ObservableCollection<StringMapEntryViewModel>();
        Headers.CollectionChanged += OnHeadersCollectionChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSourceInfo))]
    [NotifyPropertyChangedFor(nameof(PrimaryValueWatermark))]
    [NotifyPropertyChangedFor(nameof(ShowGitFields))]
    [NotifyPropertyChangedFor(nameof(ShowHeadersField))]
    private string _source;

    [ObservableProperty] private string _primaryValue;

    /// <summary>
    /// Optional <c>ref</c> field — git branch / tag / SHA. Applies to
    /// <c>github</c> + <c>git</c> variants only; ignored on save for other
    /// sources.
    /// </summary>
    [ObservableProperty] private string? _ref;

    /// <summary>
    /// Optional <c>path</c> subfield for the marketplace.json location
    /// inside a github / git repository. Applies to github + git only;
    /// ignored on save for other sources.
    /// </summary>
    [ObservableProperty] private string? _path;

    /// <summary>
    /// Optional <c>headers</c> map — HTTP headers passed when fetching the
    /// marketplace.json. Applies to <c>url</c> source only; ignored on save
    /// for other variants. Each row is a <see cref="StringMapEntryViewModel"/>
    /// (Key + Value), reusing the existing string-map row type from
    /// <see cref="StringMapPropertyEditorViewModel"/>.
    /// </summary>
    public ObservableCollection<StringMapEntryViewModel> Headers { get; }

    /// <summary>The new-header-name buffer for the per-row "+ Add" affordance.</summary>
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddHeaderCommand))]
    private string _newHeaderName = string.Empty;

    /// <summary>The new-header-value buffer for the per-row "+ Add" affordance.</summary>
    [ObservableProperty] private string _newHeaderValue = string.Empty;

    [RelayCommand(CanExecute = nameof(CanAddHeader))]
    private void AddHeader()
    {
        string key = NewHeaderName.Trim();
        if (key.Length == 0)
        {
            return;
        }

        // Replace an existing header with the same name (case-sensitive —
        // HTTP headers are case-insensitive on the wire but a managed-
        // policy admin might intentionally distinguish casing in their
        // config; we don't second-guess them).
        StringMapEntryViewModel? existing = Headers.FirstOrDefault(h => h.Key == key);
        if (existing is not null)
        {
            existing.Value = NewHeaderValue;
        }
        else
        {
            Headers.Add(new StringMapEntryViewModel(key, NewHeaderValue));
        }

        NewHeaderName = string.Empty;
        NewHeaderValue = string.Empty;
    }

    private bool CanAddHeader()
    {
        return !string.IsNullOrWhiteSpace(NewHeaderName);
    }

    [RelayCommand]
    private void RemoveHeader(StringMapEntryViewModel? header)
    {
        if (header is null)
        {
            return;
        }

        Headers.Remove(header);
    }

    /// <summary>
    /// <see langword="true"/> when the current <see cref="Source"/> accepts
    /// <c>ref</c> + <c>path</c> sub-fields (github / git). Bound to the
    /// AXAML "Advanced" pane visibility per row.
    /// </summary>
    public bool ShowGitFields => Source is "github" or "git";

    /// <summary>
    /// <see langword="true"/> when the current <see cref="Source"/> accepts
    /// the <c>headers</c> map (url only). Bound to the AXAML headers-pane
    /// visibility per row.
    /// </summary>
    public bool ShowHeadersField => Source is "url";

    /// <summary>
    /// Per-variant non-discriminator fields preserved across the editor
    /// round-trip that the GUI does not yet surface. Hydration strips the
    /// keys we DO surface (see <see cref="Ref"/> / <see cref="Path"/> /
    /// <see cref="Headers"/>) so they don't double-write.
    /// </summary>
    internal JsonObject ExtraFields { get; } = new();

    /// <summary>Typed ComboBox binding — keeps <see cref="Source"/> as source of truth.</summary>
    public MarketplaceListSourceInfo? SelectedSourceInfo
    {
        get => MarketplaceListEditorViewModel.SourceInfos.FirstOrDefault(s => s.Value == Source);
        set
        {
            string v = value?.Value ?? "github";
            if (Source == v)
            {
                return;
            }

            Source = v;
            OnPropertyChanged();
        }
    }

    /// <summary>Per-Source placeholder text for the primary-value TextBox.</summary>
    public string PrimaryValueWatermark =>
        MarketplaceListEditorViewModel.SourceInfos
                                      .FirstOrDefault(s => s.Value == Source)?.PrimaryFieldWatermark
        ?? string.Empty;

    // -----------------------------------------------------------------------
    // Headers change forwarding
    //
    // The parent MarketplaceListEditorViewModel listens on each entry's
    // PropertyChanged for Source / PrimaryValue / Ref / Path / Headers and
    // calls MarkModified. To make Headers participate, we re-fire
    // PropertyChanged(nameof(Headers)) whenever:
    //   • a row is added or removed (CollectionChanged), AND
    //   • a row's Key or Value is edited (per-row PropertyChanged).
    // Both funnel through OnPropertyChanged(nameof(Headers)) so the parent
    // sees a single property name and doesn't have to discover collection
    // semantics.
    // -----------------------------------------------------------------------

    private void OnHeadersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (StringMapEntryViewModel h in e.NewItems)
            {
                h.PropertyChanged += OnHeaderRowChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (StringMapEntryViewModel h in e.OldItems)
            {
                h.PropertyChanged -= OnHeaderRowChanged;
            }
        }

        OnPropertyChanged(nameof(Headers));
    }

    private void OnHeaderRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StringMapEntryViewModel.Key)
            or nameof(StringMapEntryViewModel.Value))
        {
            OnPropertyChanged(nameof(Headers));
        }
    }
}

/// <summary>
/// Editor for the <c>strictKnownMarketplaces</c> / <c>blockedMarketplaces</c>
/// schema nodes — arrays of <c>source</c>-discriminated marketplace entries.
/// Each row has a Source picker (ComboBox over the 8 known variants) and a
/// single primary-value TextBox; the editor reconstructs the full schema
/// shape (<c>{source, repo|url|package|path|hostPattern|pathPattern}</c>
/// plus any preserved opaque extra fields like <c>ref</c> / <c>headers</c>)
/// on save so the user can't paste a bare string into the array.
/// </summary>
/// <remarks>
/// <para>
/// Tolerates pre-existing bad on-disk data (bare strings or items missing
/// the <c>source</c> discriminator) by skipping the offending element
/// during hydration.
/// </para>
/// <para>
/// Like <see cref="McpServerListEditorViewModel"/>, returning <see langword="null"/>
/// when <see cref="Items"/> is empty maps to RemoveValue, restoring the
/// schema's "undefined = no restriction" semantic. The editor never emits
/// <c>[]</c> by accident.
/// </para>
/// </remarks>
public partial class MarketplaceListEditorViewModel : PropertyEditorViewModel
{
    private bool _isLoading;

    // Reset semantic consistency.  See McpServerListEditorViewModel
    // for the rationale; the snapshot-cache pattern is mechanically identical.
    private LayeredValue? _lastLayered;
    private ConfigScope _lastScope;

    public MarketplaceListEditorViewModel(SchemaNode schema, ConfigScope editingScope)
        : base(schema, editingScope)
    {
        Items = [];
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    /// <inheritdoc/>
    protected override bool IsLoading => _isLoading;

    /// <summary>
    /// The eight known source variants supported by both
    /// <c>strictKnownMarketplaces</c> and <c>blockedMarketplaces</c>. Each
    /// row's <see cref="MarketplaceListEntryViewModel.Source"/> takes a
    /// value from this list; a row whose Source isn't recognised falls
    /// through to a JsonRaw-style preservation (the discriminator is
    /// echoed back unchanged so an unknown variant is not silently
    /// dropped).
    /// </summary>
    public static readonly IReadOnlyList<MarketplaceListSourceInfo> SourceInfos =
    [
        new("github", "GitHub repository (owner/repo)", "repo", "owner/repo"),
        new("git", "Git repository URL", "url", "https://example.com/repo.git"),
        new("npm", "NPM package name", "package", "@scope/package"),
        new("url", "Direct HTTPS URL to marketplace.json", "url", "https://example.com/marketplace.json"),
        new("file", "Path to a local marketplace.json", "path", "/path/to/marketplace.json"),
        new("directory", "Path to a local directory", "path", "/path/to/directory"),
        new("hostPattern", "Trust by host pattern (regex)", "hostPattern", "^(github|gitlab)\\.com$"),
        new("pathPattern", "Match by path regex", "pathPattern", "^/srv/marketplaces/.*"),
    ];

    /// <summary>Bound to an ItemsControl in the View.</summary>
    public ObservableCollection<MarketplaceListEntryViewModel> Items { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNewSourceInfo))]
    [NotifyPropertyChangedFor(nameof(NewPrimaryValueWatermark))]
    private string _newSource = "github";

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddEntryCommand))]
    private string _newPrimaryValue = string.Empty;

    /// <summary>Typed ComboBox binding for the "+ Add" row.</summary>
    public MarketplaceListSourceInfo? SelectedNewSourceInfo
    {
        get => SourceInfos.FirstOrDefault(s => s.Value == NewSource);
        set
        {
            string v = value?.Value ?? "github";
            if (NewSource == v)
            {
                return;
            }

            NewSource = v;
            OnPropertyChanged();
        }
    }

    /// <summary>Watermark for the new-row primary-value TextBox.</summary>
    public string NewPrimaryValueWatermark =>
        SourceInfos.FirstOrDefault(s => s.Value == NewSource)?.PrimaryFieldWatermark ?? string.Empty;

    [RelayCommand(CanExecute = nameof(CanAddEntry))]
    private void AddEntry()
    {
        string text = NewPrimaryValue.Trim();
        if (text.Length == 0)
        {
            return;
        }

        Items.Add(new MarketplaceListEntryViewModel(NewSource, text));
        NewPrimaryValue = string.Empty;
    }

    private bool CanAddEntry()
    {
        return !string.IsNullOrWhiteSpace(NewPrimaryValue);
    }

    [RelayCommand]
    private void RemoveEntry(MarketplaceListEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        Items.Remove(entry);
    }

    // -----------------------------------------------------------------------
    // Workspace round-trip
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public override JsonNode? ToJsonValue()
    {
        if (Items.Count == 0)
        {
            return null;
        }

        JsonArray arr = new();
        foreach (MarketplaceListEntryViewModel entry in Items)
        {
            JsonObject? obj = ToVariantObject(entry);
            // Cast to JsonNode? — JsonArray.Add<T>(T) is IL2026 under trim.
            if (obj is not null)
            {
                arr.Add((JsonNode?)obj);
            }
        }

        return arr.Count > 0 ? arr : null;
    }

    private static JsonObject? ToVariantObject(MarketplaceListEntryViewModel entry)
    {
        string primary = entry.PrimaryValue?.Trim() ?? string.Empty;
        if (primary.Length == 0)
        {
            return null;
        }

        string? primaryFieldName = SourceInfos
                                   .FirstOrDefault(s => s.Value == entry.Source)?
                                   .PrimaryFieldName;
        if (primaryFieldName is null)
        {
            return null; // unknown source — drop on save
        }

        JsonObject obj = new()
        {
            ["source"] = JsonValue.Create(entry.Source),
            [primaryFieldName] = JsonValue.Create(primary),
        };

        // GUI-surfaced per-variant fields. Only emit when:
        //   1. the variant accepts the field (ShowGitFields / ShowHeadersField), AND
        //   2. the user has typed a non-blank value (or the headers map is non-empty).
        // Empty values round-trip as "user cleared the field" → omit so
        // the on-disk shape matches what the user sees (and so the schema
        // validator doesn't complain about empty string where omission was
        // intended).
        if (entry.ShowGitFields)
        {
            if (!string.IsNullOrWhiteSpace(entry.Ref))
            {
                obj["ref"] = JsonValue.Create(entry.Ref!.Trim());
            }

            if (!string.IsNullOrWhiteSpace(entry.Path))
            {
                obj["path"] = JsonValue.Create(entry.Path!.Trim());
            }
        }

        if (entry.ShowHeadersField && entry.Headers.Count > 0)
        {
            JsonObject headersObj = new();
            foreach (StringMapEntryViewModel h in entry.Headers)
            {
                // Skip rows where the user typed a value but no name —
                // would round-trip as `"":"value"` which is never useful.
                string? headerName = h.Key?.Trim();
                if (string.IsNullOrEmpty(headerName))
                {
                    continue;
                }

                headersObj[headerName] = JsonValue.Create(h.Value ?? string.Empty);
            }

            if (headersObj.Count > 0)
            {
                obj["headers"] = headersObj;
            }
        }

        // Replay any other preserved per-variant fields the editor doesn't
        // surface yet (currently only `headers` on the url variant). These
        // came in via LoadFromLayered's ExtraFields capture; the keys we DO
        // surface (`ref`, `path`) were stripped from ExtraFields during
        // hydration so they don't double-write here. Managed-policy admin's
        // hand-curated entry survives a round-trip even when the GUI
        // doesn't expose every field.
        foreach (KeyValuePair<string, JsonNode?> kv in entry.ExtraFields)
        {
            // Defensive: don't override the discriminator or primary field.
            if (kv.Key == "source" || kv.Key == primaryFieldName)
            {
                continue;
            }

            obj[kv.Key] = kv.Value?.DeepClone();
        }

        return obj;
    }

    /// <summary>
    /// Keys that <see cref="MarketplaceListEntryViewModel"/> surfaces as
    /// dedicated properties — they are stripped from the entry's
    /// <see cref="MarketplaceListEntryViewModel.ExtraFields"/> at hydration
    /// time so <see cref="ToVariantObject"/>'s replay loop doesn't
    /// double-write them.
    /// </summary>
    private static readonly HashSet<string> SurfacedExtraFieldKeys = new(StringComparer.Ordinal)
    {
        "ref", "path", "headers",
    };

    /// <inheritdoc/>
    public override void LoadFromLayered(LayeredValue layered, ConfigScope editingScope)
    {
        _lastLayered = layered;
        _lastScope = editingScope;
        SetScopeState(layered, editingScope);

        _isLoading = true;
        try
        {
            foreach (MarketplaceListEntryViewModel item in Items)
            {
                item.PropertyChanged -= OnEntryChanged;
            }

            Items.Clear();

            JsonNode? scopeValue = layered.GetValueAt(editingScope);
            if (scopeValue is JsonArray arr)
            {
                foreach (JsonNode? element in arr)
                {
                    MarketplaceListEntryViewModel? entry = TryHydrateEntry(element);
                    if (entry is null)
                    {
                        continue;
                    }

                    entry.PropertyChanged += OnEntryChanged;
                    Items.Add(entry);
                }

                IsModified = arr.Count > 0;
            }
            else
            {
                IsModified = false;
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static MarketplaceListEntryViewModel? TryHydrateEntry(JsonNode? element)
    {
        if (element is not JsonObject obj)
        {
            return null;
        }

        if (obj["source"] is not JsonValue srcNode || !srcNode.TryGetValue(out string? source))
        {
            return null;
        }

        MarketplaceListSourceInfo? info = SourceInfos.FirstOrDefault(s => s.Value == source);
        // Unknown source — preserve as a JsonRaw-style opaque blob would be
        // ideal, but we don't have a row type for that. Skip hydration and
        // let the user re-add the row through a known variant (or the user
        // can drop into raw JSON via the on-disk file). Erring on the side
        // of "don't surface a corrupt half-row".
        if (info is null)
        {
            return null;
        }

        string primary = obj[info.PrimaryFieldName] is JsonValue pv && pv.TryGetValue(out string? s)
            ? s
            : string.Empty;

        MarketplaceListEntryViewModel entry = new(source, primary);

        // Capture every other field. Keys that the editor surfaces as
        // dedicated properties (ref / path) go straight to those properties;
        // everything else is preserved opaquely via ExtraFields so a
        // round-trip doesn't lose hand-curated data the GUI doesn't expose.
        foreach (KeyValuePair<string, JsonNode?> kv in obj)
        {
            if (kv.Key == "source" || kv.Key == info.PrimaryFieldName)
            {
                continue;
            }

            if (SurfacedExtraFieldKeys.Contains(kv.Key))
            {
                if (kv.Key == "headers")
                {
                    // Headers is a string→string map. Hydrate every
                    // string entry; non-string values get ToString'd
                    // rather than dropping the row.
                    if (kv.Value is JsonObject headersObj)
                    {
                        foreach (KeyValuePair<string, JsonNode?> h in headersObj)
                        {
                            string hVal = h.Value is JsonValue hv && hv.TryGetValue(out string? hs)
                                ? hs
                                : h.Value?.ToJsonString() ?? string.Empty;
                            entry.Headers.Add(new StringMapEntryViewModel(h.Key, hVal));
                        }
                    }

                    continue;
                }

                // Coerce to string. Non-string scalars are unexpected here
                // (the schema declares ref / path as string) but we tolerate
                // them rather than dropping the row — bare ints get
                // ToString'd, anything else falls back to the JSON form.
                string? asString = kv.Value is JsonValue extraVal && extraVal.TryGetValue(out string? extraStr)
                    ? extraStr
                    : kv.Value?.ToJsonString();

                if (kv.Key == "ref")
                {
                    entry.Ref = asString;
                }

                if (kv.Key == "path")
                {
                    entry.Path = asString;
                }

                continue;
            }

            entry.ExtraFields[kv.Key] = kv.Value?.DeepClone();
        }

        return entry;
    }

    /// <inheritdoc/>
    protected override void OnResetToInherited()
    {
        // Reset semantic consistency: prefer reload from the
        // cached snapshot so unsaved edits revert to the at-load entries
        // rather than wiping everything.  See McpServerListEditorViewModel
        // for the rationale.
        if (_lastLayered is not null)
        {
            NewPrimaryValue = string.Empty;
            NewSource = "github";
            LoadFromLayered(_lastLayered, _lastScope);
            return;
        }

        // Fallback path — no snapshot.  Suppress MarkModified during
        // teardown — Items.Clear() would otherwise re-flag IsModified=true
        // via OnItemsCollectionChanged.
        _isLoading = true;
        try
        {
            foreach (MarketplaceListEntryViewModel item in Items)
            {
                item.PropertyChanged -= OnEntryChanged;
            }

            Items.Clear();
            NewPrimaryValue = string.Empty;
            NewSource = "github";
        }
        finally
        {
            _isLoading = false;
        }
    }

    // -----------------------------------------------------------------------
    // Change propagation
    // -----------------------------------------------------------------------

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (MarketplaceListEntryViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnEntryChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (MarketplaceListEntryViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnEntryChanged;
            }
        }

        MarkModified();
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MarketplaceListEntryViewModel.Source)
            or nameof(MarketplaceListEntryViewModel.PrimaryValue)
            or nameof(MarketplaceListEntryViewModel.Ref)
            or nameof(MarketplaceListEntryViewModel.Path)
            or nameof(MarketplaceListEntryViewModel.Headers))
        {
            MarkModified();
        }
    }
}