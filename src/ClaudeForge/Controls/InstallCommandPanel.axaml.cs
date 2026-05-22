using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Controls;

/// <summary>
/// Reusable install-command panel shared by the MainWindow install banner and
/// the <c>AboutEditorView</c> "(not detected)" row.  Binds against
/// <see cref="InstallCommandViewModel"/>.
///
/// The Run button fires the VM's <c>RunCommand</c> (terminal-launch or
/// browser-open, per factory).  The Copy button is handled in code-behind
/// because clipboard access requires a <c>TopLevel</c> reference, which is
/// only available at the Avalonia control layer.
/// </summary>
public partial class InstallCommandPanel : UserControl
{
    public InstallCommandPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Copies <see cref="InstallCommandViewModel.CommandText"/> to the system
    /// clipboard.  Silently no-ops when the data context is missing or the
    /// top-level window exposes no clipboard (edge case: hosted in a surface
    /// without a <c>TopLevel</c> parent, e.g. during design-time preview).
    /// </summary>
    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InstallCommandViewModel vm)
        {
            return;
        }

        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(vm.CommandText);
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                       or COMException
                                       or PlatformNotSupportedException
                                       or NotSupportedException)
        {
            // Clipboard access can fail on Linux when no clipboard manager is
            // running, on Windows when another app holds the clipboard, or on
            // headless surfaces. Swallow to avoid crashing the install-guidance UX.
            _ = ex;
        }
    }
}