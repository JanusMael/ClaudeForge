using Avalonia.Controls;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Artifacts;

/// <summary>
/// Code-behind for the artifacts page.
/// </summary>
/// <remarks>
/// Empty beyond the generated initialisation. The page is read-only and every displayed sentence
/// is a property on a view-model, so a handler here would mean either a binding reached for an
/// ancestor — which trips <c>IL2026</c> and fails the Release trim publish — or that a decision
/// about what the page CLAIMS had leaked out of the layer where it can be tested headlessly.
/// </remarks>
public partial class OpenCodeArtifactsPageView : UserControl
{
    /// <summary>Creates the view.</summary>
    public OpenCodeArtifactsPageView()
    {
        InitializeComponent();
    }
}
