using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="MainWindowViewModel.AvailableProfileEntries"/>,
/// <see cref="MainWindowViewModel.SelectedProfileEntry"/>, and the
/// <see cref="MainWindowViewModel.CanDeleteProfile"/> gate.
/// </summary>
[TestClass]
public sealed class AvailableProfileEntriesTests
{
    private string _sandbox = null!;
    private MainWindowViewModel _vm = null!;

    [TestInitialize]
    public void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        // Instantiate without triggering InitializeAsync (no workspace load needed).
        _vm = new MainWindowViewModel(new SchemaRegistry(), new NullDialogService());
    }

    [TestCleanup]
    public void Cleanup()
    {
        _vm.Dispose();
        PlatformPaths.TestUserProfileOverride = null;
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void CreateCliProfile(string name)
    {
        Directory.CreateDirectory(Path.Combine(PlatformPaths.ProfilesDirectory, name));
    }

    private void CreateDesktopProfile(string name)
    {
        string dir = Path.Combine(PlatformPaths.DesktopProfilesDirectory, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "claude_desktop_config.json"), "{}");
    }

    // -----------------------------------------------------------------------
    // AvailableProfileEntries — structure
    // -----------------------------------------------------------------------

    [TestMethod]
    public void AvailableProfileEntries_AlwaysStartsWithGlobal()
    {
        IReadOnlyList<UnifiedProfileEntry> entries = _vm.AvailableProfileEntries;

        Assert.IsTrue(entries.Count >= 1);
        Assert.AreEqual(UnifiedProfileEntry.GlobalName, entries[0].Name);
        Assert.IsTrue(entries[0].IsGlobal);
    }

    [TestMethod]
    public void AvailableProfileEntries_NoProfiles_ContainsOnlyGlobal()
    {
        IReadOnlyList<UnifiedProfileEntry> entries = _vm.AvailableProfileEntries;

        Assert.AreEqual(1, entries.Count);
    }

    [TestMethod]
    public void AvailableProfileEntries_CliOnlyProfile_HasCliTrueHasDesktopFalse()
    {
        CreateCliProfile("work");

        IReadOnlyList<UnifiedProfileEntry> entries = _vm.AvailableProfileEntries;
        UnifiedProfileEntry work = entries.Single(e => e.Name == "work");

        Assert.IsTrue(work.HasCli);
        Assert.IsFalse(work.HasDesktop);
    }

    [TestMethod]
    public void AvailableProfileEntries_DesktopOnlyProfile_HasCliTrueIsFalse()
    {
        CreateDesktopProfile("home");

        IReadOnlyList<UnifiedProfileEntry> entries = _vm.AvailableProfileEntries;
        UnifiedProfileEntry home = entries.Single(e => e.Name == "home");

        Assert.IsFalse(home.HasCli);
        Assert.IsTrue(home.HasDesktop);
    }

    [TestMethod]
    public void AvailableProfileEntries_SharedProfile_MergedWithBothFlags()
    {
        CreateCliProfile("work");
        CreateDesktopProfile("work");

        IReadOnlyList<UnifiedProfileEntry> entries = _vm.AvailableProfileEntries;
        UnifiedProfileEntry work = entries.Single(e => e.Name == "work");

        Assert.IsTrue(work.HasCli);
        Assert.IsTrue(work.HasDesktop);
        // Must appear only once in the list
        Assert.AreEqual(1, entries.Count(e => e.Name == "work"));
    }

    [TestMethod]
    public void AvailableProfileEntries_CliOnlyBeforeDesktopOnly()
    {
        // "zzz" CLI-only should sort before "aaa" Desktop-only in the merged list
        // because CLI profiles come first as a group, then Desktop-only.
        CreateCliProfile("zzz-cli");
        CreateDesktopProfile("aaa-desktop");

        IReadOnlyList<UnifiedProfileEntry> entries = _vm.AvailableProfileEntries;
        int cliIdx = entries.ToList().FindIndex(e => e.Name == "zzz-cli");
        int dtIdx = entries.ToList().FindIndex(e => e.Name == "aaa-desktop");

        Assert.IsTrue(cliIdx < dtIdx,
            "CLI-only profiles must appear before Desktop-only profiles regardless of alphabetical order.");
    }

    [TestMethod]
    public void AvailableProfileEntries_SharedProfileSortedWithinCliGroup()
    {
        CreateCliProfile("beta");
        CreateCliProfile("alpha");

        IReadOnlyList<UnifiedProfileEntry> entries = _vm.AvailableProfileEntries;
        // skip global at index 0
        List<string> names = entries.Skip(1).Select(e => e.Name).ToList();

        Assert.AreEqual("alpha", names[0]);
        Assert.AreEqual("beta", names[1]);
    }

    // -----------------------------------------------------------------------
    // SelectedProfileEntry
    // -----------------------------------------------------------------------

    [TestMethod]
    public void SelectedProfileEntry_DefaultsToGlobal()
    {
        // SelectedProfile is initialised from saved state; sandbox has no state file
        // so it falls back to GlobalProfileSentinel.
        Assert.IsTrue(_vm.SelectedProfileEntry!.IsGlobal);
    }

    [TestMethod]
    public void SelectedProfileEntry_ReturnsMatchingEntry()
    {
        CreateCliProfile("work");
        _vm.SelectedProfile = "work";

        UnifiedProfileEntry? entry = _vm.SelectedProfileEntry;

        Assert.IsNotNull(entry);
        Assert.AreEqual("work", entry.Name);
        Assert.IsTrue(entry.HasCli);
    }

    [TestMethod]
    public void SelectedProfileEntry_CaseInsensitiveMatch()
    {
        CreateCliProfile("Work");
        _vm.SelectedProfile = "work"; // lowercase, profile dir is "Work"

        UnifiedProfileEntry? entry = _vm.SelectedProfileEntry;

        Assert.IsNotNull(entry);
        Assert.AreEqual("Work", entry.Name); // returns the canonical name from the filesystem
    }

    [TestMethod]
    public void SelectedProfileEntry_Set_UpdatesSelectedProfile()
    {
        CreateCliProfile("personal");
        UnifiedProfileEntry entry = new("personal", HasCli: true, HasDesktop: false);

        _vm.SelectedProfileEntry = entry;

        Assert.AreEqual("personal", _vm.SelectedProfile);
    }

    [TestMethod]
    public void SelectedProfileEntry_SetNull_ResetsToGlobalSentinel()
    {
        _vm.SelectedProfileEntry = null;

        Assert.AreEqual(UnifiedProfileEntry.GlobalName, _vm.SelectedProfile);
    }

    // -----------------------------------------------------------------------
    // CanDeleteProfile
    // -----------------------------------------------------------------------

    [TestMethod]
    public void CanDeleteProfile_FalseWhenGlobalIsSelected()
    {
        // SelectedProfile is "(global)" by default in a clean sandbox
        Assert.IsFalse(_vm.DeleteProfileCommand.CanExecute(null));
    }

    [TestMethod]
    public void CanDeleteProfile_TrueForCliProfile()
    {
        CreateCliProfile("work");
        _vm.SelectedProfile = "work";

        Assert.IsTrue(_vm.DeleteProfileCommand.CanExecute(null));
    }

    [TestMethod]
    public void CanDeleteProfile_FalseForDesktopOnlyProfile()
    {
        // Desktop-only profiles cannot be deleted from the toolbar — use the Profiles page.
        CreateDesktopProfile("home");
        _vm.SelectedProfile = "home";

        Assert.IsFalse(_vm.DeleteProfileCommand.CanExecute(null));
    }

    // -----------------------------------------------------------------------
    // Test double
    // -----------------------------------------------------------------------

    private sealed class NullDialogService : IDialogService
    {
        public Task<string?> PickFolderAsync(string? title = null)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickFileAsync(string? title = null, IReadOnlyList<FilePickerFilter>? filters = null)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickSaveFileAsync(string? title, string defaultFileName,
                                               IReadOnlyList<FilePickerFilter>? filters = null)
        {
            return Task.FromResult<string?>(null);
        }

        public Task ShowAlertAsync(string title, string message)
        {
            return Task.CompletedTask;
        }

        public Task<string?> ShowInputAsync(string title, string prompt, string? placeholder = null)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<bool?> ShowConfirmAsync(string title, string message, string confirmLabel = "Confirm",
                                            string cancelLabel = "Cancel")
        {
            return Task.FromResult<bool?>(false);
        }

        public Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt)
        {
            return Task.FromResult(false);
        }
    }
}