using Avalonia.Controls;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tooling;

/// <summary>
/// Code-behind for the OpenCodeLspEditorView view.
/// </summary>
/// <remarks>
/// Empty beyond the generated initialisation: every row action is a command on its own row
/// view-model, so a handler here would signal that a binding reached for an ancestor and hit
/// <c>IL2026</c>.
/// </remarks>
public partial class OpenCodeLspEditorView : UserControl
{
    /// <summary>Creates the view.</summary>
    public OpenCodeLspEditorView()
    {
        InitializeComponent();
    }
}
