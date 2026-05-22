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
/// One MCP-server config block under a plugin entry. The schema's
/// per-config-value type is <c>anyOf&lt;string|number|boolean|array&lt;string&gt;&gt;</c>;
/// the GUI surfaces only string-typed values directly. Non-string
/// originals are preserved opaquely via <see cref="ExtraConfigs"/> so a
/// hand-curated boolean / numeric / array entry survives the round-trip
/// without losing data.
/// </summary>
public partial class PluginServerConfigViewModel : ObservableObject
{
    public PluginServerConfigViewModel(string serverName)
    {
        _serverName = serverName;
        Configs = new ObservableCollection<StringMapEntryViewModel>();
        Configs.CollectionChanged += OnConfigsCollectionChanged;
    }

    [ObservableProperty] private string _serverName;

    /// <summary>Surfaced string-typed config rows.</summary>
    public ObservableCollection<StringMapEntryViewModel> Configs { get; }

    /// <summary>The new-config-key buffer for the per-server "+ Add" affordance.</summary>
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddConfigCommand))]
    private string _newConfigKey = string.Empty;

    /// <summary>The new-config-value buffer for the per-server "+ Add" affordance.</summary>
    [ObservableProperty] private string _newConfigValue = string.Empty;

    /// <summary>
    /// Per-server non-string config values preserved across the editor
    /// round-trip. Keyed by the original config name so they replay in
    /// place during <see cref="BuildOnDiskObject"/>.
    /// </summary>
    internal JsonObject ExtraConfigs { get; } = new();

    [RelayCommand(CanExecute = nameof(CanAddConfig))]
    private void AddConfig()
    {
        string key = NewConfigKey.Trim();
        if (key.Length == 0)
        {
            return;
        }

        StringMapEntryViewModel? existing = Configs.FirstOrDefault(c => c.Key == key);
        if (existing is not null)
        {
            existing.Value = NewConfigValue;
        }
        else
        {
            Configs.Add(new StringMapEntryViewModel(key, NewConfigValue));
        }

        NewConfigKey = string.Empty;
        NewConfigValue = string.Empty;
    }

    private bool CanAddConfig()
    {
        return !string.IsNullOrWhiteSpace(NewConfigKey);
    }

    [RelayCommand]
    private void RemoveConfig(StringMapEntryViewModel? config)
    {
        if (config is null)
        {
            return;
        }

        Configs.Remove(config);
    }

    /// <summary>
    /// Build the JsonObject this server's config map will become on disk.
    /// String-typed rows go first; opaquely-preserved entries (e.g. a
    /// numeric or array value the GUI doesn't surface) replay afterwards.
    /// Returns <see langword="null"/> when the server name is blank
    /// (the parent plugin VM uses null to skip the entry on save).
    /// </summary>
    internal JsonObject? BuildOnDiskObject()
    {
        if (string.IsNullOrWhiteSpace(ServerName))
        {
            return null;
        }

        JsonObject obj = new();
        foreach (StringMapEntryViewModel c in Configs)
        {
            string? key = c.Key?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            obj[key] = JsonValue.Create(c.Value ?? string.Empty);
        }

        // Replay opaque non-string entries we held back during hydration.
        foreach (KeyValuePair<string, JsonNode?> kv in ExtraConfigs)
        {
            // Defensive: never let an opaque entry overwrite a surfaced
            // string row of the same key (the GUI would have stripped it
            // during hydration, but cheap to assert).
            if (obj.ContainsKey(kv.Key))
            {
                continue;
            }

            obj[kv.Key] = kv.Value?.DeepClone();
        }

        return obj;
    }

    // -----------------------------------------------------------------------
    // Configs change forwarding — see comment on the Marketplace entry's
    // Headers forwarding for the pattern. Re-fires PropertyChanged(nameof(
    // Configs)) on every collection change AND every per-row Key/Value
    // change so the parent plugin VM's existing PropertyChanged listener
    // can funnel into MarkModified via a single property name.
    // -----------------------------------------------------------------------
    private void OnConfigsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (StringMapEntryViewModel c in e.NewItems)
            {
                c.PropertyChanged += OnConfigRowChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (StringMapEntryViewModel c in e.OldItems)
            {
                c.PropertyChanged -= OnConfigRowChanged;
            }
        }

        OnPropertyChanged(nameof(Configs));
    }

    private void OnConfigRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StringMapEntryViewModel.Key)
            or nameof(StringMapEntryViewModel.Value))
        {
            OnPropertyChanged(nameof(Configs));
        }
    }
}

/// <summary>
/// One plugin entry under <c>pluginConfigs</c>. Each plugin owns a list
/// of <see cref="PluginServerConfigViewModel"/> rows (the schema's
/// <c>mcpServers</c> map). Plugin-level extra fields the GUI doesn't
/// surface are preserved opaquely via <see cref="ExtraFields"/>; the
/// schema today defines no plugin-level fields beyond <c>mcpServers</c>
/// but the round-trip is defensive against future additions.
/// </summary>
public partial class PluginConfigEntryViewModel : ObservableObject
{
    public PluginConfigEntryViewModel(string pluginId)
    {
        _pluginId = pluginId;
        Servers = new ObservableCollection<PluginServerConfigViewModel>();
        Servers.CollectionChanged += OnServersCollectionChanged;
    }

    [ObservableProperty] private string _pluginId;

    public ObservableCollection<PluginServerConfigViewModel> Servers { get; }

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddServerCommand))]
    private string _newServerName = string.Empty;

    /// <summary>
    /// Plugin-level non-mcpServers fields the GUI doesn't surface,
    /// preserved opaquely. Empty in the current schema; defensive.
    /// </summary>
    internal JsonObject ExtraFields { get; } = new();

    [RelayCommand(CanExecute = nameof(CanAddServer))]
    private void AddServer()
    {
        string name = NewServerName.Trim();
        if (name.Length == 0)
        {
            return;
        }

        PluginServerConfigViewModel? existing = Servers.FirstOrDefault(s => s.ServerName == name);
        if (existing is not null)
        {
            return; // no-op on duplicate
        }

        Servers.Add(new PluginServerConfigViewModel(name));
        NewServerName = string.Empty;
    }

    private bool CanAddServer()
    {
        return !string.IsNullOrWhiteSpace(NewServerName);
    }

    [RelayCommand]
    private void RemoveServer(PluginServerConfigViewModel? server)
    {
        if (server is null)
        {
            return;
        }

        Servers.Remove(server);
    }

    /// <summary>
    /// Build the JsonObject this plugin entry will become on disk:
    /// <c>{mcpServers: {…}, …extraFields…}</c>. Returns <see langword="null"/>
    /// when the PluginId is blank.
    /// </summary>
    internal JsonObject? BuildOnDiskObject()
    {
        if (string.IsNullOrWhiteSpace(PluginId))
        {
            return null;
        }

        JsonObject mcpObj = new();
        foreach (PluginServerConfigViewModel server in Servers)
        {
            JsonObject? serverObj = server.BuildOnDiskObject();
            if (serverObj is null)
            {
                continue;
            }

            mcpObj[server.ServerName] = serverObj;
        }

        JsonObject pluginObj = new()
        {
            ["mcpServers"] = mcpObj,
        };

        // Replay opaque plugin-level extras (defensive — schema currently
        // has no other fields here, but future versions may add some).
        foreach (KeyValuePair<string, JsonNode?> kv in ExtraFields)
        {
            if (kv.Key == "mcpServers")
            {
                continue;
            }

            pluginObj[kv.Key] = kv.Value?.DeepClone();
        }

        return pluginObj;
    }

    // -----------------------------------------------------------------------
    // Server change forwarding — same pattern as Configs forwarding inside
    // PluginServerConfigViewModel. Funnels every Servers change (collection
    // OR per-server Configs / ServerName / NewServerName) into a single
    // PropertyChanged(nameof(Servers)) so the parent editor's MarkModified
    // logic only needs to listen for one property name.
    // -----------------------------------------------------------------------
    private void OnServersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (PluginServerConfigViewModel s in e.NewItems)
            {
                s.PropertyChanged += OnServerChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (PluginServerConfigViewModel s in e.OldItems)
            {
                s.PropertyChanged -= OnServerChanged;
            }
        }

        OnPropertyChanged(nameof(Servers));
    }

    private void OnServerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PluginServerConfigViewModel.ServerName)
            or nameof(PluginServerConfigViewModel.Configs))
        {
            OnPropertyChanged(nameof(Servers));
        }
    }
}

/// <summary>
/// Editor for the <c>pluginConfigs</c> schema node — a 3-level nested
/// map of plugin → mcpServers → server → configKey-value pairs. Each
/// level surfaces its own Add / Remove affordances inside an Expander
/// hierarchy.
/// </summary>
/// <remarks>
/// <para>
/// Per-config-value type discriminator: the schema allows
/// <c>string | number | boolean | array&lt;string&gt;</c> for each
/// leaf. v1 of this editor surfaces only string-typed values; non-string
/// originals are preserved opaquely via <see cref="PluginServerConfigViewModel.ExtraConfigs"/>
/// so the round-trip is lossless even when the GUI doesn't render every
/// value type. Most plugin configs are API keys / URLs / option strings,
/// so the common case is fully editable; the rare numeric / boolean /
/// array case requires editing the on-disk JSON directly until v2.
/// </para>
/// <para>
/// Replaces the prior JsonRaw fallback for this property — closes the
/// "every Complex / Object-array property in the bundled schema has a
/// typed editor" goal from the 2026-05-05 audit.
/// </para>
/// </remarks>
public partial class PluginConfigsEditorViewModel : PropertyEditorViewModel
{
    private bool _isLoading;

    // Reset semantic consistency.  See McpServerListEditorViewModel
    // for rationale.
    private LayeredValue? _lastLayered;
    private ConfigScope _lastScope;

    public PluginConfigsEditorViewModel(SchemaNode schema, ConfigScope editingScope)
        : base(schema, editingScope)
    {
        Plugins = [];
        Plugins.CollectionChanged += OnPluginsCollectionChanged;
    }

    /// <inheritdoc/>
    protected override bool IsLoading => _isLoading;

    /// <summary>One row per plugin entry. Bound to the outer ItemsControl in the View.</summary>
    public ObservableCollection<PluginConfigEntryViewModel> Plugins { get; }

    /// <summary>Buffer for the new plugin-id "+ Add" affordance.</summary>
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddPluginCommand))]
    private string _newPluginId = string.Empty;

    [RelayCommand(CanExecute = nameof(CanAddPlugin))]
    private void AddPlugin()
    {
        string id = NewPluginId.Trim();
        if (id.Length == 0)
        {
            return;
        }

        PluginConfigEntryViewModel? existing = Plugins.FirstOrDefault(p => p.PluginId == id);
        if (existing is not null)
        {
            return; // no-op on duplicate
        }

        Plugins.Add(new PluginConfigEntryViewModel(id));
        NewPluginId = string.Empty;
    }

    private bool CanAddPlugin()
    {
        return !string.IsNullOrWhiteSpace(NewPluginId);
    }

    [RelayCommand]
    private void RemovePlugin(PluginConfigEntryViewModel? plugin)
    {
        if (plugin is null)
        {
            return;
        }

        Plugins.Remove(plugin);
    }

    // -----------------------------------------------------------------------
    // Workspace round-trip
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public override JsonNode? ToJsonValue()
    {
        if (Plugins.Count == 0)
        {
            return null;
        }

        JsonObject obj = new();
        foreach (PluginConfigEntryViewModel plugin in Plugins)
        {
            JsonObject? built = plugin.BuildOnDiskObject();
            if (built is null)
            {
                continue;
            }

            obj[plugin.PluginId.Trim()] = built;
        }

        return obj.Count > 0 ? obj : null;
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
            Plugins.Clear();

            JsonNode? scopeValue = layered.GetValueAt(editingScope);
            if (scopeValue is JsonObject pluginsObj)
            {
                foreach (KeyValuePair<string, JsonNode?> pluginKv in pluginsObj)
                {
                    PluginConfigEntryViewModel? entry = HydratePlugin(pluginKv.Key, pluginKv.Value);
                    if (entry is not null)
                    {
                        Plugins.Add(entry);
                    }
                }

                IsModified = pluginsObj.Count > 0;
            }
            else
            {
                // Bare string / array / null — stale or hand-corrupted on
                // disk. Start empty; user can save to replace, or reset.
                IsModified = false;
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static PluginConfigEntryViewModel? HydratePlugin(string pluginId, JsonNode? pluginNode)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return null;
        }

        PluginConfigEntryViewModel entry = new(pluginId);

        // Tolerate a non-object plugin value by surfacing an empty entry.
        if (pluginNode is not JsonObject pluginObj)
        {
            return entry;
        }

        foreach (KeyValuePair<string, JsonNode?> pluginField in pluginObj)
        {
            if (pluginField.Key == "mcpServers")
            {
                if (pluginField.Value is JsonObject serversObj)
                {
                    foreach (KeyValuePair<string, JsonNode?> serverKv in serversObj)
                    {
                        PluginServerConfigViewModel? server = HydrateServer(serverKv.Key, serverKv.Value);
                        if (server is not null)
                        {
                            entry.Servers.Add(server);
                        }
                    }
                }

                continue;
            }

            // Defensive opaque preservation for any future plugin-level field.
            entry.ExtraFields[pluginField.Key] = pluginField.Value?.DeepClone();
        }

        return entry;
    }

    private static PluginServerConfigViewModel? HydrateServer(string serverName, JsonNode? serverNode)
    {
        if (string.IsNullOrWhiteSpace(serverName))
        {
            return null;
        }

        PluginServerConfigViewModel server = new(serverName);
        if (serverNode is not JsonObject configsObj)
        {
            return server;
        }

        foreach (KeyValuePair<string, JsonNode?> configKv in configsObj)
        {
            // Surface string-typed configs. Non-string types
            // (number / boolean / array) preserve opaquely so the
            // round-trip is lossless.
            if (configKv.Value is JsonValue jv && jv.TryGetValue(out string? s))
            {
                server.Configs.Add(new StringMapEntryViewModel(configKv.Key, s));
            }
            else
            {
                server.ExtraConfigs[configKv.Key] = configKv.Value?.DeepClone();
            }
        }

        return server;
    }

    /// <inheritdoc/>
    protected override void OnResetToInherited()
    {
        // Reset semantic consistency: prefer reload from the
        // cached snapshot so unsaved edits revert to the at-load entries
        // rather than wiping everything.  See McpServerListEditorViewModel.
        if (_lastLayered is not null)
        {
            NewPluginId = string.Empty;
            LoadFromLayered(_lastLayered, _lastScope);
            return;
        }

        // Fallback path — no snapshot.  Suppress MarkModified during
        // teardown — see the matching pattern in
        // StringMapPropertyEditorViewModel / MarketplaceListEditorViewModel.
        _isLoading = true;
        try
        {
            foreach (PluginConfigEntryViewModel plugin in Plugins)
            {
                plugin.PropertyChanged -= OnPluginChanged;
            }

            Plugins.Clear();
            NewPluginId = string.Empty;
        }
        finally
        {
            _isLoading = false;
        }
    }

    // -----------------------------------------------------------------------
    // Change propagation — same shape as the other Modified-aggregating
    // compound editors. Plugin / Server / Config changes all funnel into
    // OnPluginChanged via the per-level PropertyChanged forwarding.
    // -----------------------------------------------------------------------
    private void OnPluginsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (PluginConfigEntryViewModel p in e.NewItems)
            {
                p.PropertyChanged += OnPluginChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (PluginConfigEntryViewModel p in e.OldItems)
            {
                p.PropertyChanged -= OnPluginChanged;
            }
        }

        MarkModified();
    }

    private void OnPluginChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PluginConfigEntryViewModel.PluginId)
            or nameof(PluginConfigEntryViewModel.Servers))
        {
            MarkModified();
        }
    }
}