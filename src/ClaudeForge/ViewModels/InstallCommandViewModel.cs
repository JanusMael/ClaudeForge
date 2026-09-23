using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.AppServices;
using Bennewitz.Ninja.AppServices.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// View model backing the reusable <c>InstallCommandPanel</c> UserControl.
/// Bundles the monospace command (or URL) text together with the labels,
/// tooltips, and <see cref="RelayCommand"/> needed to "run" it — i.e. launch
/// a terminal pre-filled with the shell command, or open the browser at the
/// download URL.
///
/// Use <see cref="ForClaudeCode"/> for the CLI install flow (shell command +
/// terminal launcher) and <see cref="ForClaudeDesktop"/> for the Desktop
/// download flow (URL + default-browser launcher).  Copying is handled by
/// the hosting UserControl via <see cref="CommandText"/>, since clipboard
/// access needs a <c>TopLevel</c> reference.
/// </summary>
public sealed partial class InstallCommandViewModel : ObservableObject
{
    /// <summary>The text shown in the monospace code block — shell command or URL.</summary>
    public string CommandText { get; }

    /// <summary>Localised label for the Run button (shell-specific or "Open in Browser").</summary>
    public string RunButtonLabel { get; }

    /// <summary>Localised tooltip for the Run button.</summary>
    public string RunButtonTooltip { get; }

    /// <summary>AutomationProperties.Name for the Run button — non-visual accessibility text.</summary>
    public string RunButtonAutomationName { get; }

    /// <summary>Localised tooltip for the Copy button.</summary>
    public string CopyButtonTooltip { get; }

    /// <summary>AutomationProperties.Name for the Copy button.</summary>
    public string CopyButtonAutomationName { get; }

    private readonly Func<CancellationToken, ValueTask<LaunchResult>> _runAction;

    /// <summary>
    /// Non-null for up to 3 seconds after a terminal launch fails.
    /// Bound to the "no terminal found" hint TextBlock in InstallCommandPanel.axaml.
    /// </summary>
    [ObservableProperty] private string? _noTerminalMessage;

    private InstallCommandViewModel(
        string commandText,
        string runButtonLabel,
        string runButtonTooltip,
        string runButtonAutomationName,
        string copyButtonTooltip,
        string copyButtonAutomationName,
        Func<CancellationToken, ValueTask<LaunchResult>> runAction)
    {
        CommandText = commandText;
        RunButtonLabel = runButtonLabel;
        RunButtonTooltip = runButtonTooltip;
        RunButtonAutomationName = runButtonAutomationName;
        CopyButtonTooltip = copyButtonTooltip;
        CopyButtonAutomationName = copyButtonAutomationName;
        _runAction = runAction;
    }

    /// <summary>
    /// Invoked from the UserControl's Run button. Delegates to the platform
    /// action supplied by the factory (terminal launch or browser open).
    /// When the action reports failure (no terminal found), sets
    /// <see cref="NoTerminalMessage"/> for 3 seconds so the view can surface
    /// a "use Copy instead" hint to the user.
    /// </summary>
    [RelayCommand]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        bool launched;
        try
        {
            LaunchResult result = await _runAction(cancellationToken);

            // ⭐ Every status except a cancel shows the hint, exactly as the old bool did: the hint
            // is guidance ("use Copy instead"), not an error, so it is the right answer to
            // Unsupported too. Unsupported is REAL here, unlike the reveal/open sites — the
            // launcher returns it when no known terminal emulator is installed, which a Linux
            // desktop can be.
            launched = result.Status is LaunchStatus.Succeeded or LaunchStatus.Cancelled;
            if (!launched)
            {
                Log.Information("[Install] Run did not launch: {Status} ({Detail})", result.Status, result.Detail);
            }
        }
        catch (Exception ex) when (ex is Win32Exception
                                       or InvalidOperationException
                                       or FileNotFoundException
                                       or PlatformNotSupportedException
                                       or NotSupportedException)
        {
            // _runAction is a terminal-launch or browser-open delegate; any of these
            // exceptions indicates the launcher chain found nothing usable, so we
            // surface the "no terminal found" hint below rather than crashing.
            _ = ex;
            launched = false;
        }

        if (!launched)
        {
            NoTerminalMessage = Strings.TextNoTerminalFound;
            _ = Task.Delay(3000).ContinueWith(_ => Dispatcher.UIThread.Post(() => NoTerminalMessage = null));
        }
    }

    /// <summary>
    /// VM flavour for the Claude Code CLI install flow — emits the
    /// platform-appropriate shell one-liner and runs it in a new terminal.
    /// </summary>
    public static InstallCommandViewModel ForClaudeCode(IShellLauncher? shellLauncher = null)
    {
        IShellLauncher shell = shellLauncher ?? ShellLauncher.Instance;
        // Route through PlatformInfo.Current rather than OperatingSystem.IsWindows()
        // so the --linux / --macos / --windows debug flags surface the correct
        // platform-specific install one-liner. The displayed command is purely
        // copy-and-paste content for the user; emulation does not affect the
        // shell launcher itself (which still runs against the real host OS).
        bool isWindows = PlatformInfo.Current.IsWindows;
        string commandText = isWindows
            ? "irm https://claude.ai/install.ps1 | iex"
            : "curl -fsSL https://claude.ai/install.sh | bash";
        string? runLabel = isWindows ? Strings.InstallRunPowerShell : Strings.InstallRunTerminal;

        return new InstallCommandViewModel(
            commandText: commandText,
            runButtonLabel: runLabel,
            runButtonTooltip: Strings.TipButtonRunInstall,
            runButtonAutomationName: Strings.AutoNameButtonRunInstall,
            copyButtonTooltip: Strings.TipButtonCopyInstall,
            copyButtonAutomationName: Strings.AutoNameButtonCopyInstall,
            runAction: ct => shell.LaunchTerminalWithCommandAsync(commandText, ct));
    }

    /// <summary>
    /// VM flavour for the Claude Desktop install flow — no shell installer
    /// exists, so this surfaces the download URL and "runs" it by opening
    /// the default browser.
    /// </summary>
    public static InstallCommandViewModel ForClaudeDesktop()
    {
        const string downloadUrl = "https://claude.ai/download";

        return new InstallCommandViewModel(
            commandText: downloadUrl,
            runButtonLabel: Strings.InstallRunBrowser,
            runButtonTooltip: Strings.TipButtonOpenDownload,
            runButtonAutomationName: Strings.AutoNameButtonOpenDownload,
            copyButtonTooltip: Strings.TipButtonCopyUrl,
            copyButtonAutomationName: Strings.AutoNameButtonCopyUrl,
            runAction: _ =>
            {
                // Synchronous and exception-reporting, unlike the launcher: a throw here is caught
                // by RunAsync's filter below and shown as the hint, as it always was.
                OpenUrl(downloadUrl);
                return ValueTask.FromResult(LaunchResult.Ok());
            });
    }

    /// <summary>
    /// Cross-platform default-browser launcher.  Windows uses ShellExecute
    /// (the only way to honour the user's default-browser preference); macOS
    /// and Linux delegate to <c>open</c> / <c>xdg-open</c>.  Kept private
    /// because the only caller is <see cref="ForClaudeDesktop"/>; if a third
    /// URL flow appears, promote this to <c>IShellLauncher.OpenUrl</c>.
    /// </summary>
    private static void OpenUrl(string url)
    {
        // Process.Start returns a Process? whose handle is owned by the caller
        // (even with UseShellExecute=true). The discard `using` ensures Dispose
        // runs immediately so the handle is not held until GC.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { url },
                UseShellExecute = false,
            });
        }
        else
        {
            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                ArgumentList = { url },
                UseShellExecute = false,
            });
        }
    }
}