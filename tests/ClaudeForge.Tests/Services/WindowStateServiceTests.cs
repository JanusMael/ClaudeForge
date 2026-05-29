using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Services;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Services;

/// <summary>
/// Tests for the persisted UI-state file lifecycle: load / save / delete.
/// Each test isolates via <see cref="PlatformPaths.TestUserProfileOverride"/>
/// so the real <c>~/.claude/cache/ClaudeForge-gui-state.json</c> is never touched.
/// </summary>
[TestClass]
public sealed class WindowStateServiceTests
{
    private string _sandbox = null!;

    [TestInitialize]
    public void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }

    [TestMethod]
    public void Delete_RemovesPersistedFile()
    {
        // Save a state to disk so there is something to delete.
        WindowStateService.Save(new WindowState { Width = 9999 });

        string statePath = Path.Combine(_sandbox, ".claude", "cache", "ClaudeForge-gui-state.json");
        Assert.IsTrue(File.Exists(statePath), "Setup: Save must have created the file.");

        WindowStateService.Delete();

        Assert.IsFalse(File.Exists(statePath),
            "Delete must remove the persisted UI-state file so the next launch starts " +
            "from defaults — this is the contract the Clear App Data button depends on.");
    }

    [TestMethod]
    public void Delete_WhenFileMissing_DoesNotThrow()
    {
        // Calling Delete on a fresh sandbox where the state file was never
        // written must be a silent no-op — Clear App Data must never block
        // shutdown on a missing-file edge case.
        string path = WindowStateService.Delete();

        Assert.IsNotNull(path, "Delete must return the targeted path even when the file did not exist.");
    }

    [TestMethod]
    public void Load_PreExistingStateFile_MissingShowWelcomeNodeKey_DefaultsToTrue()
    {
        // regression — user launched with a state file that
        // predated the showWelcomeNode field; the Welcome nav node was
        // missing because the deserialized value came back as false
        // instead of honouring the property initializer.
        //
        // This test hand-writes a state file missing the new key + asserts
        // ShowWelcomeNode round-trips as true (the field-initializer
        // default).  If the property is missing from the JSON, the
        // deserializer MUST honour the C#-side default — otherwise
        // every user who launches after a feature add gets the opposite
        // of the documented default.
        string stateDir = Path.Combine(_sandbox, ".claude", "cache");
        Directory.CreateDirectory(stateDir);
        string stateFile = Path.Combine(stateDir, "ClaudeForge-gui-state.json");
        File.WriteAllText(stateFile, """{"width":1200,"height":900,"theme":"System"}""");

        WindowState loaded = WindowStateService.Load();

        Assert.IsTrue(loaded.ShowWelcomeNode,
            "Missing showWelcomeNode key must deserialize to the field-initializer default (true). "
            + "If this fails, the source-generated context isn't honouring property initializers "
            + "and every pre-feature state file silently opts the user out of the Welcome page.");
    }

    [TestMethod]
    public void LoadAfterDelete_ReturnsDefaults()
    {
        // After delete, Load must return a fresh default WindowState rather
        // than throwing or echoing stale state — the next launch needs to
        // boot from clean defaults so window position / theme / last-node
        // all reset.
        WindowStateService.Save(new WindowState { Width = 1234, Height = 567 });
        WindowStateService.Delete();

        WindowState loaded = WindowStateService.Load();

        Assert.AreEqual(1200, loaded.Width, "Width must fall back to the WindowState default.");
        // bumped from 750 to 900 (~20% taller) so the editor
        // area isn't cut off on a fresh install before the user resizes.
        Assert.AreEqual(900, loaded.Height, "Height must fall back to the WindowState default.");
        Assert.IsNull(loaded.LastSelectedNodeTitle);
    }

    [TestMethod]
    public void LoadCount_IsZeroBeforeAnyLoad_AndIncrementsExactlyOncePerCall()
    {
        // the SaveWindowState cache fix targets a
        // single Load() call per session (the initial cache hydrate). The
        // Load counter is the runtime instrument the plan asked for —
        // tests that drive a controlled number of Load calls observe an
        // exactly-matching count.
        WindowStateService.ResetLoadCountForTesting();
        Assert.AreEqual(0, WindowStateService.LoadCount, "Counter must start at 0 after reset.");

        _ = WindowStateService.Load();
        Assert.AreEqual(1, WindowStateService.LoadCount, "First Load must increment to 1.");

        _ = WindowStateService.Load();
        Assert.AreEqual(2, WindowStateService.LoadCount,
            "Second Load must increment to 2 — locks the per-call increment contract.");
    }

    [TestMethod]
    public void Delete_DoesNotTouchClaudeConfigFiles()
    {
        // Critical contract: Clear App Data resets the UI, NOT the user's
        // Claude config. settings.json must still exist after the delete.
        string settingsPath = Path.Combine(_sandbox, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, """{"model":"sonnet"}""");

        WindowStateService.Save(new WindowState { Width = 9999 });
        WindowStateService.Delete();

        Assert.IsTrue(File.Exists(settingsPath),
            "settings.json must NOT be deleted — Clear App Data is a UI-state reset only.");
        Assert.AreEqual("""{"model":"sonnet"}""", File.ReadAllText(settingsPath),
            "settings.json contents must be untouched.");
    }

    [TestMethod]
    public void LoadCount_ExactlyOne_AfterMwvmConstruction()
    {
        // MainWindowViewModel calls WindowStateService.Load()
        // exactly once during construction (line 212 of MainWindowViewModel.cs) to
        // hydrate _cachedState. Subsequent SaveWindowState() calls mutate that in-memory
        // object and write to disk without calling Load() again.
        //
        // If LoadCount > 1 after construction it means a new Load() call was
        // accidentally introduced on a hot path — which was the regression the
        // 4.7 cache fix targeted. This test locks the "single hydrate" contract
        // so a future refactor cannot silently re-introduce extra disk reads.
        //
        // Sandbox isolation: PlatformPaths.TestUserProfileOverride is already set
        // to _sandbox in TestInitialize so Load() reads from the test temp dir
        // rather than the developer's real ~/.claude/cache directory.
        Directory.CreateDirectory(Path.Combine(_sandbox, ".claude"));

        WindowStateService.ResetLoadCountForTesting();
        Assert.AreEqual(0, WindowStateService.LoadCount,
            "Counter must be 0 before MWVM is constructed.");

        MainWindowViewModel vm = new(new SchemaRegistry(), new NullDialogService());
        try
        {
            Assert.AreEqual(1, WindowStateService.LoadCount,
                "MWVM construction must call WindowStateService.Load() exactly once. " +
                "If count > 1 a second Load() call was introduced on a hot path — " +
                "regressing the single-hydrate contract.");
        }
        finally
        {
            vm.Dispose();
        }
    }

    // -------------------------------------------------------------------------
    // Update-check persistence — new in 2026-05-28.
    //
    // Auto-update-check has two persisted fields on WindowState:
    //   CheckForUpdatesOnLaunch (bool, default true) — user-facing toggle.
    //   DismissedUpdateVersions (List<string>) — per-tag dismiss
    //                                            persistence.
    // These tests pin the defaults + round-trip through Save/Load.
    // -------------------------------------------------------------------------

    [TestMethod]
    public void CheckForUpdatesOnLaunch_DefaultsToTrue()
    {
        // Fresh WindowState on a clean install (no file on disk yet) —
        // the user should get the auto-check behaviour by default.  If
        // they want it off they toggle the Essentials card; the default
        // should match the user's reasonable expectation that an app
        // tells them when it's out of date.
        WindowState state = WindowStateService.Load();
        Assert.IsTrue(state.CheckForUpdatesOnLaunch,
            "CheckForUpdatesOnLaunch default must be true on a fresh install. " +
            "If you change this default, also update the Essentials card's expected initial state.");
    }

    [TestMethod]
    public void DismissedUpdateVersions_DefaultsToEmpty()
    {
        WindowState state = WindowStateService.Load();
        Assert.IsNotNull(state.DismissedUpdateVersions,
            "List must be initialised, not null — the Dismiss command path " +
            "calls .Add(tag) directly without a null-check.");
        Assert.AreEqual(0, state.DismissedUpdateVersions.Count,
            "No tags dismissed on a clean install.");
    }

    [TestMethod]
    public void CheckForUpdatesOnLaunch_RoundTripsThroughSaveAndLoad()
    {
        // User toggles the Essentials card off → Save → next launch →
        // Load → reads back as false.  Locks the persistence contract.
        WindowState toSave = new() { CheckForUpdatesOnLaunch = false };
        WindowStateService.Save(toSave);

        WindowState loaded = WindowStateService.Load();
        Assert.IsFalse(loaded.CheckForUpdatesOnLaunch,
            "The user's 'check disabled' choice must survive a save-then-load cycle.");
    }

    [TestMethod]
    public void DismissedUpdateVersions_RoundTripsAllEntriesInOrder()
    {
        // User dismisses two consecutive releases (rare in practice but
        // the contract must hold) — both tags must survive a save-then-
        // load round-trip with their content byte-exact (tag comparison
        // is byte-exact, not normalised).
        WindowState toSave = new()
        {
            DismissedUpdateVersions = ["v2026.5.523", "v2026.6.524"],
        };
        WindowStateService.Save(toSave);

        WindowState loaded = WindowStateService.Load();
        CollectionAssert.AreEqual(
            new[] { "v2026.5.523", "v2026.6.524" },
            loaded.DismissedUpdateVersions,
            "Dismissed-versions list must round-trip in full — duplicates / order " +
            "are managed by the caller, not by the serializer.");
    }

    [TestMethod]
    public void DismissedUpdateVersions_NullJsonField_DeserialisesToEmptyList()
    {
        // Defensive: a pre-fix state file (written before this field
        // existed) has no `dismissedUpdateVersions` JSON property.
        // System.Text.Json should leave the property at its default
        // (empty List).  Locking this protects against the symptom we
        // hit on the ShowWelcomeNode field — a missing field
        // deserialising to false / null and breaking the runtime
        // contract.
        string statePath = Path.Combine(_sandbox, ".claude", "cache", "ClaudeForge-gui-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        // Write a minimal state file with NO dismissedUpdateVersions field.
        File.WriteAllText(statePath, """{"width":1200,"height":900}""");

        WindowState loaded = WindowStateService.Load();
        Assert.IsNotNull(loaded.DismissedUpdateVersions,
            "Old state files (pre-fix) MUST deserialise to a non-null list — " +
            "the Dismiss command path does .Add(tag) without null-checking.");
        Assert.AreEqual(0, loaded.DismissedUpdateVersions.Count,
            "Empty list, not garbage from prior runs.");
    }

    [TestMethod]
    public void CheckForUpdatesOnLaunch_MissingJsonField_DefaultsToTrueOnLoad()
    {
        // Same defensive contract as above: a pre-fix state file
        // missing the field should deserialise the property to its
        // default value — which is `true` (auto-check on).  System.Text.Json
        // leaves missing fields at their C# property initialiser value.
        string statePath = Path.Combine(_sandbox, ".claude", "cache", "ClaudeForge-gui-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        File.WriteAllText(statePath, """{"width":1200,"height":900}""");

        WindowState loaded = WindowStateService.Load();
        Assert.IsTrue(loaded.CheckForUpdatesOnLaunch,
            "Old state files (pre-fix) MUST deserialise to the new field's default — true.  " +
            "Otherwise existing users would silently lose the auto-check behaviour after upgrade.");
    }

    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

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