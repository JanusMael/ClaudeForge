using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.AgentForge.Abstractions.Dialogs;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Services;
using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// The page-navigation lifecycle: what a page is told when the user arrives at it
/// and when they leave.
///
/// <para>
/// This whole surface was <b>completely uncovered</b> until the hooks were extracted
/// behind <see cref="INavigablePage"/>. Deleting the leave dispatch outright failed
/// <b>zero</b> of 2,910 tests, and so did forcing its "a different editor is taking
/// over" flag to a constant in <em>either</em> direction — meaning the guard that
/// stops a workspace reload from throwing away a user's typed filter was protected
/// by nothing at all.
/// </para>
/// <para>
/// The first two tests are the ones that matter most: they drive the dispatch with a
/// page type that is not a view-model of this app, so they can only pass if the host
/// dispatches on the interface rather than on a chain of concrete types. That is the
/// property the extraction bought, and the failure mode it removes — a newly added
/// page silently never being refreshed — has no compiler signal and no symptom
/// beyond stale content.
/// </para>
/// </summary>
[TestClass]
public sealed class NavigationPageLifecycleTests
{
    private string _sandbox = string.Empty;

    /// <summary>A page belonging to no product, recording exactly what it was told.</summary>
    private sealed class RecordingPage : INavigablePage
    {
        public int Entered { get; private set; }

        public int Left { get; private set; }

        public bool? LastReplacedFlag { get; private set; }

        public void OnNavigatedTo()
        {
            Entered++;
        }

        public void OnNavigatedFrom(bool replaced)
        {
            Left++;
            LastReplacedFlag = replaced;
        }
    }

    /// <summary>A page that declares the interface but neither hook.</summary>
    private sealed class SilentPage : INavigablePage;

    /// <summary>
    /// Local copy, matching the other headless fixtures. Note the confirm / save
    /// answers are <c>false</c>: nothing here should reach a dialog, and a test that
    /// silently started to would fail loudly rather than write.
    /// </summary>
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

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_navlifecycle_" + Guid.NewGuid().ToString("N"));
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
        DebugFlags.ResetForTesting();
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_sandbox))
            {
                TestCleanupHelpers.DeleteDirectoryWithRetry(_sandbox);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    private static MainWindowViewModel BuildViewModel()
    {
        return new MainWindowViewModel(new SchemaRegistry(new HttpClient()), new NullDialogService());
    }

    private static NavigationNodeViewModel Attach(
        MainWindowViewModel vm, string title, object editor)
    {
        NavigationNodeViewModel node = new(title) { NodeId = title, Editor = editor, IsTopLevel = true };
        vm.NavigationTree.Add(node);
        return node;
    }

    // ── Dispatch is by interface, not by concrete type ────────────────────

    [TestMethod]
    public async Task APageThisAppDoesNotOwn_StillGetsBothHooks()
    {
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        RecordingPage first = new();
        RecordingPage second = new();
        NavigationNodeViewModel firstNode = Attach(vm, "fake-one", first);
        NavigationNodeViewModel secondNode = Attach(vm, "fake-two", second);

        vm.SelectedNode = firstNode;
        Assert.AreEqual(1, first.Entered, "Arriving at a page must call OnNavigatedTo.");
        Assert.AreEqual(0, first.Left);

        vm.SelectedNode = secondNode;
        Assert.AreEqual(1, first.Left, "Leaving a page must call OnNavigatedFrom exactly once.");
        Assert.AreEqual(1, second.Entered);
        Assert.AreEqual(true, first.LastReplacedFlag,
            "A different editor took over, so the outgoing page is genuinely being replaced.");

        vm.Dispose();
    }

    /// <summary>
    /// The direction nothing guarded. Several pages survive a workspace reload and are
    /// re-attached to a freshly built node — the outgoing and incoming editor are then
    /// the <em>same instance</em>, and a page that discards transient state on the way
    /// out must be told so it can keep it.
    /// </summary>
    [TestMethod]
    public async Task SameEditorInstanceTakingOver_IsReportedAsNotReplaced()
    {
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        RecordingPage shared = new();
        NavigationNodeViewModel oldNode = Attach(vm, "before-reload", shared);
        NavigationNodeViewModel rebuiltNode = Attach(vm, "after-reload", shared);

        vm.SelectedNode = oldNode;
        vm.SelectedNode = rebuiltNode;

        Assert.AreEqual(1, shared.Left, "The hook still fires — only the flag differs.");
        Assert.AreEqual(false, shared.LastReplacedFlag,
            "The incoming editor IS this instance, so it is not being replaced.");

        vm.Dispose();
    }

    [TestMethod]
    public async Task APageDeclaringNeitherHook_IsSafeToNavigateThroughBothWays()
    {
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        NavigationNodeViewModel silent = Attach(vm, "silent", new SilentPage());
        NavigationNodeViewModel other = Attach(vm, "other", new RecordingPage());

        vm.SelectedNode = silent;
        vm.SelectedNode = other;
        vm.SelectedNode = silent;

        Assert.AreSame(silent, vm.SelectedNode,
            "The default no-op hooks must let navigation complete normally.");

        vm.Dispose();
    }

    // ── The real pages' leave behaviour ───────────────────────────────────

    [TestMethod]
    public async Task LeavingASettingsGroup_ClearsTheFilterTheNextVisitWouldInherit()
    {
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        NavigationNodeViewModel groupNode = vm.NavigationTree
                                              .First(n => n.NodeId == MainWindowViewModel.NavIdClaudeCode)
                                              .Children
                                              .First(c => c.Editor is SettingsGroupEditorViewModel);
        var group = (SettingsGroupEditorViewModel)groupNode.Editor!;

        vm.SelectedNode = groupNode;
        group.FilterText = "cleanup";
        Assert.AreEqual("cleanup", group.FilterText, "Precondition: the filter is set.");

        vm.SelectedNode = vm.NavigationTree.First(n => n.NodeId == MainWindowViewModel.NavIdEssentials);

        Assert.AreEqual(string.Empty, group.FilterText,
            "Navigating away must clear the group's filter so the next visit starts unfiltered.");

        vm.Dispose();
    }

    [TestMethod]
    public async Task LeavingTheEnvironmentPage_ClearsItsFilter()
    {
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        NavigationNodeViewModel envNode = vm.NavigationTree
                                            .First(n => n.NodeId == MainWindowViewModel.NavIdEnvironment);
        var env = (EnvironmentEditorViewModel)envNode.Editor!;

        vm.SelectedNode = envNode;
        env.FilterText = "PATH";
        Assert.AreEqual("PATH", env.FilterText, "Precondition: the filter is set.");

        vm.SelectedNode = vm.NavigationTree.First(n => n.NodeId == MainWindowViewModel.NavIdEssentials);

        Assert.AreEqual(string.Empty, env.FilterText,
            "Navigating away must clear the Environment page's filter.");

        vm.Dispose();
    }

    [TestMethod]
    public async Task LeavingAgentsSkills_ForADifferentPage_ClearsItsNavigationFilter()
    {
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        NavigationNodeViewModel agentsNode = vm.NavigationTree
                                               .First(n => n.NodeId == MainWindowViewModel.NavIdAgentsSkills);
        var agents = (AgentsSkillsEditorViewModel)agentsNode.Editor!;

        vm.SelectedNode = agentsNode;
        agents.ApplyNavigationFilter("pdf");
        Assert.AreEqual("pdf", agents.FilterText, "Precondition: the reveal filter is applied.");

        vm.SelectedNode = vm.NavigationTree.First(n => n.NodeId == MainWindowViewModel.NavIdEssentials);

        Assert.AreEqual(string.Empty, agents.FilterText,
            "Leaving for a different page must clear the reveal filter.");

        vm.Dispose();
    }

    /// <summary>
    /// The counterpart to the test above, and the reason the flag exists: when the
    /// incoming editor is this same instance, the filter must survive.
    /// </summary>
    [TestMethod]
    public async Task AgentsSkills_KeepsItsFilter_WhenTheSameEditorInstanceTakesOver()
    {
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        NavigationNodeViewModel agentsNode = vm.NavigationTree
                                               .First(n => n.NodeId == MainWindowViewModel.NavIdAgentsSkills);
        var agents = (AgentsSkillsEditorViewModel)agentsNode.Editor!;

        vm.SelectedNode = agentsNode;
        agents.ApplyNavigationFilter("pdf");

        // A rebuilt node re-attached to the surviving view-model.
        NavigationNodeViewModel rebuilt = Attach(vm, "agents-skills-rebuilt", agents);
        vm.SelectedNode = rebuilt;

        Assert.AreEqual("pdf", agents.FilterText,
            "The user never navigated away, so the reveal filter must survive.");

        vm.Dispose();
    }
}
