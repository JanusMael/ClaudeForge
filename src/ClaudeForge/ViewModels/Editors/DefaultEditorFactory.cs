using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Services;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>
/// Generic dispatch factory — creates the standard <see cref="PropertyEditorViewModel"/>
/// for each <see cref="SchemaValueType"/> via a simple switch. Specialised editors
/// (Hooks, MCP servers, Permissions) are handled by name-based fallback for Complex nodes.
/// </summary>
/// <remarks>
/// Instantiate once and keep for the lifetime of a settings surface. Recursive calls
/// for Object child editors go through <see cref="Create"/> so that subclasses
/// (e.g. <see cref="CompositeEditorFactory"/>) can intercept them.
/// </remarks>
public class DefaultEditorFactory
{
    /// <summary>
    /// Optional sink notified whenever this factory falls back to the raw-JSON
    /// editor for a schema shape it cannot classify (the only "unsupported shape"
    /// path). Lets the host aggregate a single load-time notice. Null by default
    /// so tests and standalone callers are unaffected; set by NavigationTreeBuilder
    /// for the production build.
    /// </summary>
    public IUnsupportedShapeSink? UnsupportedShapeSink { get; set; }

    /// <summary>
    /// Build the raw-JSON fallback editor for a shape the factory cannot classify,
    /// tag it with the per-field "unsupported shape" notice, and report it to
    /// <see cref="UnsupportedShapeSink"/> (when set). The single origin of the
    /// unsupported-shape signal — every raw-fallback arm routes here.
    /// </summary>
    private JsonRawPropertyEditorViewModel RawFallback(SchemaNode schema, ConfigScope editingScope)
    {
        JsonRawPropertyEditorViewModel raw = new(schema, editingScope)
        {
            UnsupportedShapeNotice = UnsupportedShapeText.FieldTooltip,
        };
        UnsupportedShapeSink?.Report(schema.JsonPath, schema.Title ?? schema.Name);
        return raw;
    }

    /// <summary>
    /// Create an editor ViewModel for the given schema node.
    /// </summary>
    /// <param name="schema">The schema node describing the property.</param>
    /// <param name="editingScope">The scope the user is currently editing.</param>
    /// <param name="browseDialog">Optional file/directory browse callback for Path editors.</param>
    /// <param name="workspace">Workspace for object-type editors that need child value access.</param>
    public virtual LibVm.PropertyEditorViewModel Create(
        SchemaNode schema,
        ConfigScope editingScope,
        Func<Task<string?>>? browseDialog = null,
        SettingsWorkspace? workspace = null)
    {
        // Complex types without a registered specialised editor (Permissions /
        // Hooks / MCP / Marketplaces / EnabledPlugins are matched earlier in
        // CompositeEditorFactory.Create) get one of two structured fallbacks:
        //
        //   1. Known string→string maps (e.g. modelOverrides) -> typed
        //      key-value table editor with autocomplete on the key column.
        //   2. Truly unknown shapes -> raw JSON textarea with parse-validation.
        //
        // Both replace the prior fallback that routed every Complex property to
        // a String editor, which silently let the user write bare strings into
        // typed-object slots and only surfaced as a schema-banner error on the
        // next reload.
        if (schema.ValueType == SchemaValueType.Complex)
        {
            return CreateComplexFallback(schema, editingScope);
        }

        return schema.ValueType switch
        {
            // Phase 2.1 step 3b — Boolean migrated to the library type. The App-bridge
            // BooleanPropertyEditorViewModel shim was deleted; the library leaf works
            // through the IEditorSchema/IEditorScope adapters and is rendered by the
            // libvm:BooleanPropertyEditorViewModel data template in PropertyEditorWrapper.
            SchemaValueType.Boolean => new LibVm.BooleanPropertyEditorViewModel(
                new ClaudeSchemaAdapter(schema),
                ClaudeScope.For(editingScope)),
            // Phase 2.1 step 6 — Enum migrated to the library type.
            SchemaValueType.Enum => NewLibEnum(schema, editingScope),
            // Phase 2.1 step 6b — Path migrated to the library type.
            SchemaValueType.Path => NewLibPath(schema, editingScope, browseDialog),
            // Phase 2.1 step 4 — String migrated to the library type. App-bridge
            // StringPropertyEditorViewModel shim deleted; library leaf rendered by the
            // libvm:StringPropertyEditorViewModel data template.
            SchemaValueType.String => NewLibString(schema, editingScope),
            // Phase 2.1 step 5 — Number / Integer migrated to the library type.
            // App-bridge NumberPropertyEditorViewModel shim deleted; library leaf
            // rendered by the libvm:NumberPropertyEditorViewModel data template.
            SchemaValueType.Integer => NewLibNumber(schema, editingScope),
            SchemaValueType.Number => NewLibNumber(schema, editingScope),
            SchemaValueType.Array => CreateArrayEditor(schema, editingScope),
            SchemaValueType.Object => CreateObjectEditor(schema, editingScope, browseDialog, workspace),
            // Unknown ValueType (schema fetch failed, or a draft we don't yet
            // recognise) — same rationale as the Complex fallback above:
            // raw JSON is the only safe affordance.
            var _ => RawFallback(schema, editingScope),
        };
    }

    private static LibVm.StringPropertyEditorViewModel NewLibString(
        SchemaNode schema, ConfigScope editingScope)
    {
        return new LibVm.StringPropertyEditorViewModel(new ClaudeSchemaAdapter(schema), ClaudeScope.For(editingScope));
    }

    private static LibVm.NumberPropertyEditorViewModel NewLibNumber(
        SchemaNode schema, ConfigScope editingScope)
    {
        return new LibVm.NumberPropertyEditorViewModel(new ClaudeSchemaAdapter(schema), ClaudeScope.For(editingScope));
    }

    private static LibVm.EnumPropertyEditorViewModel NewLibEnum(
        SchemaNode schema, ConfigScope editingScope)
    {
        // `model` gets the shared rich picker — the same friendly, fuzzy-matched suggestion
        // list Essentials uses. The generic enum editor would surface only the raw hyphenated
        // ids from the schema's `examples`, which is what made the two pages look different.
        if (schema.Name == "model")
        {
            return new ModelPropertyEditorViewModel(
                new ClaudeSchemaAdapter(schema),
                ClaudeScope.For(editingScope),
                ModelSuggestionCatalog.Build());
        }

        return new LibVm.EnumPropertyEditorViewModel(new ClaudeSchemaAdapter(schema), ClaudeScope.For(editingScope));
    }

    private static LibVm.PathPropertyEditorViewModel NewLibPath(
        SchemaNode schema, ConfigScope editingScope,
        Func<Task<string?>>? browseDialog)
    {
        return new LibVm.PathPropertyEditorViewModel(new ClaudeSchemaAdapter(schema), ClaudeScope.For(editingScope),
            browseDialog);
    }

    /// <summary>
    /// Dispatch for Complex schema nodes (Object with no declared properties /
    /// anyOf with multiple non-null variants). Per-property knowledge is
    /// concentrated here so the editor types stay generic.
    /// </summary>
    private PropertyEditorViewModel CreateComplexFallback(
        SchemaNode schema, ConfigScope editingScope)
    {
        return schema.Name switch
        {
            // Map of Anthropic-model-id → provider-specific override string.
            // Key column reuses the same suggestion list as the standalone
            // `model` editor so the user picks from familiar IDs and types a
            // plain string for the override (no JSON syntax needed).
            "modelOverrides" => new StringMapPropertyEditorViewModel(
                schema, editingScope, ModelIdKeySuggestions),
            // 3-level nested per-plugin / per-server config map. Closes the
            // "every Complex / Object-array property has a typed editor" goal
            // from the 2026-05-05 audit. String-typed config values are
            // surfaced directly; non-string originals (number / boolean /
            // array) preserve opaquely via per-server ExtraConfigs.
            "pluginConfigs" => new PluginConfigsEditorViewModel(schema, editingScope),
            var _ => RawFallback(schema, editingScope),
        };
    }

    /// <summary>
    /// The same model-id list the standalone <c>model</c> editor offers as
    /// free-form-enum suggestions. Hand-curated here so the
    /// <c>modelOverrides</c> key column matches without leaking schema-
    /// traversal logic into the StringMap editor.
    /// </summary>
    private static readonly IReadOnlyList<string> ModelIdKeySuggestions =
    [
        "sonnet", "opus", "haiku", "claude-sonnet-4-5", "claude-opus-4-5",
    ];

    /// <summary>
    /// Create editors for all nodes in a group.
    /// </summary>
    public IReadOnlyList<LibVm.PropertyEditorViewModel> CreateForGroup(
        IReadOnlyList<SchemaNode> nodes,
        ConfigScope editingScope,
        Func<Task<string?>>? browseDialog = null,
        SettingsWorkspace? workspace = null)
    {
        List<LibVm.PropertyEditorViewModel> editors = new(nodes.Count);
        foreach (SchemaNode node in nodes)
        {
            editors.Add(Create(node, editingScope, browseDialog, workspace));
        }

        return editors;
    }

    // ── Private dispatch helpers ───────────────────────────────────────────────

    /// <summary>
    /// Dispatch for Array schema nodes.
    /// <list type="bullet">
    ///   <item>String / Path / Unknown items → <see cref="StringArrayPropertyEditorViewModel"/>
    ///         (the simple list-of-strings editor).</item>
    ///   <item>Object / Complex / Array items → <see cref="JsonRawPropertyEditorViewModel"/>
    ///         (the parse-on-keystroke fallback) UNLESS a property-name match (below)
    ///         routes to a typed list editor. Routing arrays of structured objects to
    ///         the StringArray editor would let the user type bare strings into slots
    ///         the schema requires to be objects — same silent-corruption mechanic
    ///         that the Complex fallback fix addressed; this is the parallel safety
    ///         net for the Array dispatch path.</item>
    /// </list>
    /// </summary>
    private LibVm.PropertyEditorViewModel CreateArrayEditor(SchemaNode schema, ConfigScope editingScope)
    {
        SchemaValueType itemType = schema.ItemsSchema?.ValueType ?? SchemaValueType.String;
        if (itemType is SchemaValueType.String or SchemaValueType.Path or SchemaValueType.Unknown)
            // Phase 2.1 step 6c — StringArray migrated to the library type.
        {
            return NewLibStringArray(schema, editingScope);
        }

        // Array of structured items — every per-property typed editor goes through
        // CreateArrayObjectFallback; unmatched property names get JsonRaw so the user
        // sees + edits the raw JSON rather than silently corrupting a typed slot.
        return CreateArrayObjectFallback(schema, editingScope);
    }

    private static LibVm.StringArrayPropertyEditorViewModel NewLibStringArray(
        SchemaNode schema, ConfigScope editingScope)
    {
        return new LibVm.StringArrayPropertyEditorViewModel(new ClaudeSchemaAdapter(schema),
            ClaudeScope.For(editingScope));
    }

    /// <summary>
    /// Per-property dispatch for arrays whose items are structured objects
    /// (<see cref="SchemaValueType.Object"/> / <see cref="SchemaValueType.Complex"/>
    /// / <see cref="SchemaValueType.Array"/>). Mirrors the Complex-fallback
    /// dispatcher above: per-property knowledge is concentrated here so the
    /// editor types stay generic.
    /// </summary>
    /// <remarks>
    /// Stage 2 wires the MCP allow/deny list editor (Name/Command/URL
    /// discriminated union). Stage 3 wires the marketplace allow/block
    /// list editor (8 source-discriminated variants). Anything else still
    /// falls to JsonRaw (the safety net).
    /// </remarks>
    private PropertyEditorViewModel CreateArrayObjectFallback(
        SchemaNode schema, ConfigScope editingScope)
    {
        return schema.Name switch
        {
            // Enterprise allowlist / denylist for MCP servers — items are
            // anyOf<{serverName} | {serverCommand} | {serverUrl}>. The typed
            // editor surfaces a per-row Variant picker + payload field so
            // the user can't write a bare string into the array.
            "allowedMcpServers" or "deniedMcpServers"
                => new McpServerListEditorViewModel(schema, editingScope),
            // Managed-settings marketplace allow/block lists — items are
            // anyOf<source-discriminated 8-variant union>. The typed editor
            // surfaces a per-row Source picker + primary-value field;
            // optional fields (ref / path / headers) are preserved opaquely
            // across the round-trip so hand-curated managed entries survive.
            "strictKnownMarketplaces" or "blockedMarketplaces"
                => new MarketplaceListEditorViewModel(schema, editingScope),
            var _ => RawFallback(schema, editingScope),
        };
    }

    /// <summary>
    /// Virtual so subclasses can intercept child-property creation.
    /// Calls <see cref="Create"/> for each child so subclass overrides propagate.
    /// </summary>
    protected virtual LibVm.PropertyEditorViewModel CreateObjectEditor(
        SchemaNode schema,
        ConfigScope editingScope,
        Func<Task<string?>>? browseDialog,
        SettingsWorkspace? workspace)
    {
        List<LibVm.PropertyEditorViewModel> children = new(schema.Properties.Count);
        foreach (SchemaNode child in schema.Properties)
        {
            children.Add(Create(child, editingScope, browseDialog, workspace));
        }

        return new ObjectPropertyEditorViewModel(schema, editingScope, children, workspace);
    }
}