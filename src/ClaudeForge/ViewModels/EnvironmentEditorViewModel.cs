using System.Collections;
using System.Collections.ObjectModel;
using System.Security;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using Bennewitz.Ninja.ClaudeForge.Converters;
using Bennewitz.Ninja.ClaudeForge.Core.JsonHelpers;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
// Alias the SDK so the SDK client type and Changed event args don't clash
// with anything else in this file. Mirrors the editor migrations from 4.3.6.

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>Which persistent store the user wants to write an env var to.</summary>
public enum EnvEditScope
{
    /// <summary>Claude Code's <c>env</c> object (written to the User-scope settings file).</summary>
    Claude,

    /// <summary>Windows HKCU user environment (Windows only).</summary>
    User,

    /// <summary>Windows HKLM machine environment (Windows only, requires elevation).</summary>
    Machine,
}

/// <summary>Display info for a single env-edit scope — display name + short description.</summary>
public sealed record EnvScopeInfo(EnvEditScope Value, string DisplayName, string Description)
{
    public string AccessibleName => $"{DisplayName}: {Description}";
}

/// <summary>
/// Unified environment-variable editor.
/// Merges System.Environment (Machine → User → Process) with the Claude <c>env</c> dict
/// into a single read/write view.
/// </summary>
public partial class EnvironmentEditorViewModel : ObservableObject, IDisposable
{
    private bool _disposed;

    // Guards against rebuilding in response to workspace writes that this VM itself initiates,
    // which would cause a double-rebuild (one from the Changed event, one from Refresh()).
    private bool _selfWriting;

    private readonly IEnvironmentProvider _envProvider;
    private readonly ClaudeConfigClientCore? _client;
    private readonly IReadOnlyList<string> _suggestedEnvVarNames;

    // Variables whose names match the allowlist are shown when ShowAll is false.
    // Built at startup so the platform check runs once.
    private static readonly Regex AllowlistPattern = BuildAllowlistPattern();

    // POSIX env var name constraint: [A-Za-z_][A-Za-z0-9_]*
    // Prevents saving a key with spaces or '=' which creates a broken/unreadable variable.
    private static readonly Regex EnvKeyRegex = new(
        @"^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="key"/> is a valid POSIX
    /// environment variable name (<c>[A-Za-z_][A-Za-z0-9_]*</c>).
    /// </summary>
    public static bool IsValidEnvKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        try
        {
            return EnvKeyRegex.IsMatch(key);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static Regex BuildAllowlistPattern()
    {
        // Core variables shown on every platform
        const string core =
            @"PATH|HOME|HOMEPATH|USERPROFILE|NODE_.*|CLAUDE_.*|npm_.*|ANTHROPIC_.*|TEMP|TMP|SHELL|DISABLE_.*|ENABLE_CLAUDEAI_.*|BASH_.*|HTTP_PROXY|HTTPS_PROXY|NO_PROXY|MAX_THINKING_TOKENS|API_TIMEOUT_MS|CLAUDECODE";
        // Platform-specific additions
        string extra = OperatingSystem.IsWindows() ? @"|COMSPEC|WSLENV" : @"|TMPDIR";
        return new Regex($@"^({core}{extra})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    // Pattern matching variables belonging to the "Claude / Anthropic / Node" section.
    // Everything else goes in the "General" section.
    private static readonly Regex ClaudeSectionPattern = MyRegex();

    public EnvironmentEditorViewModel(
        IEnvironmentProvider envProvider,
        ClaudeConfigClientCore? client,
        IReadOnlyList<string>? suggestedEnvVarNames = null)
    {
        _envProvider = envProvider;
        _client = client;
        _suggestedEnvVarNames = suggestedEnvVarNames ?? [];
        AllEntries = [];
        RebuildEntries();

        // Auto-refresh when the Claude Code workspace changes (e.g. another
        // editor wrote the env key, or the file watcher detected an external
        // modification). subscribe to the SDK's
        // Changed event, which forwards workspace.Changed (step 8) so editor
        // direct writes still trigger refresh.
        if (_client != null)
        {
            _client.Changed += OnSdkChanged;
        }
    }

    // -----------------------------------------------------------------------
    // Collections
    // -----------------------------------------------------------------------

    /// <summary>All env var rows (unfiltered), rebuilt by <see cref="RefreshCommand"/>.</summary>
    public ObservableCollection<EnvVarEntry> AllEntries { get; }

    public IEnumerable<EnvVarEntry> FilteredEntries
    {
        get
        {
            // Suggested-only vars are always shown even when ShowAll is false —
            // they are specifically discovered from the schema and represent
            // configurable options the user may not know about.
            IEnumerable<EnvVarEntry> entries = ShowAll
                ? AllEntries
                : AllEntries.Where(e => AllowlistPattern.IsMatch(e.Name) || e.IsFromSuggestion);

            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                entries = entries.Where(e =>
                    e.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                    (e.EffectiveValue ?? "").Contains(FilterText, StringComparison.OrdinalIgnoreCase));
            }

            return entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Filtered entries for the "Claude / Anthropic / Node" section:
    /// variables whose names start with ANTHROPIC_, CLAUDE_, NODE_, etc.
    /// </summary>
    public IEnumerable<EnvVarEntry> ClaudeSectionEntries =>
        FilteredEntries.Where(e => ClaudeSectionPattern.IsMatch(e.Name));

    /// <summary>
    /// Filtered entries for the "General" section:
    /// PATH, SHELL, HOME and other OS-level variables not specific to Claude.
    /// </summary>
    public IEnumerable<EnvVarEntry> GeneralSectionEntries =>
        FilteredEntries.Where(e => !ClaudeSectionPattern.IsMatch(e.Name));

    // -----------------------------------------------------------------------
    // Filter / selection / editing state
    // -----------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredEntries))]
    [NotifyPropertyChangedFor(nameof(ClaudeSectionEntries))]
    [NotifyPropertyChangedFor(nameof(GeneralSectionEntries))]
    private string _filterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredEntries))]
    [NotifyPropertyChangedFor(nameof(ClaudeSectionEntries))]
    [NotifyPropertyChangedFor(nameof(GeneralSectionEntries))]
    private bool _showAll;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveFromScopeCommand))]
    [NotifyPropertyChangedFor(nameof(SelectedEntryDescription))]
    private EnvVarEntry? _selectedEntry;

    /// <summary>
    /// Human-readable description for the selected variable, pulled from the known-vars
    /// dictionary. <c>null</c> when the variable name is not in the known set.
    /// Shown as a label beneath the variable name in the right detail pane.
    /// </summary>
    public string? SelectedEntryDescription =>
        EnvVarTooltipConverter.GetDescription(SelectedEntry?.Name);

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    private string? _editValue;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddNewCommand))]
    private string? _newVarName;

    /// <summary>
    /// False when <see cref="NewVarName"/> contains characters that are illegal in a
    /// POSIX environment variable name — shown as a red inline error in the Add-new row.
    /// True when the field is empty (not-yet-typed) so no error flickers on startup.
    /// </summary>
    [ObservableProperty] private bool _newVarNameIsValid = true;

    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScopeNote))]
    [NotifyPropertyChangedFor(nameof(SelectedScopeInfo))]
    private EnvEditScope _editingScope = EnvEditScope.Claude;

    // -----------------------------------------------------------------------
    // Platform / scope availability
    // -----------------------------------------------------------------------

    private static readonly IReadOnlyDictionary<EnvEditScope, EnvScopeInfo> ScopeInfoMap =
        new Dictionary<EnvEditScope, EnvScopeInfo>
        {
            [EnvEditScope.Claude] = new(EnvEditScope.Claude, "Claude",
                "Claude env dict — saved with the main Save button"),
            [EnvEditScope.User] = new(EnvEditScope.User, "User",
                "Windows user registry (HKCU) — takes effect immediately"),
            [EnvEditScope.Machine] = new(EnvEditScope.Machine, "Machine",
                "Windows system registry (HKLM) — requires admin"),
        };

    public IReadOnlyList<EnvEditScope> AvailableScopes =>
        OperatingSystem.IsWindows()
            ? _client != null
                ? [EnvEditScope.Claude, EnvEditScope.User, EnvEditScope.Machine]
                : [EnvEditScope.User, EnvEditScope.Machine]
            : _client != null
                ? [EnvEditScope.Claude]
                : [];

    /// <summary>Typed list for the scope ComboBox ItemsSource — mapped from <see cref="AvailableScopes"/>.</summary>
    public IReadOnlyList<EnvScopeInfo> AvailableScopeInfos =>
        AvailableScopes.Select(s => ScopeInfoMap[s]).ToList();

    /// <summary>Typed ComboBox binding — keeps <see cref="EditingScope"/> as the source of truth.</summary>
    public EnvScopeInfo? SelectedScopeInfo
    {
        get => ScopeInfoMap.GetValueOrDefault(EditingScope);
        set
        {
            if (value == null || value.Value == EditingScope)
            {
                return;
            }

            EditingScope = value.Value;
            OnPropertyChanged();
        }
    }

    /// <summary>True when the Claude Code SDK client is wired (workspace loaded).</summary>
    public bool HasWorkspace => _client != null;

    public bool IsPersistentEnvAvailable => OperatingSystem.IsWindows();

    /// <summary>Contextual note shown below the scope selector.</summary>
    public string ScopeNote => EditingScope switch
    {
        EnvEditScope.Claude => Strings.ScopeNoteClaude,
        EnvEditScope.User => Strings.ScopeNoteUser,
        EnvEditScope.Machine => Strings.ScopeNoteMachine,
        var _ => string.Empty,
    };

    // -----------------------------------------------------------------------
    // Handlers
    // -----------------------------------------------------------------------

    partial void OnSelectedEntryChanged(EnvVarEntry? value)
    {
        SyncEditValue();
    }

    partial void OnEditingScopeChanged(EnvEditScope value)
    {
        SyncEditValue();
    }

    partial void OnNewVarNameChanged(string? value)
    {
        // Blank input means the user hasn't typed yet — show no error (true = valid).
        NewVarNameIsValid = string.IsNullOrWhiteSpace(value) || IsValidEnvKey(value);
    }

    private void SyncEditValue()
    {
        if (SelectedEntry == null)
        {
            EditValue = null;
            return;
        }

        EditValue = EditingScope switch
        {
            EnvEditScope.Machine => SelectedEntry.MachineValue,
            EnvEditScope.User => SelectedEntry.UserValue,
            EnvEditScope.Claude => SelectedEntry.ClaudeValue,
            var _ => null,
        };
    }

    // -----------------------------------------------------------------------
    // Commands
    // -----------------------------------------------------------------------

    [RelayCommand]
    private void Refresh()
    {
        string? prevName = SelectedEntry?.Name;
        RebuildEntries();

        // Re-select the previously selected entry if it still exists
        if (prevName != null)
        {
            SelectedEntry = AllEntries.FirstOrDefault(e =>
                string.Equals(e.Name, prevName, StringComparison.OrdinalIgnoreCase));
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveEdit()
    {
        if (SelectedEntry == null)
        {
            return;
        }

        Log.Information("[Environment.UserEdit] action=SaveEdit name=\"{Name}\" scope={Scope} newValue={Value}",
            SelectedEntry.Name, EditingScope, EditValue ?? "(empty)");
        if (!ApplyValue(SelectedEntry.Name, EditValue ?? string.Empty))
        {
            return;
        }

        string name = SelectedEntry.Name;
        Refresh();
        SelectedEntry = AllEntries.FirstOrDefault(e =>
            string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        StatusMessage = string.Format(Strings.StatusEnvSavedFmt, name, EditingScope);
    }

    private bool CanSave()
    {
        return SelectedEntry != null && EditValue != null;
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void RemoveFromScope()
    {
        if (SelectedEntry == null)
        {
            return;
        }

        Log.Information("[Environment.UserEdit] action=Remove name=\"{Name}\" scope={Scope}",
            SelectedEntry.Name, EditingScope);
        if (!ApplyValue(SelectedEntry.Name, null))
        {
            return;
        }

        string name = SelectedEntry.Name;
        Refresh();
        SelectedEntry = AllEntries.FirstOrDefault(e =>
            string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        StatusMessage = string.Format(Strings.StatusEnvRemovedFmt, name, EditingScope);
    }

    private bool CanRemove()
    {
        return SelectedEntry != null;
    }

    [RelayCommand(CanExecute = nameof(CanAddNew))]
    private void AddNew()
    {
        string? name = NewVarName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (!IsValidEnvKey(name))
        {
            // Should not reach here (CanAddNew guards this), but log defensively.
            Log.Warning("[Environment] Blocked attempt to add env var with invalid key {Key}", name);
            StatusMessage = string.Format(Strings.StatusEnvKeyInvalidFmt, name);
            return;
        }

        Log.Information("[Environment.UserEdit] action=Add name=\"{Name}\" scope={Scope}",
            name, EditingScope);
        if (!ApplyValue(name, string.Empty))
        {
            return;
        }

        NewVarName = string.Empty;
        // Ensure the new entry is visible regardless of the allowlist filter
        ShowAll = true;
        Refresh();
        SelectedEntry = AllEntries.FirstOrDefault(e =>
            string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        StatusMessage = string.Format(Strings.StatusEnvAddedFmt, name, EditingScope);
    }

    private bool CanAddNew()
    {
        return !string.IsNullOrWhiteSpace(NewVarName) && NewVarNameIsValid;
    }

    // -----------------------------------------------------------------------
    // Navigation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Navigates to a specific environment variable.  The Environment section
    /// opens with its default, unfiltered view — no text filter is applied.
    /// If the named entry is already visible in <see cref="FilteredEntries"/>
    /// it is pre-selected so the detail pane populates immediately; otherwise
    /// the selection is left unchanged.
    /// </summary>
    public void NavigateTo(string varName)
    {
        EnvVarEntry? entry = FilteredEntries.FirstOrDefault(e =>
            string.Equals(e.Name, varName, StringComparison.OrdinalIgnoreCase));
        if (entry != null)
        {
            SelectedEntry = entry;
        }
    }

    // -----------------------------------------------------------------------
    // Workspace change tracking
    // -----------------------------------------------------------------------

    /// <summary>
    /// Called whenever the Claude Code workspace document is mutated (in-memory or by the
    /// file watcher).  Rebuilds the env-var list on the UI thread, restoring the selection.
    /// Skipped when the mutation originated from this VM to avoid a redundant double-rebuild
    /// (the Refresh() call in SaveEdit/RemoveFromScope already handles that case).
    /// </summary>
    private void OnSdkChanged(object? sender, ClientChangedEventArgs e)
    {
        if (_selfWriting)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Rebuild();
        }
        else
        {
            Dispatcher.UIThread.Post(Rebuild);
        }

        return;

        void Rebuild()
        {
            string? prevName = SelectedEntry?.Name;
            RebuildEntries();
            if (prevName != null)
            {
                SelectedEntry = AllEntries.FirstOrDefault(e =>
                    string.Equals(e.Name, prevName, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_client != null)
        {
            _client.Changed -= OnSdkChanged;
        }
    }

    // -----------------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------------

    private void RebuildEntries()
    {
        AllEntries.Clear();

        Dictionary<string, EntryBuilder> builders = new(StringComparer.OrdinalIgnoreCase);

        // Machine env (lowest priority) — Windows only
        if (OperatingSystem.IsWindows())
        {
            IDictionary machine = _envProvider.GetVariables(EnvironmentVariableTarget.Machine);
            foreach (DictionaryEntry kv in machine)
            {
                if (kv.Key is string k && kv.Value is string v)
                {
                    GetOrAdd(k).MachineValue = v;
                }
            }
        }

        // User env — Windows only
        if (OperatingSystem.IsWindows())
        {
            IDictionary user = _envProvider.GetVariables(EnvironmentVariableTarget.User);
            foreach (DictionaryEntry kv in user)
            {
                if (kv.Key is string k && kv.Value is string v)
                {
                    GetOrAdd(k).UserValue = v;
                }
            }
        }

        // Claude env (from workspace — all scopes merged effective)
        if (_client != null)
        {
            LayeredValue layered = _client.GetLayeredValueSnapshot("env");
            if (layered.EffectiveValue is JsonObject envObj)
            {
                foreach (KeyValuePair<string, JsonNode?> kv in envObj)
                {
                    // AsStringOrNull tolerates type-mismatched JSON (e.g. {"PORT": 8080})
                    // by skipping the entry rather than throwing — non-string env values
                    // are not displayable in the textbox-based UI anyway.
                    string? v = kv.Value.AsStringOrNull();
                    if (v != null)
                    {
                        GetOrAdd(kv.Key).ClaudeValue = v;
                    }
                }
            }
        }

        // Process env (highest priority, always read-only)
        IDictionary process = _envProvider.GetVariables(EnvironmentVariableTarget.Process);
        foreach (DictionaryEntry kv in process)
        {
            if (kv.Key is string k && kv.Value is string v)
            {
                GetOrAdd(k).ProcessValue = v;
            }
        }

        // Merge schema-discovered suggestions: variables found in description hints
        // that aren't already present from any real environment layer.
        foreach (string varName in _suggestedEnvVarNames)
        {
            if (!builders.ContainsKey(varName))
            {
                GetOrAdd(varName).IsFromSuggestion = true;
            }
        }

        foreach (EntryBuilder b in builders.Values)
        {
            AllEntries.Add(b.Build());
        }

        OnPropertyChanged(nameof(FilteredEntries));
        OnPropertyChanged(nameof(ClaudeSectionEntries));
        OnPropertyChanged(nameof(GeneralSectionEntries));
        SyncEditValue();
        return;

        EntryBuilder GetOrAdd(string name)
        {
            if (!builders.TryGetValue(name, out EntryBuilder? b))
            {
                builders[name] = b = new EntryBuilder(name);
            }

            return b;
        }
    }

    /// <returns>
    /// <see langword="true"/> when the write succeeded; <see langword="false"/> when it
    /// was rejected (e.g. access denied) — callers must not set a success
    /// <see cref="StatusMessage"/> when this returns <see langword="false"/>.
    /// </returns>
    private bool ApplyValue(string name, string? value)
    {
        switch (EditingScope)
        {
            case EnvEditScope.Claude:
                if (_client != null)
                {
                    WriteToClaudeEnv(name, value);
                }
                else
                {
                    StatusMessage = Strings.StatusCannotSaveClaude;
                }

                break;
            case EnvEditScope.User:
                try
                {
                    _envProvider.SetVariable(name, value, EnvironmentVariableTarget.User);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
                {
                    Log.Error(ex, "[Environment] Access denied writing env var {Name} (target=User)", name);
                    StatusMessage = string.Format(Strings.StatusEnvAccessDeniedFmt, name, ex.Message);
                    return false;
                }

                break;
            case EnvEditScope.Machine:
                try
                {
                    _envProvider.SetVariable(name, value, EnvironmentVariableTarget.Machine);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
                {
                    Log.Error(ex, "[Environment] Access denied writing env var {Name} (target=Machine)", name);
                    StatusMessage = string.Format(Strings.StatusEnvAccessDeniedFmt, name, ex.Message);
                    return false;
                }

                break;
        }

        return true;
    }

    /// <summary>
    /// Reads the current Claude Code <c>env</c> object at User scope, applies the change,
    /// and writes it back (marking the workspace document dirty).
    /// Sets <see cref="_selfWriting"/> while mutating the workspace so the resulting
    /// <see cref="SettingsWorkspace.Changed"/> event does not trigger a redundant rebuild
    /// (the caller's Refresh() already handles that).
    /// </summary>
    private void WriteToClaudeEnv(string name, string? value)
    {
        try
        {
            LayeredValue layered = _client!.GetLayeredValueSnapshot("env");
            JsonObject? existing = layered.GetValueAt(ConfigScope.User) as JsonObject;

            JsonObject updated = new();
            if (existing != null)
            {
                foreach (KeyValuePair<string, JsonNode?> kv in existing)
                {
                    updated[kv.Key] = kv.Value?.DeepClone();
                }
            }

            if (value != null)
            {
                updated[name] = JsonValue.Create(value);
            }
            else
            {
                updated.Remove(name);
            }

            _selfWriting = true;
            try
            {
                if (updated.Count == 0)
                {
                    _client.RemoveValue("env", ConfigScope.User);
                }
                else
                {
                    _client.SetValue("env", updated, ConfigScope.User);
                }
            }
            finally
            {
                _selfWriting = false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Environment] Failed to write environment variable {Name}", name);
            StatusMessage = string.Format(Strings.StatusCannotWriteEnvFmt, ex.Message);
        }
    }

    // -----------------------------------------------------------------------

    private sealed class EntryBuilder(string name)
    {
        public string Name { get; } = name;
        public string? MachineValue { get; set; }
        public string? UserValue { get; set; }
        public string? ClaudeValue { get; set; }
        public string? ProcessValue { get; set; }
        public bool IsFromSuggestion { get; set; }

        public EnvVarEntry Build()
        {
            return new EnvVarEntry
            {
                Name = Name,
                MachineValue = MachineValue,
                UserValue = UserValue,
                ClaudeValue = ClaudeValue,
                ProcessValue = ProcessValue,
                IsFromSuggestion = IsFromSuggestion,
            };
        }
    }

    [GeneratedRegex(@"^(ANTHROPIC_|CLAUDE_|DISABLE_AUTOUPDATER|DISABLE_ERROR_REPORTING|DISABLE_FEEDBACK|DISABLE_TELEMETRY|ENABLE_CLAUDEAI_|NODE_|NPM_|npm_|MAX_THINKING_TOKENS|API_TIMEOUT_MS|BASH_|HTTP_PROXY|HTTPS_PROXY|NO_PROXY|GOOGLE_|GCLOUD_|AWS_|CLAUDECODE).*", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex MyRegex();
}