using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Views;

public partial class SettingsGroupEditorView : UserControl
{
    private SettingsGroupEditorViewModel? _vm;

    public SettingsGroupEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.CopyJsonRequested -= OnCopyJsonRequested;
        }

        _vm = DataContext as SettingsGroupEditorViewModel;

        if (_vm != null)
        {
            _vm.CopyJsonRequested += OnCopyJsonRequested;
        }
    }

    private async void OnCopyJsonRequested(object? sender, string json)
    {
        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(json);
        }
    }

    /// <summary>
    /// Toggles the per-editor "?" scope-legend popup anchored to the
    /// scope ComboBox in the editor header. Light-dismiss is enabled
    /// on the popup, so closing it just requires a click outside.
    /// </summary>
    private void OnScopeLegendButtonClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<Popup>("ScopeLegendPopup") is { } popup)
        {
            popup.IsOpen = !popup.IsOpen;
        }
    }
}