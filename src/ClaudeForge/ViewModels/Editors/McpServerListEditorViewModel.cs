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
/// Discriminator for <see cref="McpServerListEntryViewModel"/>. Each row in
/// <c>allowedMcpServers</c> / <c>deniedMcpServers</c> picks exactly one of
/// these variants and provides the corresponding payload.
/// </summary>
public enum McpServerMatchKind
{
    /// <summary><c>{ "serverName": "alpha-mcp" }</c> — exact server name (regex constrained).</summary>
    ByName,

    /// <summary><c>{ "serverCommand": ["node","/path/to/server.js"] }</c> — exact stdio launch command + args.</summary>
    ByCommand,

    /// <summary><c>{ "serverUrl": "https://*.example.com/*" }</c> — URL pattern, supports wildcards.</summary>
    ByUrl,
}

/// <summary>
/// One row in <see cref="McpServerListEditorViewModel"/>. Holds the
/// discriminator (<see cref="Kind"/>) and a single user-typed payload
/// (<see cref="Text"/>) that the parent editor interprets per
/// <see cref="Kind"/>: <see cref="McpServerMatchKind.ByName"/> /
/// <see cref="McpServerMatchKind.ByUrl"/> are the raw string;
/// <see cref="McpServerMatchKind.ByCommand"/> is split on newlines into the
/// array elements (one arg per line, leading / trailing whitespace trimmed).
/// </summary>
public partial class McpServerListEntryViewModel : ObservableObject
{
    public McpServerListEntryViewModel(McpServerMatchKind kind, string text)
    {
        _kind = kind;
        _text = text;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCommandKind))]
    [NotifyPropertyChangedFor(nameof(InputWatermark))]
    private McpServerMatchKind _kind;

    [ObservableProperty] private string _text;

    /// <summary><see langword="true"/> when this row's input area should accept multi-line text (one arg per line).</summary>
    public bool IsCommandKind => Kind == McpServerMatchKind.ByCommand;

    /// <summary>Per-kind placeholder shown in the input box.</summary>
    public string InputWatermark => Kind switch
    {
        McpServerMatchKind.ByName => "alpha-mcp",
        McpServerMatchKind.ByCommand => "one arg per line\nnode\n/path/to/server.js",
        McpServerMatchKind.ByUrl => "https://*.example.com/*",
        var _ => string.Empty,
    };
}

/// <summary>
/// Editor for the <c>allowedMcpServers</c> / <c>deniedMcpServers</c>
/// schema nodes — arrays of discriminated-union objects. Each row picks
/// one of three match variants (Name / Command / URL) and provides the
/// corresponding payload; the editor reconstructs the
/// <c>{serverName|serverCommand|serverUrl: …}</c> JsonObject on save.
/// </summary>
/// <remarks>
/// <para>
/// Tolerates pre-existing bad on-disk data: when the loaded scope value is
/// not a JsonArray, the rows simply start empty. When an item is missing
/// the discriminator field (or has multiple), it is skipped on load so the
/// editor doesn't surface a corrupt half-state. The user can save to
/// replace bad data with a well-formed array, or click Reset to clear the
/// property outright (which removes the key — the schema's "if undefined,
/// all servers are allowed" semantics).
/// </para>
/// <para>
/// Empty rows (no payload) are skipped on save so partially-filled "+ Add"
/// rows don't write `{ "serverName": "" }` and re-trigger the schema
/// banner on next reload.
/// </para>
/// </remarks>
public partial class McpServerListEditorViewModel : PropertyEditorViewModel
{
    private bool _isLoading;

    // Reset semantic consistency.  Cached so OnResetToInherited
    // can restore the at-load entries instead of clearing.  Mirrors the
    // top-level compound editors' restore-on-reset pattern (Permissions /
    // Hooks / McpServers / Marketplaces / EnabledPlugins).  These sub-list
    // editors read from the LayeredValue snapshot directly via
    // layered.GetValueAt(scope), so the snapshot's JsonArray reference is
    // stable across the parent's RemoveValue mutation that the base class
    // triggers on reset — no SDK SetValue restore required (unlike the
    // top-level compounds).
    private LayeredValue? _lastLayered;
    private ConfigScope _lastScope;

    public McpServerListEditorViewModel(SchemaNode schema, ConfigScope editingScope)
        : base(schema, editingScope)
    {
        Items = [];
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    /// <inheritdoc/>
    protected override bool IsLoading => _isLoading;

    /// <summary>Bound to an ItemsControl in the View.</summary>
    public ObservableCollection<McpServerListEntryViewModel> Items { get; }

    /// <summary>The three possible discriminator values, populated as a ComboBox source.</summary>
    public IReadOnlyList<McpServerMatchKind> AvailableKinds { get; } =
        [McpServerMatchKind.ByName, McpServerMatchKind.ByCommand, McpServerMatchKind.ByUrl];

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddEntryCommand))]
    private McpServerMatchKind _newKind = McpServerMatchKind.ByName;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddEntryCommand))]
    private string _newText = string.Empty;

    [RelayCommand(CanExecute = nameof(CanAddEntry))]
    private void AddEntry()
    {
        string text = NewText.Trim();
        if (text.Length == 0)
        {
            return;
        }

        Items.Add(new McpServerListEntryViewModel(NewKind, text));
        NewText = string.Empty;
    }

    private bool CanAddEntry()
    {
        return !string.IsNullOrWhiteSpace(NewText);
    }

    [RelayCommand]
    private void RemoveEntry(McpServerListEntryViewModel? entry)
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
        // The schema treats undefined and `[]` differently:
        //   undefined → "no restriction" (anything allowed)
        //   []        → "lockdown" (nothing allowed)
        // Returning null when Items is empty maps to RemoveValue, restoring
        // undefined semantics. The user can produce an empty-array lockdown
        // explicitly by adding rows then clearing them via the inline JSON
        // raw-edit affordance — the typed editor itself won't emit `[]`.
        if (Items.Count == 0)
        {
            return null;
        }

        JsonArray arr = new();
        foreach (McpServerListEntryViewModel entry in Items)
        {
            JsonObject? node = ToVariantObject(entry);
            // Cast to JsonNode? — JsonArray.Add<T>(T) is IL2026 under trim.
            if (node is not null)
            {
                arr.Add((JsonNode?)node);
            }
        }

        return arr.Count > 0 ? arr : null;
    }

    private static JsonObject? ToVariantObject(McpServerListEntryViewModel entry)
    {
        string text = entry.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return null; // Skip blank rows on save.
        }

        return entry.Kind switch
        {
            McpServerMatchKind.ByName => new JsonObject
            {
                ["serverName"] = JsonValue.Create(text),
            },
            McpServerMatchKind.ByUrl => new JsonObject
            {
                ["serverUrl"] = JsonValue.Create(text),
            },
            McpServerMatchKind.ByCommand => BuildCommandObject(text),
            var _ => null,
        };
    }

    private static JsonObject? BuildCommandObject(string text)
    {
        // One arg per line; trim per-line; drop empties so trailing newlines
        // don't produce phantom "" elements in the array.
        List<string> parts = text
                             .Split('\n')
                             .Select(s => s.Trim('\r', ' ', '\t'))
                             .Where(s => s.Length > 0)
                             .ToList();
        if (parts.Count == 0)
        {
            return null;
        }

        JsonArray arr = new();
        // Cast to JsonNode? — JsonArray.Add<T>(T) is IL2026 under trim.
        foreach (string p in parts)
        {
            arr.Add((JsonNode?)JsonValue.Create(p));
        }

        return new JsonObject { ["serverCommand"] = arr };
    }

    /// <inheritdoc/>
    public override void LoadFromLayered(LayeredValue layered, ConfigScope editingScope)
    {
        _lastLayered = layered;
        _lastScope = editingScope;
        SetScopeState(layered, editingScope);

        _isLoading = true;
        try
        {
            foreach (McpServerListEntryViewModel item in Items)
            {
                item.PropertyChanged -= OnEntryChanged;
            }

            Items.Clear();

            JsonNode? scopeValue = layered.GetValueAt(editingScope);
            if (scopeValue is JsonArray arr)
            {
                foreach (JsonNode? element in arr)
                {
                    McpServerListEntryViewModel? entry = TryHydrateEntry(element);
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
                // Bad on-disk data (bare string from the old corrupting
                // StringArray fallback, or unexpected shape). Start empty;
                // the user can save to replace, or reset to clear.
                IsModified = false;
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static McpServerListEntryViewModel? TryHydrateEntry(JsonNode? element)
    {
        if (element is not JsonObject obj)
        {
            return null;
        }

        // Detect the discriminator by which property is set. Reject items that
        // declare more than one (schema would refuse them anyway) so we
        // don't surface a half-corrupt row the user can't fix without raw JSON.
        bool hasName = obj.ContainsKey("serverName");
        bool hasCommand = obj.ContainsKey("serverCommand");
        bool hasUrl = obj.ContainsKey("serverUrl");
        int setCount = (hasName ? 1 : 0) + (hasCommand ? 1 : 0) + (hasUrl ? 1 : 0);
        if (setCount != 1)
        {
            return null;
        }

        if (hasName && obj["serverName"] is JsonValue nv && nv.TryGetValue(out string? name))
        {
            return new McpServerListEntryViewModel(McpServerMatchKind.ByName, name);
        }

        if (hasUrl && obj["serverUrl"] is JsonValue uv && uv.TryGetValue(out string? url))
        {
            return new McpServerListEntryViewModel(McpServerMatchKind.ByUrl, url);
        }

        if (hasCommand && obj["serverCommand"] is JsonArray cmdArr)
        {
            List<string> lines = cmdArr
                                 .Select(n => n is JsonValue v && v.TryGetValue(out string? s) ? s : null)
                                 .Where(s => s is not null)
                                 .Cast<string>()
                                 .ToList();
            return new McpServerListEntryViewModel(
                McpServerMatchKind.ByCommand,
                string.Join('\n', lines));
        }

        return null;
    }

    /// <inheritdoc/>
    protected override void OnResetToInherited()
    {
        // Reset semantic consistency: prefer reload from the
        // cached snapshot so unsaved edits revert to the at-load entries
        // rather than wiping everything.  Mirrors the top-level compound
        // editors' restore-on-reset pattern.  Falls back to clearing when
        // no snapshot is available (first-load reset before LoadFromLayered
        // ran).
        if (_lastLayered is not null)
        {
            // Reset transient input fields here (LoadFromLayered does not
            // touch them).  The user's pending NewText / NewKind input is
            // unfinished work that the user explicitly chose to discard
            // by clicking Reset.
            NewText = string.Empty;
            NewKind = McpServerMatchKind.ByName;
            LoadFromLayered(_lastLayered, _lastScope);
            return;
        }

        // Fallback path — no snapshot.  Suppress MarkModified during
        // teardown — Items.Clear() would otherwise re-flag IsModified=true
        // via OnItemsCollectionChanged.
        _isLoading = true;
        try
        {
            foreach (McpServerListEntryViewModel item in Items)
            {
                item.PropertyChanged -= OnEntryChanged;
            }

            Items.Clear();
            NewText = string.Empty;
            NewKind = McpServerMatchKind.ByName;
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
            foreach (McpServerListEntryViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnEntryChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (McpServerListEntryViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnEntryChanged;
            }
        }

        MarkModified();
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(McpServerListEntryViewModel.Kind)
            or nameof(McpServerListEntryViewModel.Text))
        {
            MarkModified();
        }
    }
}