using System.Reflection;
using Avalonia.Headless;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// on a fresh install (no persisted
/// <c>~/.claude/cache/ClaudeForge-gui-state.json</c>), the Welcome page
/// flickered briefly before <c>RestoreSelectedNode</c> auto-selected the
/// first child of Claude Code — new users never got to read the orientation
/// content.
///
/// Fix: a dedicated top-of-tree Welcome nav node with no Editor (so
/// <c>ActiveEditor</c> stays null and the existing <c>WelcomeView</c>
/// renders), selected by default when <c>_lastNodeTitle == null</c>.
///
/// These tests lock both halves of the contract — the node's presence in
/// the navigation tree, and its selection on a fresh state.
/// </summary>
[TestClass]
public sealed class NavigationTreeWelcomeNodeTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    private string _sandbox = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_welcomenode_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        string ccDir = Path.Combine(_sandbox, ".claude");
        Directory.CreateDirectory(ccDir);
        File.WriteAllText(Path.Combine(ccDir, "settings.json"), "{}");

        string dtDir = Path.GetDirectoryName(PlatformPaths.DesktopConfigPath)!;
        Directory.CreateDirectory(dtDir);
        File.WriteAllText(PlatformPaths.DesktopConfigPath, "{}");
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
            /* best effort — file-system indexer may hold transient locks */
        }
    }

    private static MainWindowViewModel BuildViewModel()
    {
        SchemaRegistry schemaRegistry = new(new HttpClient());
        NullDialogService dialog = new();
        return new MainWindowViewModel(schemaRegistry, dialog);
    }

    [TestMethod]
    public Task NavigationTree_AfterFirstLoad_ContainsWelcomeNodeAsFirstTopLevelEntry()
    {
        return Session.Dispatch(async () =>
        {
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();

            Assert.IsTrue(vm.NavigationTree.Count > 0,
                "NavigationTree must be populated after LoadAllWorkspacesAsync.");

            NavigationNodeViewModel first = vm.NavigationTree[0];
            Assert.AreEqual("Welcome", first.Title,
                "Welcome node must be the first top-level entry — it's the orientation landing spot.");
            Assert.IsTrue(first.IsTopLevel);
            Assert.IsNull(first.Editor,
                "Welcome node must have NO Editor so ActiveEditor stays null and WelcomeView renders.");
        }, CancellationToken.None);
    }

    [TestMethod]
    public Task FreshState_NoPersistedSelection_SelectsWelcomeByDefault()
    {
        return Session.Dispatch(async () =>
        {
            // The sandbox has no ~/.claude/cache/ClaudeForge-gui-state.json,
            // so _lastNodeTitle is null at construction time — the fresh-
            // install path through RestoreSelectedNode.
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();

            Assert.IsNotNull(vm.SelectedNode,
                "Fresh-state SelectedNode must not be null — the user needs SOMETHING highlighted "
                + "in the tree so they can tell where they are.");
            Assert.AreEqual("Welcome", vm.SelectedNode!.Title,
                "On a fresh install, the default selection must be the Welcome node so new users "
                + "see the orientation content instead of being dropped straight into the first "
                + "Claude Code editor before they know what they're editing.");
            Assert.IsNull(vm.ActiveEditor,
                "Welcome node has no Editor, so ActiveEditor stays null and the existing "
                + "WelcomeView renders.");
        }, CancellationToken.None);
    }

    // ── Opt-out via the Welcome page's "Show on launch" checkbox (2026-05-19) ──

    [TestMethod]
    public Task ShowWelcomeOnLaunch_DefaultsToTrue_OnFreshState()
    {
        return Session.Dispatch(async () =>
        {
            // Fresh sandbox → no persisted preference → defaults to true.
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();

            Assert.IsTrue(vm.ShowWelcomeOnLaunch,
                "Default for fresh state must be true so first-launch users see the Welcome page.");
        }, CancellationToken.None);
    }

    [TestMethod]
    public Task ToggleOff_RemovesWelcomeNodeFromTree()
    {
        return Session.Dispatch(async () =>
        {
            // User unchecks the "Show on launch" checkbox on the Welcome
            // page.  The node must disappear from the tree immediately
            // (without requiring a relaunch).
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();
            Assert.IsTrue(vm.NavigationTree.Any(n => n.Title == "Welcome"),
                "Pre-toggle baseline: Welcome node is in the tree.");

            vm.ShowWelcomeOnLaunch = false;

            Assert.IsFalse(vm.NavigationTree.Any(n => n.Title == "Welcome"),
                "After toggle-off, the Welcome node must be removed from the navigation tree.");
        }, CancellationToken.None);
    }

    [TestMethod]
    public Task ToggleOff_WhileOnWelcomePage_MovesSelectionToEssentials()
    {
        return Session.Dispatch(async () =>
        {
            // The user is currently SITTING on the Welcome page (default
            // selection on fresh state).  They uncheck the checkbox.
            // Selection must move to a different node (Essentials is the
            // natural top-of-tree successor) so the editor area doesn't
            // briefly bind to a stale removed node.
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();
            Assert.AreEqual("Welcome", vm.SelectedNode!.Title, "Baseline: selected Welcome.");

            vm.ShowWelcomeOnLaunch = false;

            Assert.IsNotNull(vm.SelectedNode);
            Assert.AreNotEqual("Welcome", vm.SelectedNode!.Title,
                "After Welcome is removed, selection must move off it.");
            Assert.AreEqual("Essentials", vm.SelectedNode.Title,
                "Essentials is the natural successor — top-of-tree, user-actionable, has an Editor.");
        }, CancellationToken.None);
    }

    [TestMethod]
    public Task ToggleBackOn_RestoresWelcomeNode()
    {
        return Session.Dispatch(async () =>
        {
            // Round-trip: toggle off, then toggle back on.  The Welcome
            // node must reappear at the top of the tree.
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();

            vm.ShowWelcomeOnLaunch = false;
            Assert.IsFalse(vm.NavigationTree.Any(n => n.Title == "Welcome"));

            vm.ShowWelcomeOnLaunch = true;

            NavigationNodeViewModel first = vm.NavigationTree[0];
            Assert.AreEqual("Welcome", first.Title,
                "Toggling back on must re-insert Welcome at the top of the tree.");
        }, CancellationToken.None);
    }

    [TestMethod]
    public Task Construction_DoesNotPersistShowWelcomeNodeAsFalse_WithoutUserToggle()
    {
        return Session.Dispatch(async () =>
        {
            // Regression for the user-reported "Welcome node disappeared
            // without me clicking anything" bug (2026-05-19).  Construct
            // a VM with default state, run LoadAllWorkspacesAsync, and
            // then inspect the persisted state file — ShowWelcomeNode
            // must NOT have been silently written as false by binding-
            // init round-trips.  If this fails, something is firing the
            // partial OnShowWelcomeOnLaunchChanged handler during
            // construction with value=false.
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();

            // Force a SaveWindowState so any pending state is on disk
            // (no-op if everything has already been persisted, but
            // ensures the inspection below sees the current truth).
            // SaveWindowState is invoked internally by various paths;
            // explicitly invoke OnSelectedNodeChanged via assigning
            // SelectedNode to a non-Welcome node to drive a clean save.
            NavigationNodeViewModel? essentials = vm.NavigationTree.FirstOrDefault(n => n.Title == "Essentials");
            Assert.IsNotNull(essentials);
            vm.SelectedNode = essentials;

            // Read the persisted state file.
            string stateFile = Path.Combine(_sandbox, ".claude", "cache",
                "ClaudeForge-gui-state.json");
            Assert.IsTrue(File.Exists(stateFile),
                "Setup: SaveWindowState should have written the state file.");
            string json = await File.ReadAllTextAsync(stateFile);
            // Must either NOT contain showWelcomeNode (then it defaults true on load)
            // OR contain it set to true.  An explicit `"showWelcomeNode": false`
            // here proves the partial handler fired during construction.
            Assert.IsFalse(
                json.Contains("\"showWelcomeNode\": false") ||
                json.Contains("\"showWelcomeNode\":false"),
                $"State file must NOT persist showWelcomeNode=false without a user toggle. "
                + $"Actual JSON: {json}");

            // And on reload, ShowWelcomeOnLaunch must still be true.
            MainWindowViewModel vm2 = BuildViewModel();
            await vm2.LoadAllWorkspacesAsync();
            Assert.IsTrue(vm2.ShowWelcomeOnLaunch,
                "Round-trip: reloading after no-toggle construction must keep ShowWelcomeOnLaunch=true.");
            Assert.IsTrue(vm2.NavigationTree.Any(n => n.Title == "Welcome"),
                "Round-trip: Welcome nav node must still be present after no-toggle reload.");
        }, CancellationToken.None);
    }

    [TestMethod]
    public Task PersistedOptOut_PreventsNodeFromAppearing_OnReload()
    {
        return Session.Dispatch(async () =>
        {
            // First session: opt out + this persists via WindowState.
            MainWindowViewModel vm1 = BuildViewModel();
            await vm1.LoadAllWorkspacesAsync();
            vm1.ShowWelcomeOnLaunch = false;

            // Second session (simulated by constructing a fresh VM over the
            // same sandbox): the persisted preference must carry through —
            // the Welcome node must NOT appear, and the default selection
            // must fall through to Essentials.
            MainWindowViewModel vm2 = BuildViewModel();
            await vm2.LoadAllWorkspacesAsync();

            Assert.IsFalse(vm2.ShowWelcomeOnLaunch,
                "Persisted opt-out must survive reconstruction.");
            Assert.IsFalse(vm2.NavigationTree.Any(n => n.Title == "Welcome"),
                "Welcome node must NOT be added when the user has opted out.");
            Assert.IsNotNull(vm2.SelectedNode);
            Assert.AreEqual("Essentials", vm2.SelectedNode!.Title,
                "Default selection falls through to Essentials when Welcome is opted out.");
        }, CancellationToken.None);
    }

    [TestMethod]
    public Task LastNodeWasWelcome_ButPrefIsOff_FallsThroughToEssentials()
    {
        return Session.Dispatch(async () =>
        {
            // Edge case: user was on Welcome when they unchecked the
            // checkbox + closed the app.  WindowState.LastSelectedNodeTitle
            // == "Welcome" AND WindowState.ShowWelcomeNode == false.  On
            // relaunch, RestoreSelectedNode must NOT try to restore Welcome
            // (it's no longer in the tree) and must fall through to
            // Essentials.
            MainWindowViewModel vm1 = BuildViewModel();
            await vm1.LoadAllWorkspacesAsync();
            // vm1 starts on Welcome (default).  Opt out, which moves
            // selection to Essentials, then move back to Welcome by direct
            // assignment to simulate the user re-clicking it after toggle —
            // but ShowWelcomeOnLaunch is still false, so the node was
            // removed.  Instead, fake the state-file scenario directly by
            // setting up _lastNodeTitle through a fresh construction with a
            // pre-written state file.
            vm1.ShowWelcomeOnLaunch = false;
            // At this point: SelectedNode is Essentials, lastNode persisted
            // is "Essentials".  To exercise the edge case I want, I need
            // the persisted state to literally say lastNode="Welcome" AND
            // showWelcomeNode=false.  Hand-write the state file:
            string stateFile = Path.Combine(_sandbox, ".claude", "cache",
                "ClaudeForge-gui-state.json");
            Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
            await File.WriteAllTextAsync(stateFile,
                """{"lastNode":"Welcome","showWelcomeNode":false}""");

            // Second session reads that crafted state file.
            MainWindowViewModel vm2 = BuildViewModel();
            await vm2.LoadAllWorkspacesAsync();

            Assert.IsFalse(vm2.ShowWelcomeOnLaunch);
            Assert.IsFalse(vm2.NavigationTree.Any(n => n.Title == "Welcome"));
            Assert.IsNotNull(vm2.SelectedNode);
            Assert.AreEqual("Essentials", vm2.SelectedNode!.Title,
                "When the saved lastNode is Welcome but the user opted out, fall through "
                + "to Essentials rather than the Claude-Code-first-child fallback (the user "
                + "explicitly chose to skip Welcome).");
        }, CancellationToken.None);
    }

    // ── Test doubles ────────────────────────────────────────────────────

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