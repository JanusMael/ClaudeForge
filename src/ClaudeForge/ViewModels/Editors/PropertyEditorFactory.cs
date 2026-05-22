using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>
/// Backward-compatible static façade over a Claude-configured <see cref="CompositeEditorFactory"/>.
/// New code should take a <see cref="DefaultEditorFactory"/> instance (or
/// <see cref="CompositeEditorFactory"/>) via constructor injection instead.
/// </summary>
public static class PropertyEditorFactory
{
    private static readonly DefaultEditorFactory _default = ClaudeEditorFactoryConfig.CreateDefault();

    /// <inheritdoc cref="DefaultEditorFactory.Create"/>
    public static LibVm.PropertyEditorViewModel Create(
        SchemaNode schema,
        ConfigScope editingScope,
        Func<Task<string?>>? browseDialog = null,
        SettingsWorkspace? workspace = null)
    {
        return _default.Create(schema, editingScope, browseDialog, workspace);
    }

    /// <inheritdoc cref="DefaultEditorFactory.CreateForGroup"/>
    public static IReadOnlyList<LibVm.PropertyEditorViewModel> CreateForGroup(
        IReadOnlyList<SchemaNode> nodes,
        ConfigScope editingScope,
        Func<Task<string?>>? browseDialog = null,
        SettingsWorkspace? workspace = null)
    {
        return _default.CreateForGroup(nodes, editingScope, browseDialog, workspace);
    }
}