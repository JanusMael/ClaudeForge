using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Bennewitz.Ninja.ClaudeForge.Core;

namespace Bennewitz.Ninja.ClaudeForge.Views;

/// <summary>
/// "About ClaudeForge" modal opened from the bottom-right version button in
/// <see cref="MainWindow"/>. Shows app identity, version, copyright, and
/// links to the GitHub repository and issue tracker.
/// <para>
/// The dialog binds to <see cref="AppVersion"/> and <see cref="AppCopyright"/>
/// (both read once at construction from the entry assembly) rather than to
/// <c>MainWindowViewModel</c>, so it can be shown standalone from a unit-test
/// harness or any other surface that does not own the main window's view-model.
/// </para>
/// </summary>
public partial class AboutDialog : Window
{
    private const string GitHubUrl = "https://github.com/JanusMael/ClaudeForge";
    private const string ReportIssueUrl = "https://github.com/JanusMael/ClaudeForge/issues";

    /// <summary>Version string read once from the entry assembly.</summary>
    public string AppVersion { get; }

    /// <summary>
    /// Copyright text read once from the entry assembly's
    /// <see cref="AssemblyCopyrightAttribute"/>. Emitted at build time by the
    /// Bennewitz.Ninja.AutoVersioning source generator (when the consuming
    /// project sets the relevant MSBuild property), so it is only available
    /// via runtime reflection — not as a compile-time constant. Returns the
    /// empty string when the attribute is absent (e.g. a hand-built test
    /// host); the AXAML binding hides the line in that case via
    /// <c>StringConverters.IsNotNullOrEmpty</c>.
    /// </summary>
    public string AppCopyright { get; }

    public AboutDialog()
    {
        AppVersion = BackupConstants.AppVersion;
        AppCopyright = ReadAssemblyCopyright();
        DataContext = this;
        InitializeComponent();
        // Titlebar uses SmallInstance (simplified small SVG at 64 px) for
        // crisper rendering at the OS-scaled-down dialog titlebar size.
        // The in-dialog hero <Image> (Source=BitmapInstance) uses the
        // detailed 256-px master scaled to a 48-px display slot — same
        // asset that the Welcome page's hero icon renders so the two
        // surfaces share an identity.
        if (AppIcon.SmallInstance is not null)
        {
            Icon = AppIcon.SmallInstance;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnGitHubClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl(GitHubUrl);
    }

    private void OnReportIssueClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl(ReportIssueUrl);
    }

    /// <summary>
    /// Reads <see cref="AssemblyCopyrightAttribute"/> from the entry assembly
    /// via runtime reflection. The attribute is emitted at build time by the
    /// auto-versioning source generator; it cannot be captured as a compile-time
    /// constant from outside the assembly that hosts it. Falls back to the
    /// containing-assembly when <see cref="Assembly.GetEntryAssembly"/> returns
    /// null (test runners, design-time tooling). Returns an empty string when
    /// no copyright attribute was emitted.
    /// </summary>
    private static string ReadAssemblyCopyright()
    {
        Assembly asm = Assembly.GetEntryAssembly() ?? typeof(AboutDialog).Assembly;
        return asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
               ?? string.Empty;
    }

    /// <summary>Opens a URL in the platform default browser, cross-platform.</summary>
    private static void OpenUrl(string url)
    {
        try
        {
            // Process.Start returns a Process? whose handle is owned by the caller
            // even when UseShellExecute=true. The discard `using` ensures Dispose
            // runs immediately so the handle is not held until GC.
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
            // Non-fatal — URL opening is purely cosmetic. The user can still copy
            // the link out of the dialog manually if no default browser is wired up.
            _ = ex;
        }
    }
}