using Avalonia.Controls;
using Avalonia.Interactivity;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Views;

public partial class WelcomeView : UserControl
{
    public WelcomeView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// explicit click handler for the "Show this page on
    /// launch" checkbox.  The XAML binding is intentionally OneWay
    /// (VM → View) only: a two-way binding from <c>CheckBox.IsChecked</c>
    /// (<c>bool?</c>) to the <c>bool</c> VM property fires a binding-init
    /// round-trip that lands <c>false</c> on the source the first time
    /// the WelcomeView renders, even without a user click.  That spurious
    /// write triggered <c>OnShowWelcomeOnLaunchChanged → SaveWindowState</c>
    /// and silently persisted opt-out for users who never touched the
    /// checkbox.  Routing the VM-write through this handler means the
    /// only path that flips the property is an actual user click.
    /// </summary>
    private void OnShowWelcomeOnLaunchClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && DataContext is MainWindowViewModel vm)
        {
            // CheckBox is two-state (not three-state) so IsChecked is
            // bool? but only ever null / true / false on user click —
            // treat null as false (defensive, shouldn't fire in practice).
            bool newValue = cb.IsChecked == true;
            // Audit-log every legitimate user click as a [Welcome.Command]
            // event — aligns with the [Memory.Command] / [Profiles.Command]
            // pattern used by other VMs and gives a permanent trail of
            // opt-in / opt-out toggles.  See CLAUDE.md "User-action audit
            // logging" for the convention.
            Log.Information(
                "[Welcome.Command] action=ToggleShowOnLaunch newValue={NewValue}", newValue);
            vm.ShowWelcomeOnLaunch = newValue;
        }
    }
}