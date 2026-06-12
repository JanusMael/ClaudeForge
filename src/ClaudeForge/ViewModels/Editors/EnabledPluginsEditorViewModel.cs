using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Plugins;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

// Alias the SDK namespace so ConfigScope unambiguously refers to the Core type
// in this file. The SDK type is reached via Sdk.IClaudeConfigClient / ConfigScope.

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>Observable row for a single plugin toggle.</summary>
public partial class PluginEntry : ObservableObject
{
    public PluginEntry(string pluginRef, bool enabled)
    {
        _pluginRef = pluginRef;
        _enabled = enabled;
    }

    [ObservableProperty] private string _pluginRef;
    [ObservableProperty] private bool _enabled;

    /// <summary>
    /// The original on-disk value when it was NOT a plain boolean. The
    /// <c>enabledPlugins</c> schema (<c>additionalProperties</c>) permits an
    /// array-of-strings (enable specific components) in addition to a bool; the
    /// editor has no typed affordance for the array form, so it round-trips the
    /// original verbatim rather than coercing it to a bool — which silently
    /// destroyed the array before this fix. <see langword="null"/> when the
    /// value was a plain bool (the common case).
    /// <para>
    /// The preserved value ALWAYS wins on save — the bool checkbox cannot override
    /// it (the view disables the checkbox when <see cref="HasPreservedValue"/>). An
    /// earlier "explicit toggle discards the array" escape hatch was removed: the
    /// checkbox can't reveal that a value is an array, so a toggle there was a
    /// silent-data-loss trap (a single uncheck, or a net-zero toggle-and-back,
    /// destroyed the array). Changing an array value is done by editing the raw
    /// JSON, not via this checkbox.
    /// </para>
    /// </summary>
    public JsonNode? PreservedValue { get; internal set; }

    /// <summary>
    /// True when this entry carries a non-boolean <see cref="PreservedValue"/> (e.g.
    /// the schema's array-of-strings form). The view disables the enabled checkbox
    /// for such rows so a toggle cannot silently destroy the value.
    /// </summary>
    public bool HasPreservedValue => PreservedValue is not null;
}

/// <summary>
/// Editor for the "enabledPlugins" object — a dictionary of plugin-id@marketplace-id
/// → bool (or, per the schema's anyOf, array-of-strings) entries.
/// </summary>
/// <remarks>
/// when an <see cref="IClaudeConfigClient"/> is supplied via
/// the constructor, <see cref="LoadFromLayered"/> reads its initial entries
/// through the SDK's typed accessor (<c>client.Plugins.GetAt(scope)</c>)
/// instead of decoding the raw <see cref="JsonObject"/> stored in the
/// <see cref="LayeredValue"/>. This is the proof-of-pattern migration ahead
/// of the four remaining specialized editors. Writes still flow through the
/// legacy <see cref="ToJsonValue"/>/SettingsGroupEditorViewModel live-write
/// loop pending the <c>_selfWriting</c> plumbing migration.
/// </remarks>
public partial class EnabledPluginsEditorViewModel : PropertyEditorViewModel
{
    // SDK client for typed reads. Optional: when null, fall back to the
    // legacy JsonObject-based load path so unit-test fixtures and
    // non-migrated call sites continue to work unchanged.
    private readonly IClaudeConfigClient? _client;

    // Set while LoadFromLayered is running so CollectionChanged and PropertyChanged
    // handlers do not prematurely flip IsModified to true during the initial load.
    private bool _isLoading;

    // Stored so OnResetToInherited can restore the on-disk state instead of clearing
    // the user's plugin list. Same pattern as McpServersEditorViewModel,
    // PermissionsEditorViewModel, HooksEditorViewModel.
    private LayeredValue? _lastLayered;
    private ConfigScope _lastScope;

    // Reset-bug fix (mirrors HooksEditorViewModel._baselineHooksValue,
    // commit 6861748).  Captured at LoadFromLayered as a deep clone of the at-load
    // 'enabledPlugins' value, so OnResetToInherited can RESTORE the workspace to
    // baseline after the base class's IsModified=false setter triggered a
    // destructive RemoveValue through the live-write path.  Without this, the
    // SDK-backed read in LoadFromLayered (_client.Plugins.GetAt) sees the
    // post-removal empty state and the UI rebuilds with zero entries.
    private JsonNode? _baselineEnabledPluginsValue;

    public EnabledPluginsEditorViewModel(SchemaNode schema, ConfigScope editingScope)
        : this(schema, editingScope, client: null)
    {
    }

    public EnabledPluginsEditorViewModel(
        SchemaNode schema,
        ConfigScope editingScope,
        IClaudeConfigClient? client)
        : base(schema, editingScope)
    {
        _client = client;
        Plugins = [];
        Plugins.CollectionChanged += OnPluginsChanged;
    }

    public ObservableCollection<PluginEntry> Plugins { get; }

    [ObservableProperty] private string _newPluginRef = string.Empty;

    // -----------------------------------------------------------------------
    // Change tracking
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>Routes through the base <see cref="PropertyEditorViewModel.IsLoading"/>
    /// hook so the canonical <c>MarkModified</c> implementation
    /// suppresses spurious flagging during the <c>LoadFromLayered</c> bulk-load.</remarks>
    protected override bool IsLoading => _isLoading;

    private void OnPluginsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Subscribe new entries so Enabled checkbox toggles and PluginRef edits
        // mark the editor dirty immediately, not just collection add/remove.
        if (e.NewItems != null)
        {
            foreach (PluginEntry item in e.NewItems)
            {
                item.PropertyChanged += OnEntryChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (PluginEntry item in e.OldItems)
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
    private void AddPlugin()
    {
        string r = NewPluginRef.Trim();
        if (string.IsNullOrEmpty(r) || Plugins.Any(p => p.PluginRef == r))
        {
            return;
        }

        Plugins.Add(new PluginEntry(r, true));
        NewPluginRef = string.Empty;
    }

    [RelayCommand]
    private void RemovePlugin(PluginEntry entry)
    {
        Plugins.Remove(entry);
    }

    // -----------------------------------------------------------------------
    // Serialization
    // -----------------------------------------------------------------------

    public override JsonNode? ToJsonValue()
    {
        if (Plugins.Count == 0)
        {
            return null;
        }

        JsonObject obj = new();
        foreach (PluginEntry p in Plugins)
        {
            // A preserved non-bool value (e.g. the schema's array-of-strings form)
            // ALWAYS round-trips verbatim — the bool checkbox is disabled for such
            // rows, so it can never silently coerce the array away on save.
            obj[p.PluginRef] = p.PreservedValue is not null
                ? p.PreservedValue.DeepClone()
                : (JsonNode?)JsonValue.Create(p.Enabled);
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
            Plugins.Clear();

            // Capture the at-load baseline so OnResetToInherited can write it back
            // to the workspace before reloading.  Deep-clone so subsequent edits
            // don't mutate this snapshot in place.  Re-captured on every
            // LoadFromLayered call — including during reset — so the snapshot
            // tracks the most recent CLEAN state.
            _baselineEnabledPluginsValue = layered.GetValueAt(editingScope)?.DeepClone();

            // SDK-backed read path (4.3.6b). The SDK client and the
            // workspace that produced `layered` share state via
            // ClaudeCodeClient.FromExistingWorkspace, so reading through
            // the typed accessor returns the same values that the JsonObject
            // path would — but as strongly-typed EnabledPlugin records, with
            // the SDK applying the same null/missing-key handling rules
            // headless consumers see.
            if (_client is not null)
            {
                // Cast across the parallel ConfigScope enums (Core ↔ Sdk):
                // their numeric values match — see ConfigScope XML doc and
                // tests/ClaudeForge.Tests/Adapters/ClaudeValueAdapterTests.
                ConfigScope sdkScope = editingScope;
                IReadOnlyList<EnabledPlugin> snapshot = _client.Plugins.GetAt(sdkScope);
                foreach (EnabledPlugin plugin in snapshot)
                {
                    Plugins.Add(new PluginEntry(plugin.PluginRef, plugin.Enabled));
                }
            }
            else
            {
                // Legacy path — used by unit-test fixtures that construct
                // the editor without an SDK client and by any caller that
                // pre-dates 4.3.6b plumbing.
                JsonObject? scopeValue = layered.GetValueAt(editingScope) as JsonObject;
                if (scopeValue != null)
                {
                    foreach (KeyValuePair<string, JsonNode?> kv in scopeValue)
                    {
                        bool enabled = kv.Value is JsonValue jv && jv.TryGetValue(out bool b) && b;
                        Plugins.Add(new PluginEntry(kv.Key, enabled));
                    }
                }
            }

            // Reconcile against the raw scope JSON so non-bool plugin values survive.
            // The schema permits an array-of-strings value (enable specific components)
            // alongside the bool form. The SDK accessor surfaces such values (via
            // EnabledPlugin.Components), but the SDK-path loop above keeps only PluginRef +
            // Enabled, and the legacy bool-parse coerces a non-bool to false — so the editor
            // needs the raw scope JSON (captured as the baseline) to attach the verbatim
            // original as PreservedValue. ToJsonValue then re-emits it unchanged; without
            // this an array value would be dropped or rewritten to `false` on the next save
            // (which overwrites the whole block on disk). Also recovers a key the SDK-path
            // list might not contain. Such rows are marked enabled (a non-bool value means
            // active) and the view shows a checked, disabled checkbox.
            if (_baselineEnabledPluginsValue is JsonObject rawScope)
            {
                foreach ((string key, JsonNode? raw) in rawScope)
                {
                    if (raw is null || (raw is JsonValue rv && rv.TryGetValue(out bool _)))
                    {
                        continue; // plain bool (or null) — already handled by the load above.
                    }

                    PluginEntry? existing = Plugins.FirstOrDefault(p => p.PluginRef == key);
                    if (existing is null)
                    {
                        Plugins.Add(new PluginEntry(key, enabled: true) { PreservedValue = raw.DeepClone() });
                    }
                    else
                    {
                        existing.Enabled = true;
                        existing.PreservedValue = raw.DeepClone();
                    }
                }
            }

            // IsModified = true  when the scope has at least one plugin entry to persist.
            // IsModified = false when no value is set OR the value is an explicit empty {}.
            // An empty object carries no meaningful state and is semantically equivalent
            // to "not set" — treating it as modified would cause Save to attempt a no-op
            // write of {} to disk, and leaves first-launch users with a phantom "unsaved
            // changes" indicator after merely opening their config.
            IsModified = Plugins.Count > 0;
        }
        finally
        {
            _isLoading = false;
        }
    }

    protected override void OnResetToInherited()
    {
        // Reset-bug fix (mirrors HooksEditorViewModel + Permissions
        // + McpServers + Marketplaces reset-restore pattern).  When the SDK
        // client is wired, the base class's IsModified=false →
        // RemoveValue("enabledPlugins") sequence emptied the workspace before
        // this method ran; LoadFromLayered then read live state via
        // _client.Plugins.GetAt and rebuilt with zero entries.  Restore the
        // baseline first so the subsequent reload sees the at-load state.
        if (_client is not null && _lastLayered is not null)
        {
            if (_baselineEnabledPluginsValue is not null)
            {
                _client.SetValue("enabledPlugins", _baselineEnabledPluginsValue.DeepClone(), _lastScope);
            }
            else
            {
                _client.RemoveValue("enabledPlugins", _lastScope);
            }
        }

        // Reload from the last-persisted scope value so the user's pre-edit plugin list is
        // restored (undo unsaved additions/removals/toggles). Fall back to clearing if we have
        // no stored value (first-load reset before LoadFromLayered ran).
        if (_lastLayered != null)
        {
            LoadFromLayered(_lastLayered, _lastScope);
        }
        else
        {
            _isLoading = true;
            try
            {
                Plugins.Clear();
            }
            finally
            {
                _isLoading = false;
            }

            IsModified = false;
        }
    }
}