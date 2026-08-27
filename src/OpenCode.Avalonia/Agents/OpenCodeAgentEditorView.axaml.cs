using Avalonia.Controls;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Agents;

/// <summary>
/// Code-behind for the agent map.
/// </summary>
/// <remarks>
/// Empty beyond the generated initialisation. Every row action is a command on its own row
/// view-model, and the nested permission editor is resolved by an application DataTemplate — so a
/// handler here would signal that a binding reached for an ancestor and hit <c>IL2026</c>.
/// </remarks>
public partial class OpenCodeAgentEditorView : UserControl
{
    /// <summary>Creates the view.</summary>
    public OpenCodeAgentEditorView()
    {
        InitializeComponent();
    }
}
