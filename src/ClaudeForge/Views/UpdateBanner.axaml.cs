using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Views;

/// <summary>
/// "Update available" banner that surfaces when a newer ClaudeForge
/// release is detected on launch.  DataContext is supplied via the
/// MainWindow slot: <c>&lt;views:UpdateBanner DataContext="{Binding UpdateBanner}" /&gt;</c>.
///
/// <para>
/// Designed-by-extraction sibling of <c>InstallBanner.axaml</c> +
/// <c>SchemaErrorBanner.axaml</c> — same amber palette, same
/// right-edge button cluster + ✕ dismiss pattern, so the three banners
/// read as a coherent "advisory chrome" set.
/// </para>
///
/// <para>
/// The two buttons in the cluster (View release + ✕ dismiss) are both
/// regular Buttons rather than mixing a HyperlinkButton and a Button —
/// see the lead comment in <c>UpdateBanner.axaml</c> for the
/// alignment-drift rationale.  The View-release Button uses a Click
/// handler here that opens the URL via cross-platform
/// <see cref="Process.Start"/>, same shape as
/// <c>AboutDialog.OpenUrl</c>.
/// </para>
/// </summary>
public partial class UpdateBanner : UserControl
{
    public UpdateBanner()
    {
        InitializeComponent();
    }

    /// <summary>
    /// User clicked "View release".  Reads the URL off the bound
    /// <see cref="UpdateBannerViewModel"/> and opens it in the platform
    /// default browser.  Cross-platform implementation matches
    /// <c>AboutDialog.OpenUrl</c> — the small amount of duplication is
    /// acceptable because both surfaces ship in the same assembly and
    /// the dialog / banner are otherwise self-contained.
    /// </summary>
    private void OnViewReleaseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UpdateBannerViewModel vm &&
            !string.IsNullOrWhiteSpace(vm.ReleaseUrl))
        {
            OpenUrl(vm.ReleaseUrl);
        }
    }

    /// <summary>Opens a URL in the platform default browser, cross-platform.</summary>
    private static void OpenUrl(string url)
    {
        try
        {
            // Discard `using` ensures the returned Process handle is disposed
            // immediately rather than held until GC — same shape AboutDialog
            // uses for its GitHub / Report-issue links.
            using Process? _ =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true })
                    : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                        ? Process.Start(new ProcessStartInfo
                            { FileName = "open", ArgumentList = { url }, UseShellExecute = false })
                        : Process.Start(new ProcessStartInfo
                            { FileName = "xdg-open", ArgumentList = { url }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is Win32Exception
                                       or InvalidOperationException
                                       or FileNotFoundException
                                       or PlatformNotSupportedException)
        {
            // Non-fatal — URL opening is purely cosmetic.  A banner click
            // that lands on a host with no default browser shouldn't crash
            // the app; the user can still copy the tag and visit GitHub
            // manually.
            _ = ex;
        }
    }
}
