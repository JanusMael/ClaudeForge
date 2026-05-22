using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>
/// App bridge class: extends the reusable <see cref="LayeredEditors.Avalonia.ViewModels.PropertyEditorViewModel"/>
/// with the Claude-specific <c>JsonNode / LayeredValue / ConfigScope</c> API used by
/// specialized editors (Hooks, MCP servers, Permissions) that haven't yet been migrated
/// to the library's interface contract.
/// </summary>
/// <remarks>
/// Generic leaf editors (Boolean, String, Path, …) do NOT extend this class —
/// they extend <see cref="LayeredEditors.Avalonia.ViewModels.PropertyEditorViewModel"/>
/// directly from the library. App shims for those editors merely add backward-compat
/// constructor overloads.
/// </remarks>
public abstract class PropertyEditorViewModel
    : LayeredEditors.Avalonia.ViewModels.PropertyEditorViewModel
{
    protected PropertyEditorViewModel(SchemaNode schema, ConfigScope editingScope)
        : base(new ClaudeSchemaAdapter(schema), ClaudeScope.For(editingScope))
    {
        Schema = schema;
    }

    // ── Backward-compat properties ─────────────────────────────────────────────
    //
    //  OtherScopesWithData was previously declared here as
    // IReadOnlyList<ConfigScope>.  It moved up to the library typed as
    // IReadOnlyList<IEditorScope>, populated by SetScopeState below via
    // ClaudeScope.For(...).  AXAML bindings continue to render scope chiclets
    // because the App-side scope converters (ScopeToBrush, ScopeToTooltip,
    // ScopeToDisplayName) all accept either IEditorScope or ConfigScope.

    /// <summary>The original <see cref="SchemaNode"/>; hides the library's <c>IEditorSchema Schema</c>.</summary>
    public new SchemaNode Schema { get; }

    /// <summary>Dot-separated JSON path — mirrors the library's <c>Path</c> property.</summary>
    public string JsonPath => Path;

    /// <summary>
    /// Legacy name for <see cref="LayeredEditors.Avalonia.ViewModels.PropertyEditorViewModel.IsLocked"/>.
    /// Kept so that existing AXAML bindings (PropertyEditorWrapper) continue to work.
    /// </summary>
    public bool IsManagedLocked => IsLocked;

    // ── Claude-specific interface (virtual passthroughs) ───────────────────────
    //
    // Phase 2.1 step 2: these were abstract in the original bridge contract.
    // They become virtual with library-delegating defaults so:
    //
    //   • Existing App-side leaves (Boolean, String, Number, Enum, Path,
    //     StringArray) keep working unchanged — they override ToJsonValue
    //     and LoadFromLayered today, and overrides win over the new defaults.
    //
    //   • Future migrated leaves (steps 3–6 of the deferred plan) will instead
    //     override the library's ToValue() / LoadFromValue() directly. The
    //     defaults below let App-side callers (SettingsGroupEditorViewModel,
    //     ObjectPropertyEditorViewModel) keep calling ToJsonValue() /
    //     LoadFromLayered() during the per-leaf migration without forcing a
    //     big-bang rewrite of every consumer in the same commit.
    //
    // The cycle: bridge.ToValue() forwards to bridge.ToJsonValue(); bridge's
    // new ToJsonValue() default forwards to library.ToValue(). If a leaf
    // overrides NEITHER, the two defaults call each other infinitely. The
    // ThreadStatic recursion guards below catch that misconfiguration with
    // a clear error rather than a StackOverflowException.

    [ThreadStatic] private static int _toJsonValueDepth;
    [ThreadStatic] private static int _loadFromLayeredDepth;

    /// <summary>
    /// Serialize editor state to a <see cref="JsonNode"/> for saving.
    /// Default implementation delegates to <c>ToValue()</c> (library) and
    /// converts the resulting currency to JSON via <see cref="JsonCurrency.ToJsonNode"/>.
    /// </summary>
    /// <remarks>
    /// Override either this method (App-side, the legacy pattern still used
    /// by all six leaves today) OR override the library's <c>ToValue()</c>
    /// (the post-migration target).  Overriding NEITHER triggers the
    /// recursion guard with a diagnostic message — see the comment above.
    /// </remarks>
    public virtual JsonNode? ToJsonValue()
    {
        if (_toJsonValueDepth > 0)
        {
            throw new InvalidOperationException(
                $"Leaf editor '{GetType().Name}' overrides neither ToJsonValue (App-bridge) "
                + "nor ToValue (library); one of the two MUST be overridden to break the "
                + "default-implementation cycle through the App-bridge. See "
                + "Bennewitz.Ninja.ClaudeForge.ViewModels.Editors.PropertyEditorViewModel for the contract.");
        }

        _toJsonValueDepth++;
        try
        {
            return JsonCurrency.ToJsonNode(ToValue());
        }
        finally
        {
            _toJsonValueDepth--;
        }
    }

    /// <summary>
    /// Load editor state from the layered settings at the given scope.
    /// Default implementation wraps <paramref name="layered"/> in a
    /// <see cref="ClaudeValueAdapter"/> and calls the library's
    /// <c>LoadFromValue(IEditorValue, IEditorScope)</c>.
    /// </summary>
    /// <remarks>
    /// Same override-one-of-two rule as <see cref="ToJsonValue"/>.  A leaf
    /// that overrides only the library's <c>LoadFromValue</c> will hit
    /// this default when an App-side caller passes a
    /// <see cref="LayeredValue"/>.
    /// </remarks>
    public virtual void LoadFromLayered(LayeredValue layered, ConfigScope editingScope)
    {
        if (_loadFromLayeredDepth > 0)
        {
            throw new InvalidOperationException(
                $"Leaf editor '{GetType().Name}' overrides neither LoadFromLayered (App-bridge) "
                + "nor LoadFromValue (library); one of the two MUST be overridden to break the "
                + "default-implementation cycle through the App-bridge.");
        }

        _loadFromLayeredDepth++;
        try
        {
            LoadFromValue(new ClaudeValueAdapter(layered), ClaudeScope.For(editingScope));
        }
        finally
        {
            _loadFromLayeredDepth--;
        }
    }

    // ── IsModified change-tracking helper (shared by compound editors) ─────────

    /// <summary>
    /// Indicates whether <see cref="LoadFromLayered"/> is currently populating editor
    /// state from disk. While <c>true</c>, <see cref="MarkModified"/> is a no-op so
    /// the bulk-load burst (collection-changed events for each loaded entry) does
    /// not flag the editor as user-modified.
    /// </summary>
    /// <remarks>
    /// Default implementation returns <c>false</c> — appropriate for editors that
    /// only wire their entry-level subscriptions at the END of LoadFromLayered (e.g.
    /// HooksEditorViewModel) and therefore see no spurious mid-load events. Editors
    /// that subscribe in their constructor (Permissions, McpServers, Marketplaces,
    /// EnabledPlugins) override this to expose their private <c>_isLoading</c> field.
    /// </remarks>
    protected virtual bool IsLoading => false;

    /// <summary>
    /// Marks the editor modified for any user-initiated change. Always sets
    /// <see cref="LayeredEditors.Avalonia.ViewModels.PropertyEditorViewModel.IsModified"/>
    /// to <c>true</c> for transitions, and explicitly raises <c>PropertyChanged(IsModified)</c>
    /// when the flag was already <c>true</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CommunityToolkit.Mvvm's <c>[ObservableProperty]</c>-generated setter elides
    /// equal assignments — without the explicit re-raise, a delete-after-load or
    /// edit-after-load mutation would not propagate through
    /// <c>SettingsGroupEditorViewModel.OnEditorPropertyChanged</c>, leaving the
    /// workspace unwritten and the Save button disabled.
    /// </para>
    /// <para>
    /// Suppressed during the <see cref="LoadFromLayered"/> bulk-load via the
    /// <see cref="IsLoading"/> hook so per-item collection-changed events do not
    /// flag the editor as user-modified.
    /// </para>
    /// <para>
    /// This was previously a private method copy-pasted into 5 compound editors
    /// (McpServers, Permissions, Hooks, Marketplaces, EnabledPlugins)
    /// </para>
    /// </remarks>
    protected void MarkModified()
    {
        if (IsLoading)
        {
            return;
        }

        if (IsModified)
        {
            OnPropertyChanged(nameof(IsModified));
        }
        else
        {
            IsModified = true;
        }
    }

    // ── Library interface — implemented via the old API ────────────────────────

    /// <inheritdoc/>
    public override object? ToValue()
    {
        return ClaudeValueAdapter.Normalise(ToJsonValue());
    }

    /// <inheritdoc/>
    public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
    {
        ConfigScope configScope = ClaudeScope.ToConfigScope(editingScope);
        LayeredValue layered = value is ClaudeValueAdapter adapter
            ? adapter.Inner
            : BuildFallbackLayeredValue(value, configScope);
        LoadFromLayered(layered, configScope);
        // Compute inherited-display after the concrete LoadFromLayered sets IsModified / value
        UpdateInheritedDisplay(value, editingScope);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Convenience method for App shim implementations of <see cref="LoadFromLayered"/>.
    /// Sets the standard scope-state properties in one call, including
    /// <see cref="OtherScopesWithData"/> from the layered entries.
    /// </summary>
    protected void SetScopeState(LayeredValue layered, ConfigScope editingScope)
    {
        EditingScope = ClaudeScope.For(editingScope);
        EffectiveScope = layered.EffectiveScope.HasValue
            ? ClaudeScope.For(layered.EffectiveScope.Value)
            : null;
        IsOverridden = layered.IsOverridden;

        // Show which OTHER scopes have an explicit value — used by PropertyEditorWrapper
        // to render scope-indicator chiclets next to the property name.
        // .Distinct() — `layered.Entries` may legitimately contain multiple entries at
        // the same scope (e.g. several ~/.claude/managed-settings.d/*.json drop-ins all
        // defining the same key, each producing their own ScopeEntry at Managed scope).
        // Without dedup the user sees the same coloured chiclet repeated in the
        // "Defined in scopes" row of every property header.
        //
        // Step 3a: the library's OtherScopesWithData is IReadOnlyList<IEditorScope>;
        // map each ConfigScope through ClaudeScope.For so the AXAML scope converters
        // (which accept either IEditorScope or ConfigScope) keep rendering correctly.
        OtherScopesWithData = layered.Entries
                                     .Where(e => e.Scope != editingScope && e.Value != null)
                                     .Select(e => e.Scope)
                                     .Distinct()
                                     .Select(scope => (IEditorScope)ClaudeScope.For(scope))
                                     .ToList();
    }

    /// <summary>
    /// Build a minimal <see cref="LayeredValue"/> from an <see cref="IEditorValue"/> that
    /// is NOT a <see cref="ClaudeValueAdapter"/> (e.g. a test fake).
    /// Only the defined entries are included.
    /// </summary>
    private static LayeredValue BuildFallbackLayeredValue(IEditorValue value, ConfigScope editingScope)
    {
        // Produce entries for the scopes that have an explicit value
        ConfigScope[] allScopes = Enum.GetValues<ConfigScope>();
        List<ScopeEntry> entries = new();
        foreach (ConfigScope scope in allScopes)
        {
            ClaudeScope libScope = ClaudeScope.For(scope);
            if (value.IsDefinedAt(libScope))
            {
                JsonNode? raw = ClaudeValueAdapter.Coerce(value.GetValueAt(libScope));
                entries.Add(new ScopeEntry(scope, raw, "/fake"));
            }
        }

        // Determine effective scope
        ConfigScope? effectiveScope = value.EffectiveScope is { } es
            ? ClaudeScope.ToConfigScope(es)
            : null;
        ScopeEntry? effectiveEntry = effectiveScope.HasValue
            ? entries.FirstOrDefault(e => e.Scope == effectiveScope.Value)
            : null;

        return new LayeredValue(value.Path, entries)
        {
            EffectiveValue = effectiveEntry?.Value,
            EffectiveScope = effectiveScope,
        };
    }
}