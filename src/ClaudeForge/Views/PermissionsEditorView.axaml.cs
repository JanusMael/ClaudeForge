using Avalonia.Controls;

namespace Bennewitz.Ninja.ClaudeForge.Views;

/// <summary>
/// Permissions "Overview" body — explainer + inline education accordion + Default
/// Mode selector + an Advanced accordion. The rule-editing surfaces live on the
/// sibling tabs (PermissionsCommonView / BuildView / ListsView). Binding-driven;
/// no code-behind logic.
/// </summary>
public partial class PermissionsEditorView : UserControl
{
    public PermissionsEditorView()
    {
        InitializeComponent();
    }
}
