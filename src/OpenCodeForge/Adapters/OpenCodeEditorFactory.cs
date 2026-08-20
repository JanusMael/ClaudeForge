using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

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
/// ⚠ <b>No specialised editors yet, and that is a real limitation rather than a design choice.</b>
/// Every complex shape — most importantly the <c>permission</c> map, whose ordering carries
/// meaning — renders through the generic object editor. That is honest but not good: the
/// permission grid is Phase 9's work, and it registers here.
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

    /// <inheritdoc />
    public PropertyEditorViewModel Create(
        SchemaNode schema,
        ConfigScope editingScope,
        Func<Task<string?>>? browseDialog = null,
        SettingsWorkspace? workspace = null)
    {
        ArgumentNullException.ThrowIfNull(schema);

        IEditorSchema adaptedSchema = new SchemaNodeAdapter(schema);
        IEditorScope adaptedScope = ConfigScopeAdapter.For(editingScope);
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
