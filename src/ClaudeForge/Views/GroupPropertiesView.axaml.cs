using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Bennewitz.Ninja.ClaudeForge.Controls;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Views;

/// <summary>Built-in "Properties" tab body — filter bar + virtualized editor list.</summary>
public partial class GroupPropertiesView : UserControl
{
    public GroupPropertiesView()
    {
        InitializeComponent();

        // Perf guard: after the first layout pass, log how many PropertyEditorWrapper rows
        // actually realized (and roughly how long since construction). This is the metric
        // that localized the ~4.4s Environment page — env's 306 declared vars were rendered
        // eagerly — so keeping it flags any regression of that class: a healthy page realizes
        // ~a viewport-full (top-level list virtualizes; large nested objects render collapsed),
        // so a count in the hundreds means something is eagerly building a big subtree again.
        // Cheap: one visual-tree walk over the (virtualized, so small) realized subtree per
        // Properties-page load, on a Background tick. Correlate with [App.Nav]/[Editor.Activate].
        long startTs = Stopwatch.GetTimestamp();
        Loaded += (_, _) => Dispatcher.UIThread.Post(
            () =>
            {
                int realized = this.GetVisualDescendants().OfType<PropertyEditorWrapper>().Count();
                string group = (DataContext as SettingsGroupEditorViewModel)?.GroupName ?? "?";
                Log.Information(
                    "[PropView.Realized] group={Group} wrappers={Count} sinceCtorMs={Ms:F0}",
                    group, realized, Stopwatch.GetElapsedTime(startTs).TotalMilliseconds);
            },
            DispatcherPriority.Background);
    }
}
