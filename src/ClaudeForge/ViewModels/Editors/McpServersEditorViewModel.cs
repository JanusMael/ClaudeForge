using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McpServer = Bennewitz.Ninja.ClaudeForge.Sdk.McpServers.McpServer;
using McpTransport = Bennewitz.Ninja.ClaudeForge.Sdk.McpServers.McpTransport;

// Alias the SDK to keep ConfigScope unambiguous and signal each SDK
// touchpoint at the call site. Mirrors the EnabledPlugins / Marketplaces
// editor migrations.

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>
/// Editor for the "mcpServers" object.
/// Shows a list of server entries; each can be added, edited, or removed.
/// </summary>
public partial class McpServersEditorViewModel : PropertyEditorViewModel
{
    // SDK client for typed reads. Optional: when null, fall back to the
    // legacy JsonObject-based load path so unit-test fixtures continue to
    // work unchanged. Mirrors the EnabledPlugins / Marketplaces migrations.
    private readonly IClaudeConfigClient? _client;

    // Stored so OnResetToInherited can restore the saved on-disk state rather
    // than clearing entirely (which would lose the user's existing servers).
    private LayeredValue? _lastLayered;
    private ConfigScope _lastScope;

    // Reset-bug fix (mirrors HooksEditorViewModel._baselineHooksValue
    // commit 6861748 + PermissionsEditorViewModel._baselinePermissionsValue).
    // Captured at LoadFromLayered as a deep clone of the at-load 'mcpServers'
    // value, so OnResetToInherited can RESTORE the workspace to baseline after
    // the base class's IsModified=false setter triggered a destructive
    // RemoveValue("mcpServers") through the live-write path.  Without this,
    // the SDK-backed read in LoadFromLayered (_client.McpServers.GetAt) sees
    // the post-removal empty state and the UI rebuilds with zero servers —
    // same bug class that affected Hooks (fixed) and Permissions (fixed today).
    private JsonNode? _baselineMcpServersValue;

    // Set to true while LoadFromLayered populates Servers so CollectionChanged
    // does not prematurely flip IsModified during the initial load.
    private bool _isLoading;

    public McpServersEditorViewModel(SchemaNode schema, ConfigScope editingScope)
        : this(schema, editingScope, client: null)
    {
    }

    public McpServersEditorViewModel(
        SchemaNode schema,
        ConfigScope editingScope,
        IClaudeConfigClient? client)
        : base(schema, editingScope)
    {
        _client = client;
        Servers = [];
        Servers.CollectionChanged += OnServersChanged;
    }

    public ObservableCollection<McpServerEntry> Servers { get; }

    [ObservableProperty] private McpServerEntry? _selectedServer;
    [ObservableProperty] private string _newServerName = string.Empty;


    [RelayCommand]
    private void AddServer()
    {
        string name = NewServerName.Trim();
        if (string.IsNullOrEmpty(name) || Servers.Any(s => s.Name == name))
        {
            return;
        }

        // Enforce a maximum name length to keep server names shell-safe.
        if (name.Length > 64)
        {
            return;
        }

        McpServerEntry entry = new(name);
        Servers.Add(entry);
        SelectedServer = entry;
        NewServerName = string.Empty;
    }

    [RelayCommand]
    private void RemoveServer(McpServerEntry? entry)
    {
        if (entry == null)
        {
            return;
        }

        Servers.Remove(entry);
        if (SelectedServer == entry)
        {
            SelectedServer = Servers.FirstOrDefault();
        }
    }

    public override JsonNode? ToJsonValue()
    {
        if (Servers.Count == 0)
        {
            return null;
        }

        JsonObject obj = new();
        foreach (McpServerEntry server in Servers)
        {
            obj[server.Name] = server.ToJson();
        }

        return obj;
    }

    public override void LoadFromLayered(LayeredValue layered, ConfigScope editingScope)
    {
        _lastLayered = layered;
        _lastScope = editingScope;
        SetScopeState(layered, editingScope);

        // 2026-05-01: capture the user's prior selected server name BEFORE
        // we tear down Servers, so the post-rebuild assignment can restore
        // it. Without this, every workspace.Changed-driven reload (which
        // fires during the Save flow's ApplyToWorkspace flush, before the
        // changes dialog has even appeared) snaps the user back to the
        // first server — disorienting when authoring multiple servers in a
        // row. See HooksEditorViewModel for the same fix applied to the
        // Hooks editor's SelectedGroup; the MCP Servers editor exhibits
        // the same drift via the same underlying mechanism.
        string? priorSelectedServerName = SelectedServer?.Name;

        _isLoading = true;
        try
        {
            // Servers.Clear() fires OnServersChanged with OldItems = the cleared
            // entries, so each one's PropertyChanged + nested-collection
            // subscriptions are unhooked cleanly. The _isLoading guard further
            // suppresses MarkModified during this clear-and-rebuild burst.
            Servers.Clear();
            JsonNode? raw = layered.GetValueAt(editingScope);

            // Capture the at-load baseline so OnResetToInherited can write it back
            // to the workspace before reloading.  Deep-clone so subsequent edits
            // don't mutate this snapshot in place.  Re-captured on every
            // LoadFromLayered call — including during reset — so the snapshot
            // tracks the most recent CLEAN state.
            _baselineMcpServersValue = raw?.DeepClone();

            // SDK-backed read path. The SDK and the
            // workspace share state (FromExistingWorkspace), so the typed
            // accessor returns the same servers but as McpServer records.
            // We project each one back into the editor's McpServerEntry
            // shape via FromSdk below so the rest of the editor (validation,
            // command/url toggling, transport-typed combobox) keeps working
            // unchanged.
            if (_client is not null)
            {
                ConfigScope sdkScope = editingScope;
                IReadOnlyDictionary<string, McpServer> snapshot = _client.McpServers.GetAt(sdkScope);
                foreach ((string name, McpServer sdkServer) in snapshot)
                {
                    Servers.Add(McpServerEntryFromSdk(name, sdkServer));
                }
            }
            else
            {
                // Legacy path — used by unit-test fixtures. Preserves the
                // unexpected-shape diagnostic and the "raw != null counts
                // as modified" semantic that the legacy contract relied on.
                JsonObject? scopeValue = raw switch
                {
                    JsonObject jo => jo,
                    // Any other non-null type (array, scalar) means the file was hand-edited
                    // into an unexpected shape.  Treat as empty rather than silently losing data
                    // via a re-parse that coerces the value to null.
                    not null => null,
                    var _ => null,
                };
                if (raw != null && scopeValue == null)
                {
                    Debug.WriteLine(
                        $"[McpServers] Unexpected value type at {editingScope} scope: {raw.GetType().Name} — servers not loaded.");
                }

                if (scopeValue != null)
                {
                    foreach (KeyValuePair<string, JsonNode?> kv in scopeValue)
                    {
                        if (kv.Value is JsonObject serverObj)
                        {
                            Servers.Add(McpServerEntry.FromJson(kv.Key, serverObj));
                        }
                    }
                }
            }

            // IsModified = true  when the editing scope has an explicit value (even if empty)
            // IsModified = false when no value is set at this scope (nothing to save/remove)
            //
            // Note: this is `raw != null` regardless of which load path ran —
            // the SDK path's GetAt returns an empty dict either way (no key
            // OR explicit empty {}), so we still need the raw layered probe
            // to disambiguate "no key" from "explicit empty {}". Keeping
            // the original semantic preserves regression-test contracts.
            IsModified = raw != null;
        }
        finally
        {
            _isLoading = false;
        }

        // Restore the user's prior selection if that server still exists in
        // the rebuilt list (matches by Name — the unique key). Falls back to
        // the historical "first server" pick on first-load
        // (priorSelectedServerName == null) or if the prior server has been
        // removed (e.g. external edit deleted it). See the prior-name capture
        // at the top of this method for the full rationale.
        SelectedServer =
            (priorSelectedServerName is not null
                ? Servers.FirstOrDefault(s => s.Name == priorSelectedServerName)
                : null)
            ?? Servers.FirstOrDefault();

        // Compute which OTHER scopes also have MCP servers so the view can show badge indicators.
        // .Distinct() guards against duplicate-scope entries in the layered value — possible when
        // e.g. multiple ~/.claude/managed-settings.d/*.json files both define `mcpServers`,
        // each producing its own ScopeEntry at ConfigScope.Managed. Without dedup the user sees
        // two identical "Managed" chiclets in the "Defined in scopes" row of the editor header.
        OtherScopesWithData = layered.Entries
                                     .Where(e => e.Scope != editingScope && e.Value is JsonObject jo && jo.Count > 0)
                                     .Select(e => e.Scope)
                                     .Distinct()
                                     .Select(scope => (IEditorScope)ClaudeScope.For(scope))
                                     .ToList();
    }

    protected override void OnResetToInherited()
    {
        // Reset-bug fix (mirrors HooksEditorViewModel.OnResetToInherited,
        // commit 6861748 + PermissionsEditorViewModel reset-restore pattern).  The
        // base ResetToInherited() set IsModified=false BEFORE calling this method,
        // which fired the SettingsGroupEditorViewModel.OnEditorPropertyChanged
        // live-write path with `value=null` — i.e. RemoveValue("mcpServers") on
        // the workspace.  We need to undo that destructive write so the SDK-backed
        // read in LoadFromLayered (_client.McpServers.GetAt) sees the at-load
        // baseline state, not the just-removed empty state.
        //
        // The legacy (non-SDK) load path reads from the LayeredValue parameter
        // rather than the live workspace, so the legacy path was unaffected by
        // the workspace mutation — but production wires the SDK client, and
        // every smoke pass goes through the SDK path.
        if (_client is not null && _lastLayered is not null)
        {
            if (_baselineMcpServersValue is not null)
            {
                _client.SetValue("mcpServers", _baselineMcpServersValue.DeepClone(), _lastScope);
            }
            else
            {
                _client.RemoveValue("mcpServers", _lastScope);
            }
        }

        // Reload from the last-persisted scope value so the user's pre-edit server list is
        // restored (undo unsaved additions/removals) and OtherScopesWithData is refreshed.
        // Fall back to clearing if we have no stored value.
        if (_lastLayered != null)
        {
            LoadFromLayered(_lastLayered, _lastScope);
        }
        else
        {
            Servers.Clear();
            SelectedServer = null;
        }
    }

    // -------------------------------------------------------------------------
    // Subscription bookkeeping for child entries + nested collections.
    //
    // Why the subscriptions matter: McpServerEntry exposes editable fields
    // (Name, Type, Command, Url, HeadersJson) AND nested ObservableCollections
    // (Args, Env, Headers) whose items are themselves ObservableObjects.
    // Without these subscriptions, only add/remove of whole servers (the
    // outer Servers.CollectionChanged event) would dirty the editor — typing
    // a new Command or editing an Arg's Value on a loaded server would silently
    // not enable Save. The subscriptions wire every level so any user edit
    // routes back to MarkModified().
    //
    // The subscribe/unsubscribe handlers are paired strictly: every
    // SubscribeEntry call is mirrored by an UnsubscribeEntry call when the
    // entry leaves Servers (via Remove or via LoadFromLayered's Clear). Same
    // for nested-item subscriptions. Without this discipline, reload would
    // accumulate handlers and MarkModified would fire N times per mutation.
    // -------------------------------------------------------------------------

    private void OnServersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (McpServerEntry entry in e.NewItems)
            {
                SubscribeEntry(entry);
            }
        }

        if (e.OldItems != null)
        {
            foreach (McpServerEntry entry in e.OldItems)
            {
                UnsubscribeEntry(entry);
            }
        }

        MarkModified();
    }

    private void SubscribeEntry(McpServerEntry entry)
    {
        entry.PropertyChanged += OnEntryPropertyChanged;

        entry.Args.CollectionChanged += OnNestedCollectionChanged;
        entry.Env.CollectionChanged += OnNestedCollectionChanged;
        entry.Headers.CollectionChanged += OnNestedCollectionChanged;

        // The entry's nested collections may already contain items at the
        // moment we subscribe (LoadFromLayered populates Args/Env/Headers
        // BEFORE the entry is added to Servers, via McpServerEntry.FromJson).
        // Hook each existing item's PropertyChanged so inline edits to
        // already-loaded args / env vars / headers trigger MarkModified too.
        foreach (ArgItem arg in entry.Args)
        {
            arg.PropertyChanged += OnNestedItemChanged;
        }

        foreach (EnvVar ev in entry.Env)
        {
            ev.PropertyChanged += OnNestedItemChanged;
        }

        foreach (EnvVar hdr in entry.Headers)
        {
            hdr.PropertyChanged += OnNestedItemChanged;
        }
    }

    private void UnsubscribeEntry(McpServerEntry entry)
    {
        entry.PropertyChanged -= OnEntryPropertyChanged;

        entry.Args.CollectionChanged -= OnNestedCollectionChanged;
        entry.Env.CollectionChanged -= OnNestedCollectionChanged;
        entry.Headers.CollectionChanged -= OnNestedCollectionChanged;

        foreach (ArgItem arg in entry.Args)
        {
            arg.PropertyChanged -= OnNestedItemChanged;
        }

        foreach (EnvVar ev in entry.Env)
        {
            ev.PropertyChanged -= OnNestedItemChanged;
        }

        foreach (EnvVar hdr in entry.Headers)
        {
            hdr.PropertyChanged -= OnNestedItemChanged;
        }
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Skip transient input-placeholder fields. McpServerEntry exposes
        // NewArg / NewEnvKey / NewEnvValue as bound text inputs whose value
        // is consumed and cleared by AddArg / AddEnv. Typing into those boxes
        // is not a "save-worthy" change to the server's persisted state — it
        // would otherwise flicker the Save button on every keystroke.
        if (e.PropertyName is nameof(McpServerEntry.NewArg)
            or nameof(McpServerEntry.NewEnvKey)
            or nameof(McpServerEntry.NewEnvValue))
        {
            return;
        }

        MarkModified();
    }

    private void OnNestedCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (INotifyPropertyChanged item in e.NewItems)
            {
                item.PropertyChanged += OnNestedItemChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (INotifyPropertyChanged item in e.OldItems)
            {
                item.PropertyChanged -= OnNestedItemChanged;
            }
        }

        MarkModified();
    }

    private void OnNestedItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkModified();
    }

    /// <inheritdoc/>
    /// <remarks>Routes through the base <see cref="PropertyEditorViewModel.IsLoading"/>
    /// hook so the canonical <c>MarkModified</c> implementation
    /// suppresses spurious flagging during the <c>LoadFromLayered</c> bulk-load.</remarks>
    protected override bool IsLoading => _isLoading;

    /// <summary>
    /// Project a typed <see cref="McpServer"/> record back into the
    /// editor's mutable <see cref="McpServerEntry"/> shape. The editor still
    /// owns binding-time concerns (transport-aware validation, observable
    /// nested collections, "new …" entry-row inputs) that the immutable SDK
    /// record does not.
    /// </summary>
    private static McpServerEntry McpServerEntryFromSdk(string name, McpServer server)
    {
        McpServerEntry entry = new(name)
        {
            Type = FormatTransport(server.Transport),
            Command = server.Command,
            Url = server.Url,
            Description = server.Description,
        };

        if (server.Args is { Count: > 0 } args)
        {
            foreach (string a in args)
            {
                entry.Args.Add(new ArgItem(a));
            }
        }

        if (server.Env is { Count: > 0 } env)
        {
            foreach ((string k, string v) in env)
            {
                entry.Env.Add(new EnvVar(k, v));
            }
        }

        if (server.Headers is { Count: > 0 } headers)
        {
            foreach ((string k, string v) in headers)
            {
                entry.Headers.Add(new EnvVar(k, v));
            }
        }

        // 2026-04-30: copy any fields the SDK didn't model (description,
        // future fields) into the editor's _extraFields stash so
        // McpServerEntry.ToJson re-emits them. Without this bridge the
        // SDK-backed load path would silently drop user data on every
        // save flush. See McpServer.PreservedFields documentation.
        entry.IngestPreservedFields(server.PreservedFields);

        return entry;
    }

    /// <summary>
    /// SDK enum → editor's string-typed <see cref="McpServerEntry.Type"/>.
    /// Note the asymmetry: the SDK's <c>StreamableHttp</c> renders as the
    /// editor's <c>"http"</c> ComboBox value (matches
    /// <see cref="McpServerEntry.TransportInfos"/>) — emitting
    /// <c>"streamable-http"</c> here would give the user a blank dropdown
    /// because no <c>McpTransportInfo</c> entry has that value.
    /// </summary>
    private static string FormatTransport(McpTransport transport)
    {
        return transport switch
        {
            McpTransport.Stdio => "stdio",
            McpTransport.Sse => "sse",
            McpTransport.StreamableHttp => "http",
            var _ => "stdio",
        };
    }
}