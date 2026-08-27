using Avalonia.Controls;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Mcp;

/// <summary>
/// Code-behind for the MCP server list.
/// </summary>
/// <remarks>
/// Empty beyond the generated initialisation: every row action is a command on its own row
/// view-model, so there is nothing for a handler to do. Keep it that way — a handler here would be
/// a sign a binding reached for an ancestor and hit <c>IL2026</c>.
/// </remarks>
public partial class OpenCodeMcpEditorView : UserControl
{
    /// <summary>Creates the view.</summary>
    public OpenCodeMcpEditorView()
    {
        InitializeComponent();
    }
}
