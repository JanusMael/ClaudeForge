using System.Collections.ObjectModel;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Core.Profile;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk.Dialogs;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// ViewModel for the "Profiles" navigation section.
/// <para>
/// Exposes two independent profile lists — one for Claude Code (CLI) profiles and one
/// for Claude Desktop profiles — together with commands to Apply, Sync, Create, and
/// Delete profiles in each list independently.
/// </para>
/// <para>
/// Interplay with the main window's toolbar is handled through the
/// <see cref="OnProfileApplied"/>, <see cref="OnProfileDeleted"/>,
/// <see cref="OnDesktopProfileApplied"/>, and <see cref="OnDesktopProfileDeleted"/>
/// callbacks wired up by <see cref="MainWindowViewModel"/> when the nav node is built.
/// </para>
/// </summary>
public partial class ProfilesViewModel : ObservableObject
{
    private readonly IDialogService _dialogService;

    // ── CLI callbacks ────────────────────────────────────────────────────────
    /// <summary>
    /// Invoked after a successful CLI Apply with the profile name.
    /// MainWindowViewModel syncs the toolbar ComboBox and reloads the workspace.
    /// </summary>
    public Func<string, Task>? OnProfileApplied { get; set; }

    /// <summary>
    /// Invoked after a successful CLI Delete with the deleted profile name.
    /// MainWindowViewModel refreshes <c>AvailableProfiles</c> and clears the CLI badge.
    /// </summary>
    public Action<string>? OnProfileDeleted { get; set; }

    /// <summary>
    /// Invoked after a successful profile creation with the
    /// new profile's name.  MainWindowViewModel re-queries
    /// <c>AvailableProfileEntries</c> so the toolbar dropdown reflects the
    /// newly-disk-resident profile without requiring a workspace reload or
    /// app restart.  Pre-existing bug: the dropdown only refreshed on
    /// Apply / Delete, so a Create→inspect cycle silently missed the new
    /// entry until next launch.
    /// </summary>
    public Action<string>? OnProfileCreated { get; set; }

    // ── Desktop callbacks ────────────────────────────────────────────────────
    /// <summary>
    /// Invoked after a successful Desktop Apply with the profile name.
    /// MainWindowViewModel updates the Desktop active-profile chiclet.
    /// </summary>
    public Action<string>? OnDesktopProfileApplied { get; set; }

    /// <summary>
    /// Invoked after a successful Desktop Delete.
    /// MainWindowViewModel clears the Desktop chiclet if the deleted profile was active.
    /// </summary>
    public Action<string>? OnDesktopProfileDeleted { get; set; }

    public ProfilesViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        Profiles = new ObservableCollection<ProfileRowViewModel>();
        DesktopProfiles = new ObservableCollection<DesktopProfileRowViewModel>();
    }

    // -----------------------------------------------------------------------
    //  CLI observable state
    // -----------------------------------------------------------------------

    public ObservableCollection<ProfileRowViewModel> Profiles { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private ProfileRowViewModel? _selectedProfile;

    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isBusy;

    // -----------------------------------------------------------------------
    //  Desktop observable state
    // -----------------------------------------------------------------------

    public ObservableCollection<DesktopProfileRowViewModel> DesktopProfiles { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyDesktopCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncDesktopCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteDesktopCommand))]
    private DesktopProfileRowViewModel? _selectedDesktopProfile;

    [ObservableProperty] private string? _desktopStatusMessage;
    [ObservableProperty] private bool _isDesktopBusy;

    /// <summary>True when Claude Desktop appears to be installed on this machine.</summary>
    public bool DesktopAvailable => PlatformPaths.IsDesktopInstalled;

    // -----------------------------------------------------------------------
    //  Commands — shared
    // -----------------------------------------------------------------------

    [RelayCommand]
    public void Refresh()
    {
        RefreshCli();
        RefreshDesktop();
    }

    // -----------------------------------------------------------------------
    //  Commands — CLI profiles
    // -----------------------------------------------------------------------

    private void RefreshCli()
    {
        string? prevName = SelectedProfile?.Name;
        IReadOnlyList<ProfileInfo> rows = ProfileEngine.DiscoverProfiles();
        Profiles.Clear();
        foreach (ProfileInfo info in rows)
        {
            Profiles.Add(new ProfileRowViewModel(info));
        }

        SelectedProfile = prevName != null
            ? Profiles.FirstOrDefault(p => p.Name == prevName)
            : Profiles.FirstOrDefault(p => p.IsCliActive) ?? Profiles.FirstOrDefault();

        StatusMessage = Profiles.Count == 0
            ? Strings.StatusNoCliProfiles
            : null;
    }

    // ---- New Profile ----

    [RelayCommand]
    private async Task NewProfileAsync()
    {
        string? name = await _dialogService.ShowInputAsync(
            "New Profile",
            "Enter a name for the new profile:",
            placeholder: "e.g. work, personal, home");

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (!IsValidProfileName(name, out string error))
        {
            await _dialogService.ShowAlertAsync("Invalid Name", DialogMessage.Plain(error));
            return;
        }

        Log.Information("[Profiles.Command] action=NewCli name=\"{Name}\"", name);

        string profileDir = Path.Combine(PlatformPaths.ProfilesDirectory, name);
        if (Directory.Exists(profileDir))
        {
            DialogMessage existsMsg = DialogMessage.Builder()
                                                   .Text("A profile named '").Bold(name)
                                                   .Text("' already exists. Choose a different name.")
                                                   .Build();
            await _dialogService.ShowAlertAsync("Profile Exists", existsMsg);
            return;
        }

        IsBusy = true;
        StatusMessage = string.Format(Strings.StatusCreatingProfileFmt, name);
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            await ProfileEngine.CreateFromLiveAsync(name, cts.Token);
            StatusMessage = string.Format(Strings.StatusProfileCreatedFmt, name);
            Refresh();

            // Select the new profile.
            SelectedProfile = Profiles.FirstOrDefault(p => p.Name == name);

            // Notify MWVM so the toolbar Editing-Profile dropdown re-queries
            // AvailableProfileEntries.  Without this, the dropdown only
            // picked up the new entry after the next Apply/Delete or app
            // restart (CachyOS smoke).
            OnProfileCreated?.Invoke(name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Profiles] Could not create profile {Name}", name);
            StatusMessage = null;
            await _dialogService.ShowAlertAsync("Error",
                DialogMessage.Plain($"Could not create profile: {ex.Message}"),
                DialogCategory.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Apply profile → live ----

    private bool CanApply()
    {
        return SelectedProfile != null && !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (SelectedProfile == null)
        {
            return;
        }

        string name = SelectedProfile.Name;
        Log.Information("[Profiles.Command] action=ApplyCli name=\"{Name}\"", name);

        DialogMessage msg = DialogMessage.Builder()
                                         .Text("Apply profile '").Bold(name).Text("' to Claude Code's live settings?\n\n")
                                         .Text("This will overwrite ").Path("~/.claude/settings.json").Text(" (and ")
                                         .Path("CLAUDE.md").Text(" / MCP servers if present in the profile). Claude Code will ")
                                         .Text("pick up the new settings on its next run.\n\n")
                                         .Text(
                                             "The current live settings will be auto-saved back to the currently-active profile ")
                                         .Text("first, so no external edits are lost.")
                                         .Build();
        bool? confirmed = await _dialogService.ShowConfirmAsync(
            "Apply Profile", msg, confirmLabel: "Apply");

        // Binary yes/no — both Cancel (false) and X (null) abort.
        if (confirmed != true)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = string.Format(Strings.StatusApplyingProfileFmt, name);
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            await ProfileEngine.ApplyProfileToLiveAsync(name, autoSync: true, cts.Token);
            StatusMessage = string.Format(Strings.StatusProfileAppliedFmt, name);
            Refresh();

            // Notify the main window to sync the toolbar ComboBox and reload the workspace.
            if (OnProfileApplied != null)
            {
                await OnProfileApplied(name);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Strings.StatusApplyTimedOut;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Profiles] Apply failed for {Name}", name);
            StatusMessage = null;
            await _dialogService.ShowAlertAsync("Apply Failed",
                DialogMessage.Plain(ex.Message), DialogCategory.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Sync live → profile ----

    private bool CanSync()
    {
        return SelectedProfile != null && !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncAsync()
    {
        if (SelectedProfile == null)
        {
            return;
        }

        string name = SelectedProfile.Name;
        Log.Information("[Profiles.Command] action=SyncCli name=\"{Name}\"", name);

        DialogMessage syncMsg = DialogMessage.Builder()
                                             .Text("Update profile '").Bold(name)
                                             .Text("' with the current live Claude Code files?\n\n")
                                             .Text("This copies ").Path("~/.claude/settings.json").Text(" (and ")
                                             .Path("CLAUDE.md").Text(" / mcpServers if present) into the profile directory, ")
                                             .Text("overwriting what was previously stored.")
                                             .Build();
        bool? confirmed = await _dialogService.ShowConfirmAsync(
            "Sync from Live", syncMsg, confirmLabel: "Sync");

        // Binary yes/no — both Cancel (false) and X (null) abort.
        if (confirmed != true)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = string.Format(Strings.StatusSyncingProfileFmt, name);
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            await ProfileEngine.SyncFromLiveAsync(name, cts.Token);
            StatusMessage = string.Format(Strings.StatusProfileSyncedFmt, name);
            Refresh();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Strings.StatusSyncTimedOut;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Profiles] Sync failed for {Name}", name);
            StatusMessage = null;
            await _dialogService.ShowAlertAsync("Sync Failed",
                DialogMessage.Plain(ex.Message), DialogCategory.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Delete ----

    private bool CanDelete()
    {
        return SelectedProfile != null && !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (SelectedProfile == null || IsBusy)
        {
            return;
        }

        string name = SelectedProfile.Name;
        Log.Information("[Profiles.Command] action=DeleteCli name=\"{Name}\"", name);

        string? cliActive = ProfileEngine.ReadCurrentProfileName();
        bool isCliActive = string.Equals(cliActive, name, StringComparison.OrdinalIgnoreCase);

        // Set IsBusy BEFORE the first await so a rapid second click is blocked by CanDelete
        // even while the confirmation dialog is waiting for user input.
        IsBusy = true;
        try
        {
            // Build the destructive prompt with the active-profile escalation as
            // distinct prose runs rather than baked into a single string so the
            // path span gets its monospace + clickable treatment.
            DialogMessageBuilder b = DialogMessage.Builder()
                                                  .Text("Permanently delete profile '").Bold(name).Text("' and all its stored files?");
            if (isCliActive)
            {
                b.Text("\n\n").Bold($"'{name}'")
                 .Text(" is the currently CLI-active profile. Deleting it will clear ")
                 .Text("the CLI activation pointer — Claude Code will continue using the same live ")
                 .Path("~/.claude/settings.json")
                 .Text(" file, but no profile will be marked as active.");
            }

            b.Text("\n\nThis cannot be undone.");
            DialogMessage msg = b.Build();
            bool? confirmed = await _dialogService.ShowConfirmAsync(
                "Delete Profile", msg, DialogCategory.Destructive, confirmLabel: "Delete");

            // Binary yes/no — both Cancel (false) and X (null) abort.
            if (confirmed != true)
            {
                return;
            }

            string profileDir = Path.Combine(PlatformPaths.ProfilesDirectory, name);

            if (Directory.Exists(profileDir))
            {
                Directory.Delete(profileDir, recursive: true);
            }

            // Clear the CLI-active pointer if it pointed here.
            if (isCliActive)
            {
                ProfileEngine.WriteCurrentProfileName(null);
            }

            StatusMessage = string.Format(Strings.StatusProfileDeletedFmt, name);
            Refresh();
            OnProfileDeleted?.Invoke(name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Profiles] Could not delete profile {Name}", name);
            await _dialogService.ShowAlertAsync("Error",
                DialogMessage.Plain($"Could not delete profile: {ex.Message}"),
                DialogCategory.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // -----------------------------------------------------------------------
    //  Commands — Export / Import (claudectx-compatible JSON)
    //
    //  2026-05-07.  Per-profile export and import using the same single-file
    //  JSON format the claudectx CLI tool uses.  Profile data is converted
    //  via ProfileEngine.ExportProfileAsync / ImportProfileAsync; the GUI
    //  only owns the file-picker / status-message wiring.  Keeps the
    //  SDK-first separation: all logic lives in ClaudeForge.Core.Profile.
    // -----------------------------------------------------------------------

    // ---- Export ----

    private bool CanExport()
    {
        return SelectedProfile != null && !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (SelectedProfile == null || IsBusy)
        {
            return;
        }

        string name = SelectedProfile.Name;
        Log.Information("[Profiles.Command] action=ExportCli name=\"{Name}\"", name);

        // Set IsBusy BEFORE the first await — same race-prevention pattern
        // as Delete.  The file-save dialog is awaitable; without this guard
        // a second Export click during the dialog could double-fire.
        IsBusy = true;
        try
        {
            string? dest = await _dialogService.PickSaveFileAsync(
                title: Strings.ButtonExportProfile,
                defaultFileName: $"{name}.json",
                filters: [new FilePickerFilter(Strings.FilePickerProfileJson, ["json"])]);
            if (string.IsNullOrEmpty(dest))
            {
                return; // user cancelled
            }

            // 30-second timeout matches the NewProfile / Apply / Sync
            // pattern elsewhere in this VM — guards against a hung
            // network-mounted destination file system or pathological
            // CLAUDE.md size leaving the UI stuck with IsBusy=true.
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            await ProfileEngine.ExportProfileAsync(name, dest, cts.Token);
            StatusMessage = string.Format(Strings.StatusProfileExportedFmt, name, dest);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Profiles] Export failed for {Name}", name);
            await _dialogService.ShowAlertAsync(
                Strings.DialogTitleExportProfileFailed,
                DialogMessage.Plain(ex.Message),
                DialogCategory.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Import ----

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (IsBusy)
        {
            return;
        }

        Log.Information("[Profiles.Command] action=ImportCli");
        IsBusy = true;
        try
        {
            string? src = await _dialogService.PickFileAsync(
                title: Strings.ButtonImportProfile,
                filters: [new FilePickerFilter(Strings.FilePickerProfileJson, ["json"])]);
            if (string.IsNullOrEmpty(src))
            {
                return; // user cancelled
            }

            // overrideName=null → use the embedded ExportedProfile.Name.
            // ProfileEngine.ImportProfileAsync validates the name is a
            // single path-segment that resolves under ProfilesDirectory
            // (path-traversal guard — see ResolveProfileDirSecurely).
            // If the target already exists, ProfileEngine throws
            // IOException with a "profile X already exists" message —
            // surface as-is.
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            string landed = await ProfileEngine.ImportProfileAsync(src, overrideName: null, cts.Token);
            StatusMessage = string.Format(Strings.StatusProfileImportedFmt, landed);
            Refresh();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Profiles] Import failed");
            await _dialogService.ShowAlertAsync(
                Strings.DialogTitleImportProfileFailed,
                DialogMessage.Plain(ex.Message),
                DialogCategory.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // -----------------------------------------------------------------------
    //  Commands — Desktop profiles
    // -----------------------------------------------------------------------

    private void RefreshDesktop()
    {
        string? prevName = SelectedDesktopProfile?.Name;
        IReadOnlyList<DesktopProfileInfo> rows = ProfileEngine.DiscoverDesktopProfiles();
        DesktopProfiles.Clear();
        foreach (DesktopProfileInfo info in rows)
        {
            DesktopProfiles.Add(new DesktopProfileRowViewModel(info));
        }

        SelectedDesktopProfile = prevName != null
            ? DesktopProfiles.FirstOrDefault(p => p.Name == prevName)
            : DesktopProfiles.FirstOrDefault(p => p.IsActive) ?? DesktopProfiles.FirstOrDefault();

        DesktopStatusMessage = DesktopProfiles.Count == 0
            ? Strings.StatusNoDesktopProfiles
            : null;
    }

    [RelayCommand]
    private async Task NewDesktopProfileAsync()
    {
        string? name = await _dialogService.ShowInputAsync(
            "New Desktop Profile",
            "Enter a name for the new Desktop profile:",
            placeholder: "e.g. work, personal, home");

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        Log.Information("[Profiles.Command] action=NewDesktop name=\"{Name}\"", name);

        if (!IsValidProfileName(name, out string error))
        {
            await _dialogService.ShowAlertAsync("Invalid Name", DialogMessage.Plain(error));
            return;
        }

        string profileDir = Path.Combine(
            PlatformPaths.DesktopProfilesDirectory, name);
        if (Directory.Exists(profileDir))
        {
            DialogMessage existsMsg = DialogMessage.Builder()
                                                   .Text("A Desktop profile named '").Bold(name)
                                                   .Text("' already exists. Choose a different name.")
                                                   .Build();
            await _dialogService.ShowAlertAsync("Profile Exists", existsMsg);
            return;
        }

        IsDesktopBusy = true;
        DesktopStatusMessage = string.Format(Strings.StatusCreatingDesktopProfileFmt, name);
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            await ProfileEngine.CreateDesktopProfileFromLiveAsync(name, cts.Token);
            DesktopStatusMessage = string.Format(Strings.StatusDesktopProfileCreatedFmt, name);
            RefreshDesktop();
            SelectedDesktopProfile = DesktopProfiles.FirstOrDefault(p => p.Name == name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DesktopProfiles] Could not create profile {Name}", name);
            DesktopStatusMessage = null;
            await _dialogService.ShowAlertAsync("Error",
                DialogMessage.Plain($"Could not create Desktop profile: {ex.Message}"),
                DialogCategory.Error);
        }
        finally
        {
            IsDesktopBusy = false;
        }
    }

    private bool CanApplyDesktop()
    {
        return SelectedDesktopProfile != null && !IsDesktopBusy;
    }

    [RelayCommand(CanExecute = nameof(CanApplyDesktop))]
    private async Task ApplyDesktopAsync()
    {
        if (SelectedDesktopProfile == null)
        {
            return;
        }

        Log.Information("[Profiles.Command] action=ApplyDesktop name=\"{Name}\"", SelectedDesktopProfile.Name);
        string name = SelectedDesktopProfile.Name;

        DialogMessage applyMsg = DialogMessage.Builder()
                                              .Text("Apply Desktop profile '").Bold(name)
                                              .Text("' to Claude Desktop's live config?\n\n")
                                              .Text("This will overwrite the Desktop config file. Claude Desktop will use ")
                                              .Text("the new config on its next launch.\n\n")
                                              .Text("The current live config will be auto-saved back to the currently-active ")
                                              .Text("Desktop profile first, so no edits are lost.")
                                              .Build();
        bool? confirmed = await _dialogService.ShowConfirmAsync(
            "Apply Desktop Profile", applyMsg,
            confirmLabel: "Apply");

        // Binary yes/no — both Cancel (false) and X (null) abort.
        if (confirmed != true)
        {
            return;
        }

        IsDesktopBusy = true;
        DesktopStatusMessage = string.Format(Strings.StatusApplyingDesktopProfileFmt, name);
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            await ProfileEngine.ApplyDesktopProfileToLiveAsync(name, autoSync: true, cts.Token);
            DesktopStatusMessage = string.Format(Strings.StatusDesktopProfileAppliedFmt, name);
            RefreshDesktop();
            OnDesktopProfileApplied?.Invoke(name);
        }
        catch (OperationCanceledException)
        {
            DesktopStatusMessage = Strings.StatusApplyTimedOut;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DesktopProfiles] Apply failed for {Name}", name);
            DesktopStatusMessage = null;
            await _dialogService.ShowAlertAsync("Apply Failed",
                DialogMessage.Plain(ex.Message), DialogCategory.Error);
        }
        finally
        {
            IsDesktopBusy = false;
        }
    }

    private bool CanSyncDesktop()
    {
        return SelectedDesktopProfile != null && !IsDesktopBusy;
    }

    [RelayCommand(CanExecute = nameof(CanSyncDesktop))]
    private async Task SyncDesktopAsync()
    {
        if (SelectedDesktopProfile == null)
        {
            return;
        }

        Log.Information("[Profiles.Command] action=SyncDesktop name=\"{Name}\"", SelectedDesktopProfile.Name);
        string name = SelectedDesktopProfile.Name;

        DialogMessage syncMsg = DialogMessage.Builder()
                                             .Text("Update Desktop profile '").Bold(name)
                                             .Text("' with the current live Claude Desktop config?\n\n")
                                             .Text("This overwrites what was previously stored in the profile.")
                                             .Build();
        bool? confirmed = await _dialogService.ShowConfirmAsync(
            "Sync Desktop Profile from Live", syncMsg,
            confirmLabel: "Sync");

        // Binary yes/no — both Cancel (false) and X (null) abort.
        if (confirmed != true)
        {
            return;
        }

        IsDesktopBusy = true;
        DesktopStatusMessage = string.Format(Strings.StatusSyncingDesktopProfileFmt, name);
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            await ProfileEngine.SyncDesktopFromLiveAsync(name, cts.Token);
            DesktopStatusMessage = string.Format(Strings.StatusDesktopProfileSyncedFmt, name);
            RefreshDesktop();
        }
        catch (OperationCanceledException)
        {
            DesktopStatusMessage = Strings.StatusSyncTimedOut;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DesktopProfiles] Sync failed for {Name}", name);
            DesktopStatusMessage = null;
            await _dialogService.ShowAlertAsync("Sync Failed",
                DialogMessage.Plain(ex.Message), DialogCategory.Error);
        }
        finally
        {
            IsDesktopBusy = false;
        }
    }

    private bool CanDeleteDesktop()
    {
        return SelectedDesktopProfile != null && !IsDesktopBusy;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteDesktop))]
    private async Task DeleteDesktopAsync()
    {
        if (SelectedDesktopProfile != null)
        {
            Log.Information("[Profiles.Command] action=DeleteDesktop name=\"{Name}\"", SelectedDesktopProfile.Name);
        }

        if (SelectedDesktopProfile == null || IsDesktopBusy)
        {
            return;
        }

        string name = SelectedDesktopProfile.Name;

        bool isActive = string.Equals(
            ProfileEngine.ReadCurrentDesktopProfileName(), name, StringComparison.OrdinalIgnoreCase);

        IsDesktopBusy = true;
        try
        {
            DialogMessageBuilder b = DialogMessage.Builder()
                                                  .Text("Permanently delete Desktop profile '").Bold(name).Text("'?");
            if (isActive)
            {
                b.Text("\n\n").Bold($"'{name}'")
                 .Text(" is the currently active Desktop profile. Deleting it will ")
                 .Text("clear the activation pointer — the live Desktop config is not affected.");
            }

            b.Text("\n\nThis cannot be undone.");
            bool? confirmed = await _dialogService.ShowConfirmAsync(
                "Delete Desktop Profile", b.Build(), DialogCategory.Destructive,
                confirmLabel: "Delete");

            // Binary yes/no — both Cancel (false) and X (null) abort.
            if (confirmed != true)
            {
                return;
            }

            string profileDir = Path.Combine(
                PlatformPaths.DesktopProfilesDirectory, name);

            if (Directory.Exists(profileDir))
            {
                Directory.Delete(profileDir, recursive: true);
            }

            if (isActive)
            {
                ProfileEngine.WriteCurrentDesktopProfileName(null);
            }

            DesktopStatusMessage = string.Format(Strings.StatusDesktopProfileDeletedFmt, name);
            RefreshDesktop();
            OnDesktopProfileDeleted?.Invoke(name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DesktopProfiles] Could not delete profile {Name}", name);
            await _dialogService.ShowAlertAsync("Error",
                DialogMessage.Plain($"Could not delete Desktop profile: {ex.Message}"),
                DialogCategory.Error);
        }
        finally
        {
            IsDesktopBusy = false;
        }
    }

    // -----------------------------------------------------------------------
    //  Validation
    // -----------------------------------------------------------------------

    private static bool IsValidProfileName(string name, out string error)
    {
        if (name.Equals("(global)", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("global", StringComparison.OrdinalIgnoreCase))
        {
            error = $"'{name}' is a reserved name. Please choose a different profile name.";
            return false;
        }

        if (name is "." or "..")
        {
            error = "Profile name cannot be '.' or '..'.";
            return false;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "Profile name contains characters that are not allowed in file names.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

// ---------------------------------------------------------------------------

/// <summary>Row ViewModel for a single CLI profile in the DataGrid.</summary>
public sealed partial class ProfileRowViewModel : ObservableObject
{
    public ProfileRowViewModel(ProfileInfo info)
    {
        Name = info.Name;
        HasSettings = info.HasSettings;
        HasClaudeMd = info.HasClaudeMd;
        HasMcp = info.HasMcp;
        IsCliActive = info.IsCliActive;
    }

    public string Name { get; }
    public bool HasSettings { get; }
    public bool HasClaudeMd { get; }
    public bool HasMcp { get; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CliActiveLabel))]
    private bool _isCliActive;

    /// <summary>Human-readable label — "global" won't appear here since profiles never use that name.</summary>
    public string CliActiveLabel => IsCliActive ? "● CLI" : string.Empty;
}

// ---------------------------------------------------------------------------

/// <summary>Row ViewModel for a single Desktop profile in the DataGrid.</summary>
public sealed partial class DesktopProfileRowViewModel : ObservableObject
{
    public DesktopProfileRowViewModel(DesktopProfileInfo info)
    {
        Name = info.Name;
        IsActive = info.IsActive;
    }

    public string Name { get; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ActiveLabel))]
    private bool _isActive;

    /// <summary>Human-readable active label.</summary>
    public string ActiveLabel => IsActive ? "● Desktop" : string.Empty;
}