using Avalonia.Controls;
using Avalonia.Input.Platform;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Views;

/// <summary>
/// Code-behind for <c>MemoryEditorView</c>. Sole responsibility: bridge the VM's
/// <see cref="MemoryEditorViewModel.CopyMarkdownRequested"/> event to the clipboard
/// (which needs <see cref="TopLevel"/>), keeping the VM free of any view dependency.
/// <para>
/// The markdown rendering, dark-theme <c>MarkdownStyle</c>, and the force-restyle
/// tree-walk now live in the reusable <c>MarkdownBodyView</c> control
/// (<c>Controls/MarkdownBodyView.axaml[.cs]</c>) — shared with the Agents &amp; Skills
/// page — so this file no longer carries any of that machinery.
/// </para>
/// </summary>
public partial class MemoryEditorView : UserControl
{
    private MemoryEditorViewModel? _vm;

    public MemoryEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.CopyMarkdownRequested -= OnCopyMarkdownRequested;
        }

        _vm = DataContext as MemoryEditorViewModel;

        if (_vm is not null)
        {
            _vm.CopyMarkdownRequested += OnCopyMarkdownRequested;
        }
    }

    private async void OnCopyMarkdownRequested(object? sender, string markdown)
    {
        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(markdown);
        }
    }
}
