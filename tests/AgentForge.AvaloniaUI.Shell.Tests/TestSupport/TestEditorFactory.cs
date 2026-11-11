using Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Adapters;
using Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Settings;
using Bennewitz.Ninja.ScopedEditors.Abstractions;
using Bennewitz.Ninja.ScopedEditors.ViewModels;

namespace Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Tests.TestSupport;

/// <summary>
/// An <see cref="ISchemaEditorFactory"/> a host with no editors of its own would supply: the
/// library's generic <see cref="DefaultPropertyEditorFactory"/>, reached through the shell's own
/// adapters (plans/00008 step 4).
/// </summary>
/// <remarks>
/// ClaudeForge's factory adds product editors on top (permissions, hooks, the model picker, a raw
/// JSON fallback). None of that is the settings page's behaviour, which is what this project
/// tests; the page has to work with whatever editors a host's factory returns.
/// </remarks>
internal sealed class TestEditorFactory : ISchemaEditorFactory
{
    private readonly DefaultPropertyEditorFactory _inner = new();

    public IDangerClassifier? Danger { get; init; }

    public PropertyEditorViewModel Create(
        SchemaNode schema,
        ConfigScope editingScope,
        Func<Task<string?>>? browseDialog = null,
        SettingsWorkspace? workspace = null)
    {
        IEditorWorkspace? editorWorkspace = workspace is null ? null : new SettingsWorkspaceAdapter(workspace);
        return _inner.Create(
            new SchemaNodeAdapter(schema),
            editorWorkspace!,
            ConfigScopeAdapter.For(editingScope),
            new EditorContext(browseDialog, null));
    }
}
