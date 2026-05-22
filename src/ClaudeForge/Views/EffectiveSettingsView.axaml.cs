using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Views;

public partial class EffectiveSettingsView : UserControl
{
    public EffectiveSettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private EffectiveSettingsViewModel? _vm;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.CopyJsonRequested -= OnCopyJsonRequested;
        }

        _vm = DataContext as EffectiveSettingsViewModel;

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
    /// Per-cell "Copy" handler for the effective-value DataGrid column.
    /// Copies the row's full <see cref="EffectivePropertyRow.DisplayValue"/>
    /// — not the ellipsis-trimmed visible text — so long JSON values are
    /// recoverable without expanding the column.  Standard
    /// <see cref="SelectableTextBlock"/> drag-select + Ctrl+C still works
    /// for partial copies of the visible text.
    /// </summary>
    private async void OnCopyEffectiveValue(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
        {
            return;
        }

        if (menuItem.DataContext is not EffectivePropertyRow row)
        {
            return;
        }

        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(row.DisplayValue))
        {
            await clipboard.SetTextAsync(row.DisplayValue);
        }
    }
}