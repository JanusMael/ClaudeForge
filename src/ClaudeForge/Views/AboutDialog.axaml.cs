using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Bennewitz.Ninja.AgentForge.Core;
using Bennewitz.Ninja.AgentForge.Core.Updates;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Services;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Views;

/// <summary>
/// "About ClaudeForge" modal opened from the bottom-right version button in
/// <see cref="MainWindow"/>. Shows app identity, version, copyright, a
/// "Check for updates" button, and links to the GitHub repository and
/// issue tracker.
/// <para>
/// The dialog binds to <see cref="AppVersion"/> and <see cref="AppCopyright"/>
/// (both read once at construction from the entry assembly) rather than to
/// <c>MainWindowViewModel</c>, so it can be shown standalone from a unit-test
/// harness or any other surface that does not own the main window's view-model.
/// </para>
/// <para>
/// 2026-05-29 — gained an explicit "Check for updates" button + result-text
/// row.  The button bypasses the user-toggle gate on the Essentials page
/// (clicking is consent) and the once-per-launch latch (re-clicks are
/// allowed).  Implements <see cref="INotifyPropertyChanged"/> so the new
/// state (<see cref="IsCheckingForUpdate"/>,
/// <see cref="UpdateCheckResultText"/>, etc.) updates the UI without
/// requiring a separate view-model — the dialog was already its own
/// DataContext and adding a VM layer would have been more disruptive than
/// implementing INPC directly.
/// </para>
/// </summary>
public partial class AboutDialog : Window, INotifyPropertyChanged
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

    // ── Update-check bound state ─────────────────────────────────────────
    //
    // Manual INotifyPropertyChanged plumbing rather than ObservableObject
    // because the class already inherits Window.  CompilerServices.
    // CallerMemberName lets each setter pass its name implicitly.

    private bool _isCheckingForUpdate;
    private string _updateCheckResultText = string.Empty;
    private bool _hasUpdateCheckResult;
    private bool _updateCheckHasReleaseUrl;
    private string? _updateCheckReleaseUrl;

    /// <summary>
    /// <see langword="true"/> while the manual check is in flight.  The
    /// "Check for updates" button is disabled and replaced with a
    /// "Checking…" inline label.  Defaults <see langword="false"/>.
    /// </summary>
    public bool IsCheckingForUpdate
    {
        get => _isCheckingForUpdate;
        private set => SetField(ref _isCheckingForUpdate, value);
    }

    /// <summary>
    /// The localised result string after a manual check completes.  One of:
    /// <list type="bullet">
    ///   <item>"You're on the latest version." — when the check returned
    ///         <see cref="UpdateCheckResult.NoUpdate"/> via the live path.</item>
    ///   <item>"Update available: vX.Y.Z" — when a newer release was found.</item>
    ///   <item>"Couldn't reach GitHub.  Try again in a moment." — when the
    ///         live path returned NoUpdate but we can't tell apart "all good"
    ///         from "network failed" cleanly; the live path collapses both
    ///         to NoUpdate.  We emit the "up to date" message unless the
    ///         simulate-update flag is active (in which case NoUpdate
    ///         legitimately means "tag is older-than-current").</item>
    /// </list>
    /// Empty string until the first check completes.
    /// </summary>
    public string UpdateCheckResultText
    {
        get => _updateCheckResultText;
        private set => SetField(ref _updateCheckResultText, value);
    }

    /// <summary>
    /// <see langword="true"/> when <see cref="UpdateCheckResultText"/> is
    /// non-empty — bound to the result row's <c>IsVisible</c> so the row
    /// stays hidden until the user clicks Check for updates at least once.
    /// </summary>
    public bool HasUpdateCheckResult
    {
        get => _hasUpdateCheckResult;
        private set => SetField(ref _hasUpdateCheckResult, value);
    }

    /// <summary>
    /// <see langword="true"/> when the most recent result carried an
    /// html_url and an UpdateAvailable outcome.  Drives the View-release
    /// HyperlinkButton's IsVisible.
    /// </summary>
    public bool UpdateCheckHasReleaseUrl
    {
        get => _updateCheckHasReleaseUrl;
        private set => SetField(ref _updateCheckHasReleaseUrl, value);
    }

    /// <summary>
    /// URL of the GitHub release page when an update is available,
    /// otherwise <see langword="null"/>.  Bound to the HyperlinkButton's
    /// NavigateUri.
    /// </summary>
    public string? UpdateCheckReleaseUrl
    {
        get => _updateCheckReleaseUrl;
        private set => SetField(ref _updateCheckReleaseUrl, value);
    }

    /// <summary>
    /// Cancels any in-flight manual check when the dialog closes — avoids
    /// UI-thread marshalling after the dialog is gone (the SetField calls
    /// would no-op but a debugger break or a future logger could still
    /// surface noise).
    /// </summary>
    private readonly CancellationTokenSource _lifecycleCts = new();

    // ── Schema-update check ──────────────────────────────────────────────

    private readonly Func<CancellationToken, Task<string>>? _schemaCheck;
    private bool _isCheckingSchemas;
    private string _schemaCheckResultText = string.Empty;
    private bool _hasSchemaCheckResult;

    /// <summary>
    /// <see langword="true"/> when this dialog was given something to run, i.e. it was
    /// opened from the main window rather than standalone. Drives the row's visibility.
    /// </summary>
    /// <remarks>
    /// ⚠ Hidden rather than disabled when absent. A disabled button invites the reader to
    /// work out what would enable it, and nothing they can do here would — the answer is
    /// "this dialog has no main window behind it", which is not a state a user is ever in.
    /// </remarks>
    public bool CanCheckSchemas => _schemaCheck is not null;

    /// <summary><see langword="true"/> while the schema check is in flight.</summary>
    public bool IsCheckingSchemas
    {
        get => _isCheckingSchemas;
        private set => SetField(ref _isCheckingSchemas, value);
    }

    /// <summary>The one-line outcome, composed by the view-model that ran the check.</summary>
    public string SchemaCheckResultText
    {
        get => _schemaCheckResultText;
        private set => SetField(ref _schemaCheckResultText, value);
    }

    /// <summary>Whether the result row has anything to show yet.</summary>
    public bool HasSchemaCheckResult
    {
        get => _hasSchemaCheckResult;
        private set => SetField(ref _hasSchemaCheckResult, value);
    }

    /// <param name="schemaCheck">
    /// Runs the schema check and returns the localized line to display.
    /// <see langword="null"/> hides the whole row.
    /// <para>
    /// ⚠ A delegate rather than a view-model reference. This dialog is deliberately its own
    /// DataContext so it can be shown standalone from a test harness or any surface that does
    /// not own <c>MainWindowViewModel</c> — taking the VM as a dependency would end that, and
    /// the only thing needed from it is "run this and give me a sentence".
    /// </para>
    /// </param>
    /// <summary>
    /// Parameterless overload, kept because Avalonia's runtime XAML loader requires one.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>An optional parameter is not a parameterless constructor</b> as far as that loader
    /// is concerned. Collapsing the two into
    /// <c>AboutDialog(Func&lt;…&gt;? schemaCheck = null)</c> compiles, runs, and passes every
    /// test — and emits <c>AVLN3001</c> saying the XAML resource "won't be reachable via
    /// runtime loader". Found by the Release publish, not by the Debug suite.
    /// </remarks>
    public AboutDialog() : this(null)
    {
    }

    public AboutDialog(Func<CancellationToken, Task<string>>? schemaCheck)
    {
        _schemaCheck = schemaCheck;
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

        Closed += (_, _) =>
        {
            try
            {
                _lifecycleCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The dialog may close in unusual lifecycle paths; double-
                // cancel is harmless to surface.
            }
            _lifecycleCts.Dispose();
        };
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
    /// User clicked "Check for schema updates" — re-fetch the setting definitions the
    /// editors are built from and report what upstream had.
    /// </summary>
    /// <remarks>
    /// ⚠ Distinct from the app-update check above it, and the two are only neighbours
    /// because both answer "am I running something stale?". This one reaches
    /// schemastore.org and opencode.ai, not GitHub releases, and what it refreshes is the
    /// shape of the editors rather than the binary.
    /// </remarks>
    private async void OnCheckForSchemaUpdatesClick(object? sender, RoutedEventArgs e)
    {
        if (IsCheckingSchemas || _schemaCheck is null)
        {
            return;
        }

        IsCheckingSchemas = true;
        SchemaCheckResultText = Strings.SchemaCheckChecking;
        HasSchemaCheckResult = true;

        string result;
        try
        {
            result = await _schemaCheck(_lifecycleCts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Dialog closed mid-check. Nothing to surface.
            return;
        }
        catch (Exception ex)
        {
            // Defensive: SchemaRefresher walls off the failures it can name, but an
            // unhandled escape must show as a failure rather than leave the row stuck on
            // "Checking…" forever — which is indistinguishable from a hung network.
            Log.Error(ex, "[SchemaCheck] Manual schema check threw unexpectedly.");
            result = string.Format(
                CultureInfo.CurrentCulture, Strings.SchemaCheckFailedFmt, ex.GetType().Name);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SchemaCheckResultText = result;
            IsCheckingSchemas = false;
        });
    }

    /// <summary>
    /// User clicked "Check for updates".  Fires the manual check (bypasses
    /// the auto-check opt-out + the once-per-launch latch) and surfaces
    /// the outcome inline on the dialog.  Cancellation is wired to dialog
    /// lifetime via <see cref="_lifecycleCts"/>.
    /// </summary>
    private async void OnCheckForUpdatesClick(object? sender, RoutedEventArgs e)
    {
        if (IsCheckingForUpdate)
        {
            return;
        }

        IsCheckingForUpdate = true;
        UpdateCheckResultText = Strings.LabelCheckForUpdatesChecking;
        HasUpdateCheckResult = true;
        UpdateCheckHasReleaseUrl = false;
        UpdateCheckReleaseUrl = null;

        UpdateCheckResult result;
        try
        {
            result = await AppUpdateService.CheckManualAsync(_lifecycleCts.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Dialog closed mid-check.  Nothing to surface; properties
            // will be cleaned up by Dispose anyway.
            return;
        }
        catch (Exception ex)
        {
            // Defensive: AppUpdateService.CheckManualAsync has its own
            // exception walls (the underlying GithubReleaseChecker
            // returns NoUpdate on every network failure), but a totally
            // unhandled escape lands here — show the failure message
            // rather than crashing the dialog.
            Log.Information(ex, "[UpdateCheck] Manual check threw unexpectedly.");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateCheckResultText = Strings.LabelCheckForUpdatesFailed;
                IsCheckingForUpdate = false;
            });
            return;
        }

        // Marshal to UI thread before mutating bound state — ConfigureAwait
        // (true) above should already keep us here, but the explicit
        // dispatcher InvokeAsync is defence-in-depth.
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (result.IsUpdateAvailable && !string.IsNullOrWhiteSpace(result.LatestTagName))
            {
                UpdateCheckResultText = string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.LabelCheckForUpdatesAvailableFmt,
                    result.LatestTagName);
                UpdateCheckHasReleaseUrl = !string.IsNullOrWhiteSpace(result.ReleaseUrl);
                UpdateCheckReleaseUrl = result.ReleaseUrl;
            }
            else
            {
                // The live path collapses BOTH "all good" AND "network
                // failed" to NoUpdate.  Show the optimistic message —
                // logging the failure case is enough for a forensic
                // trail without alarming the user on a flaky network.
                UpdateCheckResultText = Strings.LabelCheckForUpdatesUpToDate;
                UpdateCheckHasReleaseUrl = false;
                UpdateCheckReleaseUrl = null;
            }

            IsCheckingForUpdate = false;
        });
    }

    /// <summary>
    /// HyperlinkButton click for the inline "View release" link.  Uses
    /// the same OpenUrl helper as the GitHub / Report-issue links so the
    /// cross-platform behaviour is identical.
    /// </summary>
    private void OnViewUpdateReleaseClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(UpdateCheckReleaseUrl))
        {
            OpenUrl(UpdateCheckReleaseUrl);
        }
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

    // ── INotifyPropertyChanged ───────────────────────────────────────────

    /// <inheritdoc />
    public new event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Backing setter for the manually-INPC'd properties above.  Avoids
    /// raising PropertyChanged when the value is unchanged, matching
    /// CommunityToolkit.Mvvm's ObservableProperty source-gen behaviour.
    /// </summary>
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
