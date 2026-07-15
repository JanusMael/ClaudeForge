using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// Shows the fully merged effective configuration as formatted JSON,
/// with a flat table showing which scope provides each property.
/// Automatically refreshes whenever the workspace changes so the display
/// stays current without requiring a manual Refresh click.
/// </summary>
/// <remarks>
/// ctor takes <see cref="ClaudeConfigClientCore"/>
/// directly. Reads (compute-effective, defined keys, layered probes) flow
/// through the SDK's internal snapshot helpers; auto-refresh subscribes to
/// the SDK's <see cref="IClaudeConfigClient.Changed"/> event (which
/// includes the workspace.Changed forwarder shipped in step 8 — so editor
/// direct writes still trigger refresh).
/// </remarks>
public partial class EffectiveSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ClaudeConfigClientCore _client;
    private readonly string? _projectRoot;
    private readonly IShareService? _shareService;
    private readonly IReadOnlyDictionary<string, string> _descriptions;
    private bool _disposed;

    public EffectiveSettingsViewModel(
        ClaudeConfigClientCore client,
        string? projectRoot = null,
        IShareService? shareService = null,
        IReadOnlyDictionary<string, string>? descriptions = null)
    {
        _client = client;
        _projectRoot = projectRoot;
        _shareService = shareService;
        // Top-level-key → schema description, so the property column can show help text
        // on hover. Empty when not supplied (tests / headless) — tooltip falls back to
        // the path.
        _descriptions = descriptions ?? new Dictionary<string, string>();
        EffectiveJson = string.Empty;
        PropertyRows = [];
        FilterText = string.Empty;

        // Auto-refresh so the view stays current after any editor change,
        // without requiring the user to manually click Refresh. Dispatched
        // to the UI thread because file-watcher triggers and forwarded
        // workspace events arrive on background threads.
        _client.Changed += OnSdkChanged;
        Refresh();
    }

    [ObservableProperty] private string _effectiveJson;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FilteredRows))]
    private string _filterText;

    // Which tab (Properties / Raw JSON) is shown. VM-driven so the change is
    // observable + logged, not view-only. 0=Properties, 1=Json.
    [ObservableProperty] private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        string tab = value switch { 0 => "Properties", 1 => "Json", _ => "?" };
        Log.Information("[Effective.Tab] index={Index} tab={Tab}", value, tab);
    }

    public List<EffectivePropertyRow> PropertyRows { get; private set; }

    public IEnumerable<EffectivePropertyRow> FilteredRows =>
        string.IsNullOrWhiteSpace(FilterText)
            ? PropertyRows
            : PropertyRows.Where(r =>
                r.Property.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                r.DisplayValue.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Human-readable status line shown in the header.
    /// Informs the user which project is loaded — or warns that project/local
    /// scope files are absent when no project has been opened.
    /// </summary>
    public string ProjectStatusText =>
        _projectRoot is not null
            ? $"Project: {_projectRoot}"
            : "No project open — Project and Local scope settings are not loaded";

    /// <summary>True when no project is open; drives an amber warning banner in the view.</summary>
    public bool IsProjectMissing => _projectRoot is null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Changed -= OnSdkChanged;
    }

    [RelayCommand]
    private void Refresh()
    {
        // Guard: a queued Dispatcher.Post can fire after the client is disposed
        // (e.g. a save during window-close posts a Changed event, then the
        // client is disposed before the dispatcher drains).
        if (_disposed)
        {
            return;
        }

        JsonObject merged = _client.ComputeEffectiveSnapshot();

        JsonSerializerOptions opts = new() { WriteIndented = true };
        EffectiveJson = merged.ToJsonString(opts);

        List<EffectivePropertyRow> rows = new();
        foreach (string key in _client.AllDefinedKeysSnapshot())
        {
            LayeredValue layered = _client.GetLayeredValueSnapshot(key);
            rows.Add(new EffectivePropertyRow(
                key,
                layered.EffectiveValue?.ToJsonString() ?? "(null)",
                layered.EffectiveScope,
                layered.IsOverridden,
                _descriptions.GetValueOrDefault(key)));
        }

        rows.Sort((a, b) => string.Compare(a.Property, b.Property, StringComparison.Ordinal));
        PropertyRows = rows;
        OnPropertyChanged(nameof(PropertyRows));
        OnPropertyChanged(nameof(FilteredRows));
    }

    [RelayCommand]
    private void CopyJson()
    {
        // Clipboard access requires the UI thread — handled in code-behind via ClipboardService
        CopyJsonRequested?.Invoke(this, EffectiveJson);
    }

    /// <summary>Raised when the user clicks Copy JSON; the View wires clipboard access.</summary>
    public event EventHandler<string>? CopyJsonRequested;

    /// <summary>
    /// Shares the current effective settings JSON via the OS share sheet.
    /// </summary>
    [RelayCommand]
    private async Task ShareConfigAsync()
    {
        if (_shareService is null)
        {
            return;
        }

        try
        {
            await _shareService.ShareTextAsync("Claude Config", EffectiveJson);
        }
        catch (Exception ex)
        {
            // Share is best-effort; log without surfacing a dialog.
            Log.Error(ex, "[EffectiveSettings] Share failed: {Message}", ex.Message);
        }
    }

    private void OnSdkChanged(object? sender, ClientChangedEventArgs e)
    {
        // Mutations can originate from the file watcher (background thread)
        // or from any SDK / workspace.Changed-forwarded write — always
        // dispatch Refresh() onto the UI thread so Avalonia property-change
        // notifications fire on the correct thread.
        //
        // Early-exit if already disposed: the event can still fire briefly
        // after Dispose() is called (race between unsubscription and in-flight
        // invocations), and Refresh() guards internally anyway, but this
        // avoids queuing a no-op dispatcher post.
        if (_disposed)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            Dispatcher.UIThread.Post(Refresh);
        }
    }
}

public sealed record EffectivePropertyRow(
    string Property,
    string DisplayValue,
    ConfigScope? Scope,
    bool IsOverridden,
    string? Description = null)
{
    /// <summary>
    /// Tooltip for the property-name cell: the schema description when known, else the
    /// raw path (so the cell always has a meaningful hover, matching the old behaviour).
    /// </summary>
    public string PropertyTooltip => string.IsNullOrWhiteSpace(Description) ? Property : Description!;
}