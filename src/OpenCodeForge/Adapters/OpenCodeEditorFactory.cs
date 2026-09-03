using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Avalonia.Agents;
using Bennewitz.Ninja.OpenCode.Avalonia.Commands;
using Bennewitz.Ninja.OpenCode.Avalonia.Keybinds;
using Bennewitz.Ninja.OpenCode.Avalonia.Mcp;
using Bennewitz.Ninja.OpenCode.Avalonia.Permissions;
using Bennewitz.Ninja.OpenCode.Avalonia.Plugins;
using Bennewitz.Ninja.OpenCode.Avalonia.Themes;
using Bennewitz.Ninja.OpenCode.Avalonia.Tooling;
using Bennewitz.Ninja.OpenCode.Avalonia.Updates;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCode.Sdk.Themes;

namespace Bennewitz.Ninja.OpenCodeForge.Adapters;

/// <summary>
/// Builds this app's property editors: the editor library's generic dispatch, reached through the
/// shell's adapters.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately thin. <see cref="DefaultPropertyEditorFactory"/> already maps a schema's
/// value type to the right generic editor, and the shell's adapters already translate
/// <see cref="SchemaNode"/> / <see cref="ConfigScope"/> / <see cref="SettingsWorkspace"/> into the
/// library's interfaces. All that was missing between them was this translation of one call.
/// </para>
/// <para>
/// <b>Specialised editors are registered by schema property name.</b> The generic dispatch maps a
/// JSON <i>type</i> to an editor, which is the right default and wrong for any shape whose meaning
/// is not in its type. <c>permission</c> is the clearest case: an object of strings to the schema,
/// and an ordered last-match-wins rule list to OpenCode, so the generic object editor renders it
/// faithfully while hiding the only thing that decides the outcome.
/// </para>
/// <para>
/// ⚠ <b>Matching on the property name, not the full path, is deliberate — and it was load-bearing
/// for <c>agent{}</c>.</b> ✅ <b>Verified, not assumed:</b> <c>Config.permission</c> and
/// <c>AgentConfig.permission</c> are both a bare <c>$ref</c> to the same
/// <c>#/$defs/PermissionConfig</c>, so an agent's nested override wants exactly this editor —
/// which is why the agent editor hosts the permission grid as a child rather than reimplementing
/// it. <c>ActionOnlyToolsSchemaDriftTests.AgentPermissionAndGlobalPermission_AreTheSameDefinition</c>
/// keeps that true: if a schema refresh ever splits the two, it fails loudly instead of quietly
/// handing a nested node an editor built for the outer shape.
/// </para>
/// <para>
/// ⚠ <b>The other app's factory is NOT reused, and the duplication is one method wide.</b>
/// <c>DefaultEditorFactory</c> in ClaudeForge does the same job, but it constructs that app's
/// bridge editors — the ones its own source describes as not yet migrated to the library's
/// interface contract — and it carries a model-editor branch full of one vendor's model ids.
/// Reusing it would mean a product reference this repo's layering rules forbid. Consolidating the
/// two means finishing that migration, not moving this file.
/// </para>
/// </remarks>
public sealed class OpenCodeEditorFactory : ISchemaEditorFactory
{
    private readonly DefaultPropertyEditorFactory _generic = new();

    /// <param name="danger">
    /// The danger table for the document this factory serves, or <see langword="null"/> for none.
    /// </param>
    /// <remarks>
    /// ⚠ <b>Per DOCUMENT, not per product.</b> <c>opencode.json</c> and <c>tui.json</c> have
    /// separate tables (<c>plugin</c> is Critical in both, but almost nothing else lines up), so
    /// one factory instance shared across both sections would label one document with the other's
    /// policy. <c>MainWindowViewModel</c> therefore builds a factory per <c>HostedSection</c>
    /// rather than holding a single shared one.
    /// </remarks>
    public OpenCodeEditorFactory(IDangerClassifier? danger = null)
    {
        Danger = danger;
    }

    /// <inheritdoc />
    public IDangerClassifier? Danger { get; }

    /// <inheritdoc />
    /// <remarks>
    /// ⭐ Every arm's editor funnels through <see cref="PropertyEditorViewModel.AttachDangerClassifier"/>
    /// exactly once here. Setting the classifier inside each arm's object initializer would mean a
    /// thirteenth arm added later silently renders with no severity — which looks the same as a
    /// product that declares nothing dangerous. Guarded by
    /// <c>OpenCodeEditorDangerWiringTests</c>, which drives every top-level node of both bundled
    /// schemas through this method.
    /// </remarks>
    public PropertyEditorViewModel Create(
        SchemaNode schema,
        ConfigScope editingScope,
        Func<Task<string?>>? browseDialog = null,
        SettingsWorkspace? workspace = null)
    {
        return CreateCore(schema, editingScope, browseDialog, workspace)
            .AttachDangerClassifier(Danger);
    }

    private PropertyEditorViewModel CreateCore(
        SchemaNode schema,
        ConfigScope editingScope,
        Func<Task<string?>>? browseDialog,
        SettingsWorkspace? workspace)
    {
        ArgumentNullException.ThrowIfNull(schema);

        IEditorSchema adaptedSchema = new SchemaNodeAdapter(schema);
        IEditorScope adaptedScope = ConfigScopeAdapter.For(editingScope);

        if (string.Equals(schema.Name, "permission", StringComparison.Ordinal))
        {
            return new OpenCodePermissionEditorViewModel(adaptedSchema, adaptedScope);
        }

        if (string.Equals(schema.Name, "mcp", StringComparison.Ordinal))
        {
            return new OpenCodeMcpEditorViewModel(adaptedSchema, adaptedScope);
        }

        if (string.Equals(schema.Name, "agent", StringComparison.Ordinal))
        {
            return new OpenCodeAgentEditorViewModel(adaptedSchema, adaptedScope);
        }

        if (string.Equals(schema.Name, "command", StringComparison.Ordinal))
        {
            return new OpenCodeCommandEditorViewModel(adaptedSchema, adaptedScope);
        }

        // Both products declare the identical `plugin` anyOf, so this one registration covers the
        // config file and the TUI file alike.
        if (string.Equals(schema.Name, "plugin", StringComparison.Ordinal))
        {
            return new OpenCodePluginEditorViewModel(adaptedSchema, adaptedScope);
        }

        if (string.Equals(schema.Name, "plugin_enabled", StringComparison.Ordinal))
        {
            return new OpenCodePluginEnabledEditorViewModel(adaptedSchema, adaptedScope);
        }

        if (string.Equals(schema.Name, "formatter", StringComparison.Ordinal))
        {
            return new OpenCodeFormatterEditorViewModel(adaptedSchema, adaptedScope);
        }

        // ⚠ `lsp` is the one property name in this file that exists TWICE in the schema: here as a
        // map of language servers, and inside PermissionConfig's object arm as the permission rule
        // for the `lsp` tool. Matching by name is safe only because the permission grid owns its
        // whole subtree and never dispatches its own children back through this factory, so the
        // nested one cannot reach this branch. ✅ Verified, not assumed:
        // `OpenCodeToolingCodecTests.PermissionsInnerLsp_IsARuleConfig_NotAServerMap` pins the two
        // shapes apart, so a schema refresh that made them look alike fails loudly.
        if (string.Equals(schema.Name, "lsp", StringComparison.Ordinal))
        {
            return new OpenCodeLspEditorViewModel(adaptedSchema, adaptedScope);
        }

        // The only `boolean | scalar` union in either bundled schema — surveyed, not assumed — so
        // this branch generalises to nothing and is deliberately one property wide. Without it the
        // generic dispatch renders a three-value enum as a free-text box.
        if (string.Equals(schema.Name, "autoupdate", StringComparison.Ordinal))
        {
            return new OpenCodeAutoupdateEditorViewModel(adaptedSchema, adaptedScope);
        }

        // ⛔ The largest branch in this file, and the one the generic dispatch fails hardest. Spike
        // S6 measured it: all 184 keybind children classify as `Complex` with no children and no
        // enum, so every one of them lands on the raw-JSON fallback — 184 JSON text boxes, 86% of
        // the TUI schema's node tree and 99.1% of the file. The specialised editor is the only
        // reason that page is usable.
        if (string.Equals(schema.Name, "keybinds", StringComparison.Ordinal))
        {
            return new OpenCodeKeybindEditorViewModel(adaptedSchema, adaptedScope);
        }

        // ⭐ The one branch here that builds a LIBRARY editor rather than an OpenCode one, because
        // `EnumPropertyEditorViewModel` already is the free-form picker this field wants — it takes
        // its options, its may-I-type-anything flag and its per-option tooltips all from the schema.
        // So the only OpenCode-specific part is knowing what this machine has installed, and that
        // is a schema wrapper rather than a tenth editor. See OpenCodeThemeSchema.
        //
        // ⚠ Discovery happens per Create call, deliberately: the editors are rebuilt when the page
        // is shown, so a theme file added while the app is open appears without a restart. It is one
        // Directory.GetFiles on a directory that almost always holds nothing.
        if (string.Equals(schema.Name, "theme", StringComparison.Ordinal))
        {
            return new EnumPropertyEditorViewModel(
                new OpenCodeThemeSchema(
                    adaptedSchema,
                    OpenCodeThemeDiscovery.Discover(OpenCodeEnvironment.FromProcess())),
                adaptedScope);
        }

        IEditorWorkspace? adaptedWorkspace =
            workspace is null ? null : new SettingsWorkspaceAdapter(workspace);

        // Both callbacks get the same dialog: the caller supplies one picker, and which of file
        // or directory it opens is the host's decision, not the schema's.
        EditorContext context = browseDialog is null
            ? EditorContext.Empty
            : new EditorContext(BrowsePath: browseDialog, BrowseFile: browseDialog);

        return _generic.Create(adaptedSchema, adaptedWorkspace, adaptedScope, context);
    }
}
