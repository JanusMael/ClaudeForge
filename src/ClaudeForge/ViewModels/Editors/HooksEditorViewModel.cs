using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HookCommandType = Bennewitz.Ninja.ClaudeForge.Sdk.Hooks.HookCommandType;
using HookEvent = Bennewitz.Ninja.ClaudeForge.Sdk.Hooks.HookEvent;

// Alias the SDK HookEvent so it's reachable without fully-qualifying. The
// editor's HookEntry now uses the SDK HookCommandType directly — the former
// editor-local duplicate enum was merged into the SDK.

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>
/// Editor for the "hooks" object.
/// Groups hooks by event type; within each group lists matcher+command entries.
/// </summary>
public partial class HooksEditorViewModel : PropertyEditorViewModel
{
    // SDK client for typed reads. Optional: when null, fall back to the
    // legacy JsonObject-based load path so unit-test fixtures continue to
    // work unchanged. Mirrors the EnabledPlugins / Marketplaces / McpServers
    // editor migrations.
    private readonly IClaudeConfigClient? _client;

    // The hook events the schema currently accepts, derived FRESH from the
    // schema node this editor was built with (its hooks.properties children).
    // Empty when the schema doesn't expose them (offline/minimal schema, or a
    // bare test node) — HookEventCatalog then falls back to its curated order.
    // Replaces the former hardcoded KnownEventTypes mirror: a schema refresh
    // that adds or removes an event now flows through with no code change.
    private readonly IReadOnlyList<string> _schemaEventNames;

    // Events shown proactively in the left rail: the schema set above ordered by
    // HookEventCatalog's curated overlay (or the curated order itself as fallback).
    private readonly IReadOnlyList<string> _orderedEvents;

    // Event name -> schema description (hooks.properties[name].description) for the
    // left-rail hover tooltip + the detail-pane label. Empty for schema nodes that
    // carry no descriptions (bare / offline).
    private readonly IReadOnlyDictionary<string, string> _eventDescriptions;

    // Type-picker infos threaded into every HookEventGroup → HookEntry so the Type
    // ComboBox shows the SCHEMA's per-type help text ($defs.hookCommand.anyOf[*].description)
    // instead of HookEntry's hardcoded fallback. Built once from the SDK's schema-derived
    // KnownCommandTypes; falls back to HookEntry.DefaultCommandTypeInfos per type when the
    // schema doesn't describe it (offline / no client). Replaces the former static mirror,
    // mirroring how _eventDescriptions replaced the hardcoded event descriptions.
    private readonly IReadOnlyList<HookCommandTypeInfo> _commandTypeInfos;

    // Maps the editor's three picker values to their schema variant `type` discriminators.
    // The schema also defines agent / mcp_tool variants, which the editor round-trips
    // opaquely and does not offer in the picker, so they have no entry here.
    private static readonly IReadOnlyDictionary<HookCommandType, string> PickerSchemaType =
        new Dictionary<HookCommandType, string>
        {
            [HookCommandType.Command] = "command",
            [HookCommandType.Prompt] = "prompt",
            [HookCommandType.Url] = "http",
        };

    public HooksEditorViewModel(SchemaNode schema, ConfigScope editingScope)
        : this(schema, editingScope, client: null)
    {
    }

    public HooksEditorViewModel(
        SchemaNode schema,
        ConfigScope editingScope,
        IClaudeConfigClient? client)
        : base(schema, editingScope)
    {
        _client = client;
        EventGroups = [];

        // Schema-driven event vocabulary — names AND descriptions. Prefer the SDK
        // surface (client.Hooks.KnownEvents) when a client is present so the editor
        // and headless SDK consumers share ONE source; fall back to deriving from
        // the schema node this editor was built with (the same cached node the
        // client would read) when there is no client — e.g. unit-test fixtures.
        // _schemaEventNames stays sourced from the node for the unrecognized-event
        // notice (empty → forgiving).
        _schemaEventNames = schema.Properties.Select(p => p.Name).ToList();

        IReadOnlyList<HookEventInfo> known = _client is not null
            ? _client.Hooks.KnownEvents
            : HookEventCatalog.ResolveOrder(
                schema.Properties.Select(p => new HookEventInfo(p.Name, p.Description)).ToList());

        _orderedEvents = known.Select(e => e.Name).ToList();
        _eventDescriptions = known
            .Where(e => !string.IsNullOrWhiteSpace(e.Description))
            .GroupBy(e => e.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Description!, StringComparer.Ordinal);

        _commandTypeInfos = BuildCommandTypeInfos(_client);
    }

    /// <summary>
    /// Build the Type-picker infos: start from the offline default list (fixed order
    /// Command / Prompt / Url) and, when the SDK exposes the schema's command variants,
    /// replace each item's description with the schema's — so the picker shows the
    /// authoritative "Bash command hook" / "LLM prompt hook. See …" text and tracks a
    /// schema refresh with no code change. Falls back to the hardcoded description per item
    /// when the schema doesn't carry one (offline / no client). Mirrors how the event list
    /// prefers the SDK's <c>KnownEvents</c> and degrades to the curated fallback.
    /// </summary>
    private static IReadOnlyList<HookCommandTypeInfo> BuildCommandTypeInfos(IClaudeConfigClient? client)
    {
        IReadOnlyList<HookCommandVariantInfo> variants = client?.Hooks.KnownCommandTypes ?? [];
        if (variants.Count == 0)
        {
            return HookEntry.DefaultCommandTypeInfos;
        }

        Dictionary<string, string> descByType = variants
            .Where(v => !string.IsNullOrWhiteSpace(v.Description))
            .GroupBy(v => v.Type, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Description!, StringComparer.Ordinal);

        return HookEntry.DefaultCommandTypeInfos
            .Select(info =>
                PickerSchemaType.TryGetValue(info.Value, out string? schemaType)
                && descByType.TryGetValue(schemaType, out string? desc)
                    ? info with { Description = desc }
                    : info)
            .ToList();
    }

    public ObservableCollection<HookEventGroup> EventGroups { get; }

    [ObservableProperty] private HookEventGroup? _selectedGroup;

    /// <summary>
    /// Non-null when the loaded config contains hook events the current schema no
    /// longer recognises (deprecated or misspelled). They are kept as-is and still
    /// saved; this banner tells the user why they look "unofficial". Complements
    /// the save-time friendly message in <c>SchemaErrorMessages</c>.
    /// </summary>
    [ObservableProperty] private string? _unrecognizedEventsNotice;

    // ── Flow-tab bridge ─────────────────────────────────────────────────────
    // The Hooks group contributes a "Flow" tab (an SVG diagram) via
    // ClaudeGroupTabCustomizer, which wires EnableFlowTab with a callback that
    // selects that tab on the parent SettingsGroupEditorViewModel. This lets the
    // Properties-tab "View flow diagram" hyperlink (bound to OpenFlowCommand)
    // deep-link to the Flow tab without this editor holding a reference to the
    // group VM.

    /// <summary>
    /// True once the parent group wired a Flow tab (see <see cref="EnableFlowTab"/>).
    /// Gates the Properties-tab hyperlink's visibility.
    /// </summary>
    [ObservableProperty] private bool _hasFlowTab;

    private Action? _showFlow;

    /// <summary>
    /// Wired by <see cref="ClaudeGroupTabCustomizer"/> when it contributes the Flow
    /// tab; <paramref name="showFlow"/> navigates to it. Enables the Properties link.
    /// </summary>
    internal void EnableFlowTab(Action showFlow)
    {
        _showFlow = showFlow;
        HasFlowTab = true;

        // Surface the "View flow diagram" link in the property-name header row via the
        // generic HeaderAction slot, so it sits right of the "Hooks" label (rendered by
        // PropertyEditorWrapper) rather than inside this editor body.
        HeaderActionText = Strings.LinkHookFlowDiagram;
        HeaderActionCommand = OpenFlowCommand;
    }

    /// <summary>Navigate to the group's Flow tab. Bound to the Properties-tab hyperlink.</summary>
    [RelayCommand]
    private void OpenFlow() => _showFlow?.Invoke();


    public override JsonNode? ToJsonValue()
    {
        JsonObject obj = new();
        foreach (HookEventGroup group in EventGroups.Where(g => g.Hooks.Count > 0))
        {
            // Only emit groups that have at least one hook with a non-empty command value.
            // An entry with an empty CommandValue would produce {"type":"command"} with no
            // command field, which Claude Code would reject at runtime.
            JsonArray json = group.ToJson();
            if (json.Count > 0)
            {
                obj[group.EventName] = json;
            }
        }

        return obj.Count > 0 ? obj : null;
    }

    // Stored so OnResetToInherited can restore the saved state instead of clearing.
    private LayeredValue? _lastLayered;
    private ConfigScope _lastScope;

    // Reset-bug fix.  Captured at LoadFromLayered as a deep clone of
    // the at-load 'hooks' value, so OnResetToInherited can RESTORE the workspace
    // to baseline after the base class's IsModified=false setter triggered a
    // destructive RemoveValue("hooks") through the live-write path.  Without
    // this, the SDK-backed read in LoadFromLayered (_client.Hooks.EventsAt) sees
    // the post-removal empty state and the UI rebuilds with zero hooks.
    private JsonNode? _baselineHooksValue;

    // MarkModified comes from the base class.
    //
    // This editor does not override IsLoading: the base default (always false) is
    // correct here because the Hook entry-subscription handlers are wired up at
    // the END of LoadFromLayered, after the load-time IsModified assignment, so no
    // spurious mark-modified events occur during load. The other compound editors
    // (Permissions, McpServers, Marketplaces, EnabledPlugins) subscribe earlier
    // and override IsLoading to expose their _isLoading field.

    // -------------------------------------------------------------------------
    // Subscription bookkeeping for child entries + nested
    // collections.  Mirrors the McpServersEditorViewModel pattern (see
    // editors AGENTS.md §4) so editing existing-row Headers, removing a
    // header via × button, etc., correctly fire MarkModified.  Prior shape
    // only subscribed entry.PropertyChanged via a per-entry lambda tracked
    // in a list — sufficient when HookEntry was a leaf, but added the Headers and AllowedEnvVars ObservableCollections
    // and per-row HookHeaderEntry items.  Without these subscriptions:
    //   - editing an existing header's Key or Value cell: silent (no MarkModified)
    //   - removing a header via × button: silent (no MarkModified)
    //   - removing an env-var via × button: silent (no MarkModified)
    // The Save button stayed in its current state on those mutations; on a
    // fresh load with IsModified=false, Save would never enable for those
    // edit kinds.
    //
    // Named methods (not captured lambdas) so subscribe/unsubscribe is
    // symmetric — the McpServers editor uses the same convention.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Subscribe to a <see cref="HookEntry"/>'s property changes plus its nested
    /// <see cref="HookEntry.Headers"/> / <see cref="HookEntry.AllowedEnvVars"/>
    /// collections so any user edit (top-level, collection mutation, or per-row
    /// item edit) marks the editor as modified.
    /// </summary>
    private void SubscribeEntry(HookEntry entry)
    {
        entry.PropertyChanged += OnEntryPropertyChanged;

        entry.Headers.CollectionChanged += OnNestedCollectionChanged;
        entry.AllowedEnvVars.CollectionChanged += OnNestedCollectionChanged;

        // The entry's nested collections may already contain items at the
        // moment we subscribe — IngestTypedFields populates Headers /
        // AllowedEnvVars BEFORE the entry is added to group.Hooks.  Hook
        // each existing HookHeaderEntry's PropertyChanged so inline edits to
        // already-loaded headers (Key / Value cells in the row) trigger
        // MarkModified too.  AllowedEnvVars holds plain strings, which have
        // no PropertyChanged, so only the CollectionChanged subscription is
        // needed for env-var add / remove.
        foreach (HookHeaderEntry hdr in entry.Headers)
        {
            hdr.PropertyChanged += OnNestedItemChanged;
        }
    }

    /// <summary>
    /// Mirror of <see cref="SubscribeEntry"/>.  Every Subscribe must be matched
    /// by an Unsubscribe — without this discipline, reload accumulates handlers
    /// and <c>MarkModified</c> fires N times per mutation.
    /// </summary>
    private void UnsubscribeEntry(HookEntry entry)
    {
        entry.PropertyChanged -= OnEntryPropertyChanged;

        entry.Headers.CollectionChanged -= OnNestedCollectionChanged;
        entry.AllowedEnvVars.CollectionChanged -= OnNestedCollectionChanged;

        foreach (HookHeaderEntry hdr in entry.Headers)
        {
            hdr.PropertyChanged -= OnNestedItemChanged;
        }
    }

    /// <summary>Unsubscribe every entry across every group — used by
    /// <see cref="LoadFromLayered"/> as part of its tear-down prologue.</summary>
    private void UnsubscribeAllEntries()
    {
        foreach (HookEventGroup group in EventGroups)
        foreach (HookEntry entry in group.Hooks)
        {
            UnsubscribeEntry(entry);
        }
    }

    /// <summary>
    /// Single handler subscribed to every <see cref="HookEntry.PropertyChanged"/>.
    /// Filters out transient input-placeholder fields (NewHeaderKey /
    /// NewHeaderValue / NewAllowedEnvVar) — those back textboxes whose value
    /// is consumed and cleared by AddHeader / AddAllowedEnvVar.  Typing into
    /// those boxes is not a "save-worthy" change to the hook's persisted
    /// state — it would otherwise flicker the Save button on every keystroke.
    /// See editors AGENTS.md §5 for the parity rule across all five compound
    /// editors.
    /// </summary>
    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HookEntry.NewHeaderKey)
            or nameof(HookEntry.NewHeaderValue)
            or nameof(HookEntry.NewAllowedEnvVar))
        {
            return;
        }

        // Skip derived/computed properties — they are notified as a side-effect
        // of changing the backing field (via [NotifyPropertyChangedFor]) and do
        // not represent independent user edits.  Without this guard MarkModified()
        // fires once per PropertyChanged notification, so editing Matcher (which
        // also notifies MatcherIsValid and HasValidationWarning) would log three
        // [Editor.UserEdit] entries instead of one.
        if (e.PropertyName is nameof(HookEntry.MatcherIsValid)
            or nameof(HookEntry.HasValidationWarning)
            or nameof(HookEntry.SelectedCommandTypeInfo)
            or nameof(HookEntry.ShowHttpFields))
        {
            return;
        }

        MarkModified();
    }

    /// <summary>
    /// Wires/unwires per-item PropertyChanged subscriptions as items enter or
    /// leave a nested collection (HookEntry.Headers / HookEntry.AllowedEnvVars),
    /// then calls MarkModified for the add/remove itself.  AllowedEnvVars items
    /// are strings (not <see cref="INotifyPropertyChanged"/>), so the per-item
    /// branch is a no-op for that collection — only the MarkModified part
    /// matters.  Headers items are <see cref="HookHeaderEntry"/> which DO
    /// implement PropertyChanged (via [ObservableProperty]).
    /// </summary>
    private void OnNestedCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (object? item in e.NewItems)
            {
                if (item is INotifyPropertyChanged inpc)
                {
                    inpc.PropertyChanged += OnNestedItemChanged;
                }
            }
        }

        if (e.OldItems != null)
        {
            foreach (object? item in e.OldItems)
            {
                if (item is INotifyPropertyChanged inpc)
                {
                    inpc.PropertyChanged -= OnNestedItemChanged;
                }
            }
        }

        MarkModified();
    }

    /// <summary>
    /// Per-item PropertyChanged handler — fires on Key or Value edits inside
    /// a <see cref="HookHeaderEntry"/> row.  Routes to MarkModified so the
    /// live-write picks up the latest cell content.
    /// </summary>
    private void OnNestedItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkModified();
    }

    /// <summary>
    /// Single handler subscribed to every <see cref="HookEventGroup.Hooks"/>
    /// CollectionChanged.  Routes through MarkModified rather than a bare
    /// IsModified=true assignment so the live-write path runs even when
    /// IsModified was already true from the prior load (delete-after-load
    /// contract).
    /// </summary>
    private void OnGroupHooksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MarkModified();

        if (e.NewItems != null)
        {
            foreach (HookEntry entry in e.NewItems)
            {
                SubscribeEntry(entry);
            }
        }

        if (e.OldItems != null)
        {
            foreach (HookEntry entry in e.OldItems)
            {
                UnsubscribeEntry(entry);
            }
        }
    }

    public override void LoadFromLayered(LayeredValue layered, ConfigScope editingScope)
    {
        _lastLayered = layered;
        _lastScope = editingScope;
        SetScopeState(layered, editingScope);

        // capture the user's prior selected event-group name
        // BEFORE we tear down EventGroups, so the post-rebuild assignment
        // can restore it. Without this, every workspace.Changed-driven
        // reload (which fires during the Save flow's ApplyToWorkspace
        // flush, before the changes dialog has even appeared) snaps the
        // user back to the first non-empty group — disorienting when
        // they're authoring multiple hooks for one event in a row.
        // See HooksEditor_PreservesSelectedGroup_AcrossReload test.
        string? priorSelectedEventName = SelectedGroup?.EventName;

        // Unsubscribe the previous collection-changed handler from all current groups
        // and all entry property-changed handlers before clearing.  UnsubscribeAllEntries
        // unwires each entry's PropertyChanged + nested-collection subscriptions; the
        // outer per-group handler is unwired separately because EventGroups is what we
        // iterate.
        foreach (HookEventGroup old in EventGroups)
        {
            old.Hooks.CollectionChanged -= OnGroupHooksChanged;
        }

        UnsubscribeAllEntries();

        EventGroups.Clear();
        SelectedGroup = null; // release reference to old group early

        JsonObject? scopeValue = layered.GetValueAt(editingScope) as JsonObject;

        // Capture the at-load baseline so OnResetToInherited can write it back
        // to the workspace before reloading.  Deep-clone so subsequent edits
        // don't mutate this snapshot in place.  Re-captured on every
        // LoadFromLayered call — including during reset — so the snapshot
        // tracks the most recent CLEAN state (after a save, after an external
        // reload, after a non-self-write workspace change).
        _baselineHooksValue = scopeValue?.DeepClone();

        // Event names actually present in the user's config at this scope,
        // collected during the build below and used to flag unrecognized /
        // deprecated events (present on disk but not in the current schema).
        List<string> configEventNames = [];

        if (_client is not null)
        {
            // SDK-backed read path. The accessor returns a
            // flat list of HookEvent records; we group them back by event name
            // to match the editor's HookEventGroup → HookEntry shape.
            //
            // Pre-create groups for every known event type (so unused events
            // still appear in the UI as empty rows the user can populate),
            // then populate from the SDK snapshot, then append any
            // SDK-discovered event names that weren't in KnownEventTypes.
            Dictionary<string, HookEventGroup> groupsByName = new(StringComparer.Ordinal);
            foreach (string name in _orderedEvents)
            {
                HookEventGroup g = new(name, DescriptionFor(name), _commandTypeInfos);
                groupsByName[name] = g;
                EventGroups.Add(g);
            }

            ConfigScope sdkScope = editingScope;
            IReadOnlyList<HookEvent> snapshot = _client.Hooks.EventsAt(sdkScope);
            foreach (HookEvent evt in snapshot)
            {
                configEventNames.Add(evt.EventName);
                if (!groupsByName.TryGetValue(evt.EventName, out HookEventGroup? group))
                {
                    group = new HookEventGroup(evt.EventName, DescriptionFor(evt.EventName), _commandTypeInfos);
                    groupsByName[evt.EventName] = group;
                    EventGroups.Add(group);
                }

                group.Hooks.Add(HookEntryFromSdk(evt));
            }
        }
        else
        {
            // Legacy path — used by unit-test fixtures that construct the
            // editor without an SDK client.
            // Build a group for every event the schema currently offers.
            foreach (string eventName in _orderedEvents)
            {
                JsonNode? node = scopeValue?[eventName];
                HookEventGroup group = node != null
                    ? HookEventGroup.FromJson(eventName, node, DescriptionFor(eventName), _commandTypeInfos)
                    : new HookEventGroup(eventName, DescriptionFor(eventName), _commandTypeInfos);
                EventGroups.Add(group);
            }

            // Also add any event types present in the file that we did not
            // pre-create above (unknown or deprecated) so nothing on disk is
            // hidden from the user.
            if (scopeValue != null)
            {
                foreach (KeyValuePair<string, JsonNode?> kv in scopeValue)
                {
                    configEventNames.Add(kv.Key);
                    if (!_orderedEvents.Contains(kv.Key))
                    {
                        EventGroups.Add(HookEventGroup.FromJson(kv.Key, kv.Value, DescriptionFor(kv.Key), _commandTypeInfos));
                    }
                }
            }
        }

        // Flag any events on disk the current schema no longer recognises
        // (deprecated or misspelled). They are preserved and still saved; the
        // banner just explains why they appear "unofficial". Skipped when the
        // schema set is unknown (empty) — we can't judge, so we don't warn.
        IReadOnlyList<string> unrecognized =
            HookEventCatalog.UnrecognizedEvents(configEventNames, _schemaEventNames);
        UnrecognizedEventsNotice = unrecognized.Count > 0
            ? string.Format(Strings.LabelHooksUnrecognizedEventsFmt, string.Join(", ", unrecognized))
            : null;

        // Subscribe to collection changes (add/remove of whole hooks) and to each
        // entry's property changes + nested Headers/AllowedEnvVars collections so
        // IsModified stays current for every kind of user edit.  The named-method
        // pattern mirrors McpServersEditorViewModel; see SubscribeEntry above for
        // the per-entry detail and the editors AGENTS.md §4 for the parity rule.
        foreach (HookEventGroup group in EventGroups)
        {
            group.Hooks.CollectionChanged += OnGroupHooksChanged;
            // Subscribe existing entries (loaded from disk).  SubscribeEntry also
            // hooks each entry's nested Headers/AllowedEnvVars collections plus
            // each already-present HookHeaderEntry.PropertyChanged.
            foreach (HookEntry entry in group.Hooks)
            {
                SubscribeEntry(entry);
            }
        }

        // IsModified follows the parity contract documented in the editors AGENTS.md
        // sidecar: true when the editing scope has any explicit value, false when
        // the scope key is absent entirely. Previously this used
        // `EventGroups.Any(g => g.Hooks.Count > 0)`, which incorrectly reported
        // `false` when the user intentionally stored an empty `"hooks": {}` —
        // Reset then had nothing to clear and the empty placeholder could not
        // be removed via the editor.
        IsModified = scopeValue != null;

        // Restore the user's prior selection if that event still exists in the
        // rebuilt EventGroups. Falls back to the historical "first non-empty"
        // pick on first-load (priorSelectedEventName == null) or if the prior
        // group has been removed (e.g. unknown event name dropped after a
        // schema-driven rebuild). See the prior-name capture at the top of
        // this method for the full rationale.
        SelectedGroup =
            (priorSelectedEventName is not null
                ? EventGroups.FirstOrDefault(g => g.EventName == priorSelectedEventName)
                : null)
            ?? EventGroups.FirstOrDefault(g => g.Hooks.Count > 0)
            ?? EventGroups.FirstOrDefault();

        // Compute which OTHER scopes also have hooks so the view can show badge indicators.
        // .Distinct() — same rationale as McpServersEditorViewModel: multiple managed
        // drop-in files at the same scope each produce their own ScopeEntry, and without
        // dedup the user sees the same scope chiclet repeated in the editor header.
        OtherScopesWithData = layered.Entries
                                     .Where(e => e.Scope != editingScope && e.Value is JsonObject jo && jo.Count > 0)
                                     .Select(e => e.Scope)
                                     .Distinct()
                                     .Select(scope => (IEditorScope)ClaudeScope.For(scope))
                                     .ToList();
    }

    protected override void OnResetToInherited()
    {
        // Reset-bug fix.  The base ResetToInherited() set
        // IsModified=false BEFORE calling this method, which fired the
        // SettingsGroupEditorViewModel.OnEditorPropertyChanged live-write
        // path with `value=null` — i.e. RemoveValue("hooks") on the
        // workspace.  We need to undo that destructive write so the
        // SDK-backed read in LoadFromLayered (_client.Hooks.EventsAt)
        // sees the at-load baseline state, not the just-removed empty
        // state.
        //
        // Restore via the same SDK SetValue/RemoveValue surface
        // SettingsGroupEditorViewModel uses, so the workspace's Changed
        // event fires once more — but the parent VM's _selfWriting guard
        // is back to false by the time we get here, so OnWorkspaceChanged
        // will fire.  That's fine: the rebuild is correct; we want the
        // post-restore state visible.
        if (_client is not null && _lastLayered is not null)
        {
            if (_baselineHooksValue is not null)
            {
                _client.SetValue("hooks", _baselineHooksValue.DeepClone(), _lastScope);
            }
            else
            {
                _client.RemoveValue("hooks", _lastScope);
            }
        }

        // Reload from the last-persisted scope value to restore the pre-edit state
        // (undo unsaved hook additions/removals) rather than clearing everything.
        if (_lastLayered != null)
        {
            LoadFromLayered(_lastLayered, _lastScope);
        }
        else
        {
            // Unwire the collection-changed handler from each group BEFORE clearing,
            // mirroring the prologue of LoadFromLayered. Otherwise group.Hooks.Clear()
            // fires OnGroupHooksChanged, which calls MarkModified() — and this editor
            // intentionally has no _isLoading guard (subscriptions are normally wired
            // only at the END of LoadFromLayered), so MarkModified() would run during
            // a Reset and contradict the IsModified=false assignment below.
            foreach (HookEventGroup group in EventGroups)
            {
                group.Hooks.CollectionChanged -= OnGroupHooksChanged;
            }

            UnsubscribeAllEntries();
            foreach (HookEventGroup group in EventGroups)
            {
                group.Hooks.Clear();
            }

            IsModified = false;
        }
    }

    /// <summary>
    /// Project a typed <see cref="HookEvent"/> record back into the
    /// editor's mutable <see cref="HookEntry"/> shape. The editor still owns
    /// binding-time concerns (matcher/value validation, observable
    /// PropertyChanged subscriptions) that the immutable SDK record does not.
    /// </summary>
    /// <summary>The schema description for an event, or <see langword="null"/> when the schema doesn't describe it.</summary>
    private string? DescriptionFor(string eventName) =>
        _eventDescriptions.TryGetValue(eventName, out string? d) ? d : null;

    private static HookEntry HookEntryFromSdk(HookEvent evt)
    {
        HookEntry entry = new()
        {
            Matcher = evt.Matcher,
            CommandType = evt.CommandType,
            CommandValue = evt.CommandValue,
        };

        // HookEntry now has typed UI properties for
        // timeout / headers / allowedEnvVars.  IngestTypedFields plumbs
        // them in directly; IngestPreservedFields handles only the
        // remaining unmodeled fields (async, statusMessage, model, etc.).
        // Replaces the Stop-B-era transitional re-bundle into _extraFields.
        entry.IngestTypedFields(evt.Timeout, evt.Headers, evt.AllowedEnvVars);
        entry.IngestPreservedFields(evt.PreservedFields);

        // When the SDK encountered an unknown type
        // discriminator at load (e.g. "agent" / "http" / future schema
        // additions), it captured the inner JsonObject verbatim into
        // OpaqueInnerJson.  Plumb it through to the editor's _opaqueJson
        // so HookEntry.ToJson emits the original on save — closing the
        // pre-fix data-loss bug where SDK-backed reload silently downcast
        // unknown types to Command and the type discriminator was lost
        // permanently on next save.
        entry.IngestOpaqueJson(evt.OpaqueInnerJson);
        return entry;
    }
}