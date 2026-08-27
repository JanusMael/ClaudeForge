using Avalonia.Controls;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Permissions;

/// <summary>
/// Code-behind for the permission grid.
/// </summary>
/// <remarks>
/// Deliberately empty beyond the generated initialisation: every row action is a command on its
/// own row view-model, so there is nothing for a handler to do here. The sibling app needed
/// code-behind handlers precisely because its per-row buttons reached list-level commands through
/// ancestor bindings, which trip <c>IL2026</c>; putting the commands on the rows removes both the
/// binding and the handler.
/// </remarks>
public partial class OpenCodePermissionEditorView : UserControl
{
    /// <summary>Creates the view.</summary>
    public OpenCodePermissionEditorView()
    {
        InitializeComponent();
    }
}
