using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Bennewitz.Ninja.AgentForge.Core;
using Bennewitz.Ninja.OpenCodeForge.Localization;
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

    // ── INotifyPropertyChanged ───────────────────────────────────────────

    /// <inheritdoc />
    public new event PropertyChangedEventHandler? PropertyChanged;

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
