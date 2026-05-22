using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Core.Profile;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// ProfilesViewModel coverage. Targets the predicate
/// helpers (Can*), Refresh, and the input-validation branches of
/// NewProfileAsync / DeleteAsync.  The ProfileEngine static methods read
/// and write under <see cref="PlatformPaths.ProfilesDirectory"/>; tests
/// scope every interaction to a per-test sandbox via
/// <see cref="PlatformPaths.TestUserProfileOverride"/>.
///
/// What this file deliberately does NOT cover:
///   • ProfileEngine.CreateFromLiveAsync / ApplyProfileToLiveAsync
///     internal mechanics — those are exercised by ProfileEngineAsyncTests.
///   • The full Apply / Sync / Delete async happy paths (the deferred
///     "lower-value gaps" — async state machines that need fake ProfileEngine
///     plumbing not yet wired through the SDK).  Lifting the predicate +
///     dialog-flow paths is the bang-for-buck win identified in the
///     coverage doc.
/// </summary>
[TestClass]
public sealed class ProfilesViewModelTests
{
    private string _sandbox = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "pvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_sandbox, ".claude"));
        PlatformPaths.TestUserProfileOverride = _sandbox;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch
        {
            /* best-effort */
        }
    }

    private static ProfilesViewModel NewVm(StubDialogService? dlg = null)
    {
        return new ProfilesViewModel(dlg ?? new StubDialogService());
    }

    private void CreateProfile(string name, bool withSettings = true, bool withClaudeMd = false, bool withMcp = false)
    {
        string dir = Path.Combine(_sandbox, ".claude", "profiles", name);
        Directory.CreateDirectory(dir);
        if (withSettings)
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{}");
        }

        if (withClaudeMd)
        {
            File.WriteAllText(Path.Combine(dir, "CLAUDE.md"), "# profile");
        }

        if (withMcp)
        {
            File.WriteAllText(Path.Combine(dir, "mcp.json"), "{}");
        }
    }

    // ── Constructor argument validation ───────────────────────────────────

    [TestMethod]
    public void Constructor_NullDialogService_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => new ProfilesViewModel(null!));
    }

    // ── DesktopAvailable property surface ─────────────────────────────────

    [TestMethod]
    public void DesktopAvailable_TracksPlatformPathsIsDesktopInstalled()
    {
        ProfilesViewModel vm = NewVm();
        // The sandbox doesn't put a Desktop config there, so on a fresh
        // override DesktopAvailable should be false. Only assertion: the
        // property is non-null and matches PlatformPaths' decision.
        Assert.AreEqual(PlatformPaths.IsDesktopInstalled, vm.DesktopAvailable);
    }

    // ── Refresh — empty state ─────────────────────────────────────────────

    [TestMethod]
    public void Refresh_EmptyProfilesDirectory_PopulatesStatusMessage()
    {
        ProfilesViewModel vm = NewVm();
        vm.Refresh();

        Assert.AreEqual(0, vm.Profiles.Count);
        Assert.AreEqual(
            Strings.StatusNoCliProfiles,
            vm.StatusMessage,
            "Empty profile directory must surface the 'no profiles' status string.");
    }

    [TestMethod]
    public void Refresh_PopulatesRowsFromDisk()
    {
        CreateProfile("work", withSettings: true, withClaudeMd: false, withMcp: true);
        CreateProfile("personal", withSettings: true, withClaudeMd: true, withMcp: false);

        ProfilesViewModel vm = NewVm();
        vm.Refresh();

        Assert.AreEqual(2, vm.Profiles.Count);
        ProfileRowViewModel work = vm.Profiles.Single(p => p.Name == "work");
        Assert.IsTrue(work.HasSettings);
        Assert.IsFalse(work.HasClaudeMd);
        Assert.IsTrue(work.HasMcp);

        ProfileRowViewModel personal = vm.Profiles.Single(p => p.Name == "personal");
        Assert.IsTrue(personal.HasClaudeMd);
        Assert.IsFalse(personal.HasMcp);
    }

    [TestMethod]
    public void Refresh_PreservesSelection_WhenNamedProfileStillExists()
    {
        CreateProfile("alpha");
        CreateProfile("beta");
        ProfilesViewModel vm = NewVm();
        vm.Refresh();
        vm.SelectedProfile = vm.Profiles.Single(p => p.Name == "beta");

        // A second Refresh (e.g. after creating a new profile) must
        // preserve the user's selection by name, not by index.
        CreateProfile("gamma");
        vm.Refresh();

        Assert.AreEqual(3, vm.Profiles.Count);
        Assert.AreEqual("beta", vm.SelectedProfile?.Name,
            "Refresh must restore selection by name when the named profile still exists.");
    }

    [TestMethod]
    public void Refresh_FirstTime_SelectsActiveProfile_WhenSet()
    {
        CreateProfile("alpha");
        CreateProfile("beta");
        ProfileEngine.WriteCurrentProfileName("beta");

        ProfilesViewModel vm = NewVm();
        vm.Refresh();

        // No prior selection → fallback chain prefers IsCliActive over the
        // first row in disk order.  This matches the user expectation that
        // "the active profile should be selected when I open the page".
        Assert.AreEqual("beta", vm.SelectedProfile?.Name,
            "First Refresh must prefer the CLI-active profile over the first "
            + "profile in disk-walk order.");
    }

    [TestMethod]
    public void Refresh_FirstTime_FallsBackToFirstProfile_WhenNoActive()
    {
        // Two profiles, neither marked active. The fallback chain ends at
        // FirstOrDefault() which returns the first row in the order
        // ProfileEngine returns from DiscoverProfiles (typically alphabetical).
        CreateProfile("alpha");
        CreateProfile("beta");

        ProfilesViewModel vm = NewVm();
        vm.Refresh();

        Assert.IsNotNull(vm.SelectedProfile,
            "When no profile is CLI-active, Refresh still selects SOMETHING from "
            + "the available profiles so the page isn't blank on first open.");
    }

    // ── CanApply / CanSync / CanDelete predicates ────────────────────────

    [TestMethod]
    public void CanApply_FalseWhenNoSelection()
    {
        ProfilesViewModel vm = NewVm();
        Assert.IsFalse(vm.ApplyCommand.CanExecute(null));
    }

    [TestMethod]
    public void CanApply_TrueWhenProfileSelected_AndNotBusy()
    {
        CreateProfile("work");
        ProfilesViewModel vm = NewVm();
        vm.Refresh();
        Assert.IsTrue(vm.ApplyCommand.CanExecute(null));
    }

    [TestMethod]
    public void CanApply_FalseWhenBusy_EvenWithSelection()
    {
        CreateProfile("work");
        ProfilesViewModel vm = NewVm();
        vm.Refresh();
        vm.IsBusy = true;
        Assert.IsFalse(vm.ApplyCommand.CanExecute(null));
    }

    [TestMethod]
    public void CanSync_FalseWhenNoSelection()
    {
        Assert.IsFalse(NewVm().SyncCommand.CanExecute(null));
    }

    [TestMethod]
    public void CanDelete_FalseWhenNoSelection()
    {
        Assert.IsFalse(NewVm().DeleteCommand.CanExecute(null));
    }

    [TestMethod]
    public void CanDelete_TrueWithSelection_FalseWhenBusy()
    {
        CreateProfile("work");
        ProfilesViewModel vm = NewVm();
        vm.Refresh();
        Assert.IsTrue(vm.DeleteCommand.CanExecute(null));
        vm.IsBusy = true;
        Assert.IsFalse(vm.DeleteCommand.CanExecute(null));
    }

    [TestMethod]
    public void CanApplyDesktop_FalseWhenNoDesktopSelection()
    {
        Assert.IsFalse(NewVm().ApplyDesktopCommand.CanExecute(null));
    }

    [TestMethod]
    public void CanDeleteDesktop_FalseWhenNoDesktopSelection()
    {
        Assert.IsFalse(NewVm().DeleteDesktopCommand.CanExecute(null));
    }

    // ── NewProfileAsync — input-validation branches ──────────────────────

    [TestMethod]
    public async Task NewProfileAsync_UserCancelsInput_NoOps()
    {
        StubDialogService dlg = new() { InputReturns = null };
        ProfilesViewModel vm = NewVm(dlg);
        await vm.NewProfileCommand.ExecuteAsync(null);

        Assert.AreEqual(1, dlg.InputCalls);
        Assert.AreEqual(0, dlg.AlertCalls,
            "Cancelling the input dialog must not open an alert.");
        Assert.AreEqual(0, vm.Profiles.Count);
    }

    [TestMethod]
    public async Task NewProfileAsync_BlankName_NoOpsSilently()
    {
        StubDialogService dlg = new() { InputReturns = "   " };
        ProfilesViewModel vm = NewVm(dlg);
        await vm.NewProfileCommand.ExecuteAsync(null);

        Assert.AreEqual(0, dlg.AlertCalls,
            "Whitespace-only name short-circuits the same way Cancel does.");
    }

    [TestMethod]
    public async Task NewProfileAsync_ReservedName_OpensAlert()
    {
        StubDialogService dlg = new() { InputReturns = "(global)" };
        ProfilesViewModel vm = NewVm(dlg);
        await vm.NewProfileCommand.ExecuteAsync(null);

        Assert.AreEqual(1, dlg.AlertCalls);
        Assert.AreEqual("Invalid Name", dlg.LastAlertTitle);
    }

    [TestMethod]
    public async Task NewProfileAsync_GlobalAlias_AlsoReserved()
    {
        StubDialogService dlg = new() { InputReturns = "GLOBAL" };
        ProfilesViewModel vm = NewVm(dlg);
        await vm.NewProfileCommand.ExecuteAsync(null);

        Assert.AreEqual(1, dlg.AlertCalls,
            "'global' (any casing) must be rejected as reserved.");
    }

    [TestMethod]
    public async Task NewProfileAsync_DotName_OpensAlert()
    {
        StubDialogService dlg = new() { InputReturns = "." };
        ProfilesViewModel vm = NewVm(dlg);
        await vm.NewProfileCommand.ExecuteAsync(null);

        Assert.AreEqual(1, dlg.AlertCalls);
    }

    [TestMethod]
    public async Task NewProfileAsync_DuplicateName_OpensAlert()
    {
        CreateProfile("work");
        StubDialogService dlg = new() { InputReturns = "work" };
        ProfilesViewModel vm = NewVm(dlg);
        vm.Refresh();
        await vm.NewProfileCommand.ExecuteAsync(null);

        Assert.AreEqual(1, dlg.AlertCalls);
        Assert.AreEqual("Profile Exists", dlg.LastAlertTitle);
    }

    // ── DeleteAsync — confirmation branch ────────────────────────────────

    [TestMethod]
    public async Task DeleteAsync_UserCancelsConfirm_NothingDeleted()
    {
        CreateProfile("work");
        StubDialogService dlg = new() { ConfirmReturns = false };
        ProfilesViewModel vm = NewVm(dlg);
        vm.Refresh();
        Assert.IsTrue(Directory.Exists(Path.Combine(_sandbox, ".claude", "profiles", "work")));

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.AreEqual(1, dlg.ConfirmCalls);
        Assert.IsTrue(Directory.Exists(Path.Combine(_sandbox, ".claude", "profiles", "work")),
            "Cancelling the destructive confirm must NOT delete the profile.");
    }

    [TestMethod]
    public async Task DeleteAsync_ConfirmedWhenSelected_RemovesProfileDir_AndFiresCallback()
    {
        CreateProfile("work");
        StubDialogService dlg = new() { ConfirmReturns = true };
        ProfilesViewModel vm = NewVm(dlg);

        List<string> deletedNotifications = new();
        vm.OnProfileDeleted = name => deletedNotifications.Add(name);

        vm.Refresh();
        Assert.AreEqual(1, vm.Profiles.Count);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.IsFalse(
            Directory.Exists(Path.Combine(_sandbox, ".claude", "profiles", "work")),
            "Confirmed delete must remove the profile directory.");
        CollectionAssert.AreEqual(new[] { "work" }, deletedNotifications);
        Assert.AreEqual(0, vm.Profiles.Count,
            "Refresh after delete must rebuild the list with the deleted profile gone.");
    }

    [TestMethod]
    public async Task DeleteAsync_ActiveProfile_ClearsCliPointer()
    {
        CreateProfile("work");
        // Mark "work" as the currently-active CLI profile.
        ProfileEngine.WriteCurrentProfileName("work");
        Assert.AreEqual("work", ProfileEngine.ReadCurrentProfileName());

        StubDialogService dlg = new() { ConfirmReturns = true };
        ProfilesViewModel vm = NewVm(dlg);
        vm.Refresh();

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.IsNull(ProfileEngine.ReadCurrentProfileName(),
            "Deleting the CLI-active profile must clear the activation pointer "
            + "so Claude Code is not pointing at a deleted directory.");
    }

    // ── DesktopProfile predicates ────────────────────────────────────────

    [TestMethod]
    public void CanSyncDesktop_FalseWhenNoSelection()
    {
        Assert.IsFalse(NewVm().SyncDesktopCommand.CanExecute(null));
    }

    [TestMethod]
    public void RefreshDesktop_EmptyDir_PopulatesStatusMessage()
    {
        ProfilesViewModel vm = NewVm();
        vm.Refresh();
        Assert.AreEqual(0, vm.DesktopProfiles.Count);
        Assert.AreEqual(
            Strings.StatusNoDesktopProfiles,
            vm.DesktopStatusMessage);
    }

    // ── Test doubles ─────────────────────────────────────────────────────

    private sealed class StubDialogService : IDialogService
    {
        public string? InputReturns { get; set; }
        public bool ConfirmReturns { get; set; }
        public string? PickSaveFileReturns { get; set; } // for ExportAsync tests
        public string? PickFileReturns { get; set; } // for ImportAsync tests
        public int InputCalls { get; private set; }
        public int ConfirmCalls { get; private set; }
        public int AlertCalls { get; private set; }
        public int PickSaveFileCalls { get; private set; }
        public int PickFileCalls { get; private set; }
        public string? LastAlertTitle { get; private set; }

        public Task<string?> PickFolderAsync(string? title = null)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickFileAsync(string? title = null,
                                           IReadOnlyList<FilePickerFilter>? filters = null)
        {
            PickFileCalls++;
            return Task.FromResult(PickFileReturns);
        }

        public Task<string?> PickSaveFileAsync(string? title, string defaultFileName,
                                               IReadOnlyList<FilePickerFilter>? filters = null)
        {
            PickSaveFileCalls++;
            return Task.FromResult(PickSaveFileReturns);
        }

        public Task ShowAlertAsync(string title, string message)
        {
            AlertCalls++;
            LastAlertTitle = title;
            return Task.CompletedTask;
        }

        public Task<string?> ShowInputAsync(string title, string prompt, string? placeholder = null)
        {
            InputCalls++;
            return Task.FromResult(InputReturns);
        }

        public Task<bool?> ShowConfirmAsync(string title, string message,
                                            string confirmLabel = "Confirm", string cancelLabel = "Cancel")
        {
            ConfirmCalls++;
            return Task.FromResult<bool?>(ConfirmReturns);
        }

        public Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt)
        {
            return Task.FromResult(false);
        }
    }

    // ── sync command execution paths ──────────

    [TestMethod]
    public async Task SyncAsync_NoSelection_NoOp()
    {
        // SyncAsync's first guard returns immediately when SelectedProfile
        // is null — the predicate Can* should already prevent this, but
        // the guard inside the method is defense-in-depth.
        StubDialogService dlg = new();
        ProfilesViewModel vm = NewVm(dlg);
        // No selection set.

        await vm.SyncCommand.ExecuteAsync(null);

        Assert.AreEqual(0, dlg.ConfirmCalls,
            "Confirm dialog must not appear when SelectedProfile is null.");
    }

    [TestMethod]
    public async Task SyncAsync_UserCancelsConfirm_NoSync()
    {
        // User says no to the confirm dialog → no SyncFromLiveAsync runs,
        // no status change.  Verifies the early-return on confirmed=false.
        CreateProfile("p");
        Directory.CreateDirectory(Path.Combine(_sandbox, ".claude"));
        await File.WriteAllTextAsync(Path.Combine(_sandbox, ".claude", "settings.json"),
            """{"changed":"true"}""");

        StubDialogService dlg = new() { ConfirmReturns = false };
        ProfilesViewModel vm = NewVm(dlg);
        vm.Refresh();
        vm.SelectedProfile = vm.Profiles.FirstOrDefault(p => p.Name == "p");

        await vm.SyncCommand.ExecuteAsync(null);

        Assert.AreEqual(1, dlg.ConfirmCalls);
        // Profile's settings.json should NOT contain the live-state edit
        // because the user cancelled.
        string profileSettings = await File.ReadAllTextAsync(
            Path.Combine(_sandbox, ".claude", "profiles", "p", "settings.json"));
        Assert.AreEqual("{}", profileSettings,
            "User declined the sync confirm — profile state must remain unchanged.");
    }

    [TestMethod]
    public async Task SyncAsync_UserConfirms_CopiesLiveIntoProfile()
    {
        CreateProfile("p");
        Directory.CreateDirectory(Path.Combine(_sandbox, ".claude"));
        await File.WriteAllTextAsync(Path.Combine(_sandbox, ".claude", "settings.json"),
            """{"model":"haiku"}""");

        StubDialogService dlg = new() { ConfirmReturns = true };
        ProfilesViewModel vm = NewVm(dlg);
        vm.Refresh();
        vm.SelectedProfile = vm.Profiles.First(p => p.Name == "p");

        await vm.SyncCommand.ExecuteAsync(null);

        Assert.AreEqual(1, dlg.ConfirmCalls);
        // Profile settings.json now contains the live state.
        string profileSettings = await File.ReadAllTextAsync(
            Path.Combine(_sandbox, ".claude", "profiles", "p", "settings.json"));
        StringAssert.Contains(profileSettings, "haiku",
            "Confirmed sync must overwrite the profile from the live files.");
    }

    [TestMethod]
    public void SyncCommand_CanExecute_FollowsCanSyncPredicate()
    {
        // Predicate-tracked CanExecute: false until a profile is selected,
        // true once selection is non-null.
        ProfilesViewModel vm = NewVm();
        Assert.IsFalse(vm.SyncCommand.CanExecute(null));

        CreateProfile("p");
        vm.Refresh();
        vm.SelectedProfile = vm.Profiles.First(p => p.Name == "p");

        Assert.IsTrue(vm.SyncCommand.CanExecute(null));
    }

    // ── ExportCommand.CanExecute — locks the GUI button-enable contract ────

    [TestMethod]
    public void ExportCommand_CanExecute_FalseInitially_TrueAfterSelection()
    {
        // regression for the user-reported "Export button
        // is always disabled when I select a profile" symptom.  The
        // button is bound to ExportCommand and uses CanExport() as its
        // predicate.  Selecting a profile sets _selectedProfile, which
        // is decorated with [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
        // so the button must transition from disabled → enabled
        // synchronously on selection change.
        ProfilesViewModel vm = NewVm();
        Assert.IsFalse(vm.ExportCommand.CanExecute(null),
            "Export must be disabled before any profile is selected.");

        CreateProfile("p");
        vm.Refresh();
        vm.SelectedProfile = vm.Profiles.First(p => p.Name == "p");

        Assert.IsTrue(vm.ExportCommand.CanExecute(null),
            "Export must enable as soon as a profile is selected (and IsBusy is false).  " +
            "If this fails, the [NotifyCanExecuteChangedFor(nameof(ExportCommand))] " +
            "attribute on _selectedProfile is missing or the source generator did not " +
            "wire the CanExecuteChanged event.");
    }

    [TestMethod]
    public void ExportCommand_CanExecute_FalseWhileIsBusy()
    {
        // Locks the IsBusy interlock on the predicate side: even with
        // a profile selected, IsBusy=true must keep the button disabled.
        // Coupled with [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
        // on _isBusy so the toggle is reactive.
        ProfilesViewModel vm = NewVm();
        CreateProfile("p");
        vm.Refresh();
        vm.SelectedProfile = vm.Profiles.First(p => p.Name == "p");
        Assert.IsTrue(vm.ExportCommand.CanExecute(null), "precondition: enabled.");

        vm.IsBusy = true;
        Assert.IsFalse(vm.ExportCommand.CanExecute(null),
            "Export must disable while IsBusy=true.");

        vm.IsBusy = false;
        Assert.IsTrue(vm.ExportCommand.CanExecute(null),
            "Export must re-enable when IsBusy returns to false.");
    }

    // Note on event-subscription testing: CommunityToolkit.Mvvm's
    // [RelayCommand]-generated `NotifyCanExecuteChanged()` posts to the
    // ambient SynchronizationContext, which means CanExecuteChanged
    // fires on the dispatcher loop, not synchronously inside the
    // setter.  In synchronous unit tests with no Avalonia dispatcher,
    // the event never surfaces.  In production (running Avalonia app)
    // the event fires on the next UI tick, which Avalonia's Button
    // binding subscribes to.  The two predicate-polling tests above
    // ARE the right unit-level coverage; the dispatcher-driven event
    // is implicitly verified by the H-3 headless tests + manual smoke.

    // ── ApplyAsync / ExportAsync / ImportAsync: these async commands were at 0% coverage;
    //    each test isolates one branch of the method's decision tree.

    [TestMethod]
    public async Task ApplyAsync_UserDeclinesConfirm_NoApply()
    {
        // Confirm dialog returns false → ProfileEngine.ApplyProfileToLiveAsync
        // never runs; live settings.json stays untouched.
        CreateProfile("p");
        await File.WriteAllTextAsync(Path.Combine(_sandbox, ".claude", "settings.json"),
            """{"live":"original"}""");

        StubDialogService dlg = new() { ConfirmReturns = false };
        ProfilesViewModel vm = NewVm(dlg);
        vm.Refresh();
        vm.SelectedProfile = vm.Profiles.First(p => p.Name == "p");

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.AreEqual(1, dlg.ConfirmCalls);
        Assert.AreEqual("""{"live":"original"}""",
            await File.ReadAllTextAsync(Path.Combine(_sandbox, ".claude", "settings.json")),
            "Decline must preserve live settings.json.");
    }

    [TestMethod]
    public async Task ApplyAsync_UserConfirms_OverwritesLiveFromProfile()
    {
        // Profile has settings.json with one shape; live has a different
        // shape.  Confirm → live overwritten with profile content (after
        // the auto-sync of the prior live state to the previously-active
        // profile, which is a no-op here since no profile is active).
        CreateProfile("p");
        await File.WriteAllTextAsync(Path.Combine(_sandbox, ".claude", "profiles", "p", "settings.json"),
            """{"from":"profile"}""");
        await File.WriteAllTextAsync(Path.Combine(_sandbox, ".claude", "settings.json"),
            """{"from":"live"}""");

        StubDialogService dlg = new() { ConfirmReturns = true };
        ProfilesViewModel vm = NewVm(dlg);
        vm.Refresh();
        vm.SelectedProfile = vm.Profiles.First(p => p.Name == "p");

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.AreEqual(1, dlg.ConfirmCalls);
        Assert.AreEqual("""{"from":"profile"}""",
            await File.ReadAllTextAsync(Path.Combine(_sandbox, ".claude", "settings.json")),
            "Confirmed apply must copy profile settings.json into the live location.");
        Assert.AreEqual("p", ProfileEngine.ReadCurrentProfileName(),
            "Apply must update the active-profile pointer.");
    }

    [TestMethod]
    public async Task ApplyAsync_NoSelection_NoOp()
    {
        // First guard: no SelectedProfile → return immediately, no dialog.
        StubDialogService dlg = new();
        ProfilesViewModel vm = NewVm(dlg);

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.AreEqual(0, dlg.ConfirmCalls,
            "No SelectedProfile → no dialog must appear (defence-in-depth past CanApply).");
    }

    [TestMethod]
    public async Task ExportAsync_UserCancelsFilePicker_NoExport()
    {
        // PickSaveFileAsync returns null → early-return; no export file
        // appears, no status update fires.
        CreateProfile("p");
        StubDialogService dlg = new() { PickSaveFileReturns = null };
        ProfilesViewModel vm = NewVm(dlg);
        vm.Refresh();
        vm.SelectedProfile = vm.Profiles.First(r => r.Name == "p");

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.AreEqual(1, dlg.PickSaveFileCalls,
            "Save-file picker must have appeared once.");
        // No status string set (would be StatusProfileExportedFmt on success).
        Assert.IsNull(vm.StatusMessage);
    }

    [TestMethod]
    public async Task ExportAsync_UserPicksDestination_WritesProfileJson()
    {
        // PickSaveFileAsync returns a path → ProfileEngine.ExportProfileAsync
        // writes a JSON file there.  We verify the file exists and has the
        // expected ExportedProfile envelope (Name + Schema fields).
        CreateProfile("p", withClaudeMd: true);
        string destPath = Path.Combine(_sandbox, "exports", "p.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        StubDialogService dlg = new() { PickSaveFileReturns = destPath };
        ProfilesViewModel vm = NewVm(dlg);
        vm.Refresh();
        vm.SelectedProfile = vm.Profiles.First(r => r.Name == "p");

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.AreEqual(1, dlg.PickSaveFileCalls);
        Assert.IsTrue(File.Exists(destPath),
            $"Export must have written the profile JSON to {destPath}.");
        string content = await File.ReadAllTextAsync(destPath);
        StringAssert.Contains(content, "\"name\"",
            "Exported file must contain the ExportedProfile envelope.");
        Assert.IsNotNull(vm.StatusMessage,
            "Successful export must populate StatusMessage.");
    }

    [TestMethod]
    public async Task ImportAsync_UserCancelsFilePicker_NoImport()
    {
        // PickFileAsync returns null → early-return; no profile created.
        StubDialogService dlg = new() { PickFileReturns = null };
        ProfilesViewModel vm = NewVm(dlg);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.AreEqual(1, dlg.PickFileCalls);
        Assert.AreEqual(0, vm.Profiles.Count,
            "User cancelled → no profile dir should appear under profiles/.");
    }

    [TestMethod]
    public async Task ImportAsync_UserPicksValidProfileJson_CreatesProfile()
    {
        // Round-trip: Export "p", delete the profile dir, then Import the
        // exported file → profile reappears.  Verifies the Import happy
        // path end-to-end through ProfileEngine.ImportProfileAsync.
        CreateProfile("p");
        string exportPath = Path.Combine(_sandbox, "exports", "p.json");
        Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
        await ProfileEngine.ExportProfileAsync("p", exportPath);

        // Delete the profile so import can re-create it.
        Directory.Delete(Path.Combine(_sandbox, ".claude", "profiles", "p"), recursive: true);

        StubDialogService dlg = new() { PickFileReturns = exportPath };
        ProfilesViewModel vm = NewVm(dlg);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.AreEqual(1, dlg.PickFileCalls);
        Assert.IsTrue(Directory.Exists(Path.Combine(_sandbox, ".claude", "profiles", "p")),
            "Import must have re-created the profile directory.");
        Assert.AreEqual(1, vm.Profiles.Count,
            "Refresh after import must surface the re-created profile.");
    }
}