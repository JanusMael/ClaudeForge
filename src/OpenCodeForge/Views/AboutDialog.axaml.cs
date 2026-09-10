using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Bennewitz.Ninja.AgentForge.Core;
using Bennewitz.Ninja.AgentForge.Core.Updates;
using Bennewitz.Ninja.OpenCodeForge.Localization;
using Bennewitz.Ninja.OpenCodeForge.Services;
// Aliased for the same reason MainWindow.axaml.cs aliases it: Avalonia has its own
// WindowState enum, and this app has a record of that name for persisted state. Unaliased,
// whichever using came last wins and the error points at the property rather than the clash.
using SavedWindowState = Bennewitz.Ninja.OpenCodeForge.Services.WindowState;
using Serilog;

namespace Bennewitz.Ninja.OpenCodeForge.Views;

/// <summary>
/// "About OpenCodeForge" modal, opened from the status-bar version button in
/// <see cref="MainWindow"/>.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Its reason for existing is the schema-update check</b>, not the version line.
/// ClaudeForge puts that action next to its app-update check in this dialog, and this app had
/// no equivalent surface at all — no About dialog, no version anywhere on screen. The version
/// line is here because a dialog called About that omitted it would be strange, not because
/// anything needed it.
/// </para>
/// <para>
/// ⚠ <b>Deliberately NOT a mirror of ClaudeForge's dialog.</b> That one carries an app-update
/// check, a copyright line and repository links; this app has no release pipeline yet and sets
/// no copyright attribute, so those rows would render blank or offer an action that does not
/// exist. They belong with Phase 15, which is where OpenCodeForge's packaging lives.
/// </para>
/// <para>
/// Implements <see cref="INotifyPropertyChanged"/> directly rather than gaining a view-model:
/// the dialog is its own DataContext, and the state is three fields that exist only while it
/// is open.
/// </para>
/// </remarks>
public partial class AboutDialog : Window, INotifyPropertyChanged
{
    private readonly Func<CancellationToken, Task<string>>? _schemaCheck;
    private readonly CancellationTokenSource _lifecycleCts = new();

    private bool _isCheckingSchemas;
    private string _schemaCheckResultText = string.Empty;
    private bool _hasSchemaCheckResult;

    private bool _isCheckingForUpdate;
    private string _updateCheckResultText = string.Empty;
    private bool _hasUpdateCheckResult;
    private bool _updateCheckHasReleaseUrl;
    private string? _updateCheckReleaseUrl;

    /// <summary>The version line, resolved once at construction.</summary>
    public string VersionText { get; }

    /// <summary>
    /// <see langword="true"/> when this dialog was given something to run, i.e. it was opened
    /// from the main window rather than standalone.
    /// </summary>
    /// <remarks>
    /// ⚠ Hidden rather than disabled when absent: a disabled button invites the reader to work
    /// out what would enable it, and nothing they can do would.
    /// </remarks>
    public bool CanCheckSchemas => _schemaCheck is not null;

    /// <summary><see langword="true"/> while the check is in flight.</summary>
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

    /// <summary><see langword="true"/> while the app-update check is in flight.</summary>
    public bool IsCheckingForUpdate
    {
        get => _isCheckingForUpdate;
        private set => SetField(ref _isCheckingForUpdate, value);
    }

    /// <summary>The one-line outcome of the app-update check.</summary>
    public string UpdateCheckResultText
    {
        get => _updateCheckResultText;
        private set => SetField(ref _updateCheckResultText, value);
    }

    /// <summary>Whether the app-update result row has anything to show yet.</summary>
    public bool HasUpdateCheckResult
    {
        get => _hasUpdateCheckResult;
        private set => SetField(ref _hasUpdateCheckResult, value);
    }

    /// <summary>Whether the found release has a page worth linking to.</summary>
    public bool UpdateCheckHasReleaseUrl
    {
        get => _updateCheckHasReleaseUrl;
        private set => SetField(ref _updateCheckHasReleaseUrl, value);
    }

    /// <summary>The release page for the version just found, if any.</summary>
    public string? UpdateCheckReleaseUrl
    {
        get => _updateCheckReleaseUrl;
        private set => SetField(ref _updateCheckReleaseUrl, value);
    }


    /// <param name="schemaCheck">
    /// Runs the schema check and returns the localized line to display.
    /// <see langword="null"/> hides the whole row.
    /// </param>
    /// <summary>
    /// Parameterless overload, kept because Avalonia's runtime XAML loader requires one.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>An optional parameter is not a parameterless constructor</b> as far as that loader
    /// is concerned. Collapsing the two emits <c>AVLN3001</c> — "won't be reachable via runtime
    /// loader" — which the Debug suite does not surface.
    /// </remarks>
    public AboutDialog() : this(null)
    {
    }

    public AboutDialog(Func<CancellationToken, Task<string>>? schemaCheck)
    {
        _schemaCheck = schemaCheck;
        VersionText = string.Format(
            CultureInfo.CurrentCulture, Strings.LabelVersionFmt, BackupConstants.AppVersion);

        DataContext = this;
        InitializeComponent();

        // Same icon as the main window: a dialog with the default Avalonia icon next to a
        // parent that has the app's reads as a different application in the Alt-Tab list.
        if (AppIcon.Instance is { } icon)
        {
            Icon = icon;
        }

        Closed += (_, _) =>
        {
            try
            {
                _lifecycleCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Unusual lifecycle paths can close twice; a double-cancel is not worth
                // surfacing.
            }

            _lifecycleCts.Dispose();
        };
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

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
            // Defensive: SchemaRefresher walls off the failures it can name, but an unhandled
            // escape must show as a failure rather than leave the row stuck on "Checking…",
            // which is indistinguishable from a hung network.
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
    /// The explicit "Check for updates" click.
    /// </summary>
    /// <remarks>
    /// ⭐ <b>Deliberately bypasses the launch opt-out.</b> Clicking this IS consent, whatever
    /// the Essentials toggle says — the toggle governs the silent automatic check, not an
    /// action the user just took.
    /// </remarks>
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
            // The dialog closed mid-check. Nothing to surface.
            return;
        }
        catch (Exception ex)
        {
            // Defensive. The checker already collapses every network failure to NoUpdate, so
            // reaching here means something unanticipated — show the failure line rather than
            // taking the dialog down with it.
            Log.Information(ex, "[UpdateCheck] Manual check threw unexpectedly.");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateCheckResultText = Strings.LabelCheckForUpdatesFailed;
                IsCheckingForUpdate = false;
            });
            return;
        }

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
                // ⚠ The live path collapses BOTH "up to date" AND "the network failed" to
                // NoUpdate, so this line is shown for both. That is the intended trade: the
                // failure is in the log for a forensic trail, and telling someone their update
                // check failed is noise they cannot act on.
                UpdateCheckResultText = Strings.LabelCheckForUpdatesUpToDate;
                UpdateCheckHasReleaseUrl = false;
                UpdateCheckReleaseUrl = null;
            }

            IsCheckingForUpdate = false;
        });
    }

    /// <summary>Open the release page for the version the check just found.</summary>
    private void OnViewUpdateReleaseClick(object? sender, RoutedEventArgs e) =>
        UrlOpener.Open(UpdateCheckReleaseUrl);

    // ── INotifyPropertyChanged ───────────────────────────────────────────

    /// <inheritdoc />
    public new event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raise <see cref="PropertyChanged"/> for a property with no backing field.
    /// </summary>
    /// <remarks>
    /// <see cref="CheckForUpdatesOnLaunch"/> reads through to persisted state rather than
    /// holding a field, so it has nothing for <see cref="SetField{T}"/> to compare against.
    /// </remarks>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
