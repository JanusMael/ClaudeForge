using Avalonia.Controls;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Essentials;

/// <summary>
/// Code-behind for the Essentials page.
/// </summary>
/// <remarks>
/// Empty beyond the generated initialisation, for the same reason as the artifacts page: every
/// value, label and warning on this page is a property on a view-model, so a handler here would
/// mean a claim about the configuration had moved out of the layer where it can be tested
/// headlessly.
/// </remarks>
public partial class OpenCodeEssentialsView : UserControl
{
    /// <summary>Creates the view.</summary>
    public OpenCodeEssentialsView()
    {
        InitializeComponent();
    }
}
