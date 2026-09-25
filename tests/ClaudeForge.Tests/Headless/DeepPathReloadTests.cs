using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using Bennewitz.Ninja.ClaudeForge.Services;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.AppServices.Abstractions;
using Bennewitz.Ninja.ScopedEditors.ViewModels;
using Bennewitz.Ninja.AppServices.Abstractions.Dialogs;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// End-to-end deep-path restore across a real in-process "Reload Window".
///
/// <para>
/// Reload is not a process restart — <c>ReloadCoreAsync</c> rebuilds the
/// navigation tree and every editor view-model. Before this feature,
/// <c>RestoreSelectedNode</c> matched on node <c>Title</c> only, so the active
/// segment, the open artifact, and (worst) an unsaved front-matter edit were all
/// discarded silently: the Agents &amp; Skills editor writes files directly, so its
/// buffer never contributed to <c>HasUnsavedChanges</c> and nothing warned.
/// </para>
/// <para>
/// <see cref="Reload_DoesNotBlankAPersistedDeepPath"/> is the highest-value test
/// here. It guards the trap that <c>SaveWindowState</c> has ~14 call sites, one of
/// them at the tail of <c>OnSelectedNodeChanged</c>: computing the deep path there
/// instead of persisting a field would capture the freshly-rebuilt (empty) editor
/// and overwrite the good path before the async restore could read it — so the
/// user's place would quietly stop being restored after any reload, with no test
/// failing and no error logged.
/// </para>
/// </summary>
public sealed class DeepPathReloadTests : IDisposable
{
    private string _sandbox = string.Empty;

    public DeepPathReloadTests() => Setup();

    private void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_deeppath_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        string ccDir = Path.Combine(_sandbox, ".claude");
        Directory.CreateDirectory(ccDir);
        File.WriteAllText(Path.Combine(ccDir, "settings.json"), "{}");

        string dtDir = Path.GetDirectoryName(PlatformPaths.DesktopConfigPath)!;
        Directory.CreateDirectory(dtDir);
        File.WriteAllText(PlatformPaths.DesktopConfigPath, "{}");

        // Two skills so a filter-reveal is observably narrowing something.
        WriteArtifact(Path.Combine(ccDir, "skills", "pdf", "SKILL.md"),
            "---\nname: pdf\ndescription: PDF tools\n---\n\nOriginal body.\n");
        WriteArtifact(Path.Combine(ccDir, "skills", "other", "SKILL.md"),
            "---\nname: other\ndescription: Something else\n---\n\nB.\n");
    }

    private void Cleanup()
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

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    private static void WriteArtifact(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static MainWindowViewModel BuildViewModel()
    {
        return new MainWindowViewModel(ClaudeEnvironment.Empty, new SchemaRegistry(), new NullDialogService());
    }

    private static async Task<AgentsSkillsEditorViewModel> OpenAgentsSkillsAsync(MainWindowViewModel vm)
    {
        NavigationNodeViewModel node = vm.NavigationTree.First(
            n => n.NodeId == MainWindowViewModel.NavIdAgentsSkills);
        vm.SelectedNode = node;

        var editor = (AgentsSkillsEditorViewModel)node.Editor!;
        if (editor.LastRefresh is { } r)
        {
            await r;
        }

        if (editor.LastDescriptionFill is { } f)
        {
            await f;
        }

        return editor;
    }

    private static AgentsSkillsEditorViewModel CurrentAgentsSkills(MainWindowViewModel vm)
    {
        return (AgentsSkillsEditorViewModel)vm.NavigationTree
                                              .First(n => n.NodeId == MainWindowViewModel.NavIdAgentsSkills)
                                              .Editor!;
    }

    private static async Task SettleRestoreAsync(MainWindowViewModel vm)
    {
        if (vm.LastDeepRestore is { } restore)
        {
            await restore;
        }
    }

    [Fact]
    public async Task Reload_RestoresSegmentArtifactAndTheUnsavedEditBuffer()
    {
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        AgentsSkillsEditorViewModel before = await OpenAgentsSkillsAsync(vm);
        before.SelectSegment(AgentsSkillsEditorViewModel.SegmentSkillsId);

        ArtifactRowViewModel row = before.SkillItems
                                         .OfType<ArtifactRowViewModel>()
                                         .First(r => r.DisplayName == "pdf");
        await before.LoadArtifactAsync(row);
        before.BeginEditCommand.Execute(null);
        before.EditDescription = "typed but never saved";
        before.EditBody = "half-written body";

        // The user hits Reload Window mid-edit.
        await vm.ReloadCommand.ExecuteAsync(null);
        await SettleRestoreAsync(vm);

        AgentsSkillsEditorViewModel after = CurrentAgentsSkills(vm);
        MessageAssert.NotSame(before, after, "Precondition: reload must rebuild the editor VM.");

        MessageAssert.Equal(1, after.SelectedSegmentIndex, "The Skills segment must come back selected.");
        MessageAssert.Equal("pdf", after.SelectedArtifact?.DisplayName, "The open artifact must come back.");
        Assert.True(after.IsEditing, "The editing experience must come back.");
        MessageAssert.Equal("typed but never saved", after.EditDescription,
            "The user's UNSAVED text must survive the reload — not the value re-read from disk.");
        Assert.Equal("half-written body", after.EditBody);

        // Nothing was saved, so the file is untouched.
        string onDisk = await File.ReadAllTextAsync(
            Path.Combine(_sandbox, ".claude", "skills", "pdf", "SKILL.md"), TestContext.Current.CancellationToken);
        OrdinalAssert.Contains("description: PDF tools", onDisk);

        vm.Dispose();
    }

    [Fact]
    public async Task Reload_RevealsTheRestoredItemByFiltering_WithTheNavigatedFrame()
    {
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        AgentsSkillsEditorViewModel before = await OpenAgentsSkillsAsync(vm);
        before.SelectSegment(AgentsSkillsEditorViewModel.SegmentSkillsId);
        await before.LoadArtifactAsync(
            before.SkillItems.OfType<ArtifactRowViewModel>().First(r => r.DisplayName == "pdf"));

        await vm.ReloadCommand.ExecuteAsync(null);
        await SettleRestoreAsync(vm);

        AgentsSkillsEditorViewModel after = CurrentAgentsSkills(vm);

        MessageAssert.Equal("pdf", after.FilterText, "The item is revealed by filtering to it.");
        Assert.True(after.FilterFromNavigation,
            "The filter came from navigation, so the orange navigated frame must show.");
        MessageAssert.Equal(1, after.FilteredSkillItems.OfType<ArtifactRowViewModel>().Count(),
            "The list should be narrowed to the restored item.");

        // Clearing returns the full list AND drops the frame.
        after.ClearFilterCommand.Execute(null);
        Assert.Equal(2, after.FilteredSkillItems.OfType<ArtifactRowViewModel>().Count());
        Assert.False(after.FilterFromNavigation);

        vm.Dispose();
    }

    [Fact]
    public async Task Reload_DoesNotBlankAPersistedDeepPath()
    {
        // THE regression guard. During a reload, RestoreSelectedNode sets
        // SelectedNode → OnSelectedNodeChanged → SaveWindowState, and the
        // editor at that instant is freshly built and empty. If the deep path
        // were computed inside SaveWindowState rather than persisted from the
        // _lastDeepPath field, that save would overwrite the good path with an
        // empty one — silently, and only visible as "my place stopped being
        // restored" some launches later.
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        AgentsSkillsEditorViewModel before = await OpenAgentsSkillsAsync(vm);
        before.SelectSegment(AgentsSkillsEditorViewModel.SegmentSkillsId);
        await before.LoadArtifactAsync(
            before.SkillItems.OfType<ArtifactRowViewModel>().First(r => r.DisplayName == "pdf"));

        // Reload while STILL on the artifact — the actual regression scenario.
        // During the rebuild, RestoreSelectedNode re-selects the node, which runs
        // OnSelectedNodeChanged → SaveWindowState against a freshly-built empty
        // editor. The path must survive that.
        await vm.ReloadCommand.ExecuteAsync(null);
        await SettleRestoreAsync(vm);

        string? afterReload = ReadPersistedDeepPath();
        Assert.False(string.IsNullOrEmpty(afterReload),
            "A reload must never blank the persisted deep path.");
        OrdinalAssert.Contains("agents-skills", afterReload!);
        MessageAssert.Contains("pdf", afterReload!,
            "The persisted path must still name the artifact after a reload.");

        vm.Dispose();
    }

    [Fact]
    public async Task NavigatingAway_PersistsTheDeepPath_ThenLeavingForAPlainPageReplacesIt()
    {
        // `_lastDeepPath` must describe where the user ACTUALLY is, not merely the
        // last deep-navigable page they touched. If it only ever recorded artifact
        // positions, navigating to a plain page and reloading would yank the user
        // back to the old artifact.
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        AgentsSkillsEditorViewModel before = await OpenAgentsSkillsAsync(vm);
        before.SelectSegment(AgentsSkillsEditorViewModel.SegmentSkillsId);
        await before.LoadArtifactAsync(
            before.SkillItems.OfType<ArtifactRowViewModel>().First(r => r.DisplayName == "pdf"));

        // Leaving the page is the capture point.
        vm.SelectedNode = vm.NavigationTree.First(
            n => n.NodeId == MainWindowViewModel.NavIdEssentials);

        string? captured = ReadPersistedDeepPath();
        MessageAssert.NotNull(captured, "Navigating away from the page must persist a deep path.");
        OrdinalAssert.Contains("agents-skills", captured!);
        OrdinalAssert.Contains("pdf", captured!);

        // Now leave Essentials too: the path must follow the user, not stay stale.
        vm.SelectedNode = vm.NavigationTree.First(
            n => n.NodeId == MainWindowViewModel.NavIdBackupRestore);

        string? moved = ReadPersistedDeepPath();
        MessageAssert.Equal(MainWindowViewModel.NavIdEssentials, moved,
            "Leaving a plain page must replace the stale artifact path with that page.");

        vm.Dispose();
    }

    [Fact]
    public async Task QuittingWithAnItemOpen_PersistsThatItem()
    {
        // Quitting straight from an open artifact never fires the navigate-away
        // capture, so the shutdown path has to capture explicitly. Without it the
        // next launch restores the page the user was on BEFORE this one.
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        AgentsSkillsEditorViewModel editor = await OpenAgentsSkillsAsync(vm);
        editor.SelectSegment(AgentsSkillsEditorViewModel.SegmentSkillsId);
        await editor.LoadArtifactAsync(
            editor.SkillItems.OfType<ArtifactRowViewModel>().First(r => r.DisplayName == "pdf"));

        // What MainWindow.OnClosed does, in order.
        vm.CaptureDeepPathForShutdown();
        vm.SaveWindowState();

        string? persisted = ReadPersistedDeepPath();
        MessageAssert.NotNull(persisted, "Shutdown must persist the in-page position.");
        OrdinalAssert.Contains("agents-skills", persisted!);
        OrdinalAssert.Contains("skills", persisted!);
        OrdinalAssert.Contains("pdf", persisted!);

        vm.Dispose();
    }

    [Fact]
    public async Task ColdLaunch_RestoresTheItemButNotEditMode()
    {
        // The agreed cold-launch behaviour: locate the item, don't re-enter the
        // editor. The buffer that made an edit meaningful died with the previous
        // process, so re-opening it seeded from disk would look like unsaved work
        // had come back.
        MainWindowViewModel first = BuildViewModel();
        await first.LoadAllWorkspacesAsync();
        AgentsSkillsEditorViewModel editor = await OpenAgentsSkillsAsync(first);
        editor.SelectSegment(AgentsSkillsEditorViewModel.SegmentSkillsId);
        await editor.LoadArtifactAsync(
            editor.SkillItems.OfType<ArtifactRowViewModel>().First(r => r.DisplayName == "pdf"));
        editor.BeginEditCommand.Execute(null);
        editor.EditDescription = "unsaved, will not survive the process";
        first.CaptureDeepPathForShutdown();
        first.SaveWindowState();
        first.Dispose();

        // A brand-new view-model stands in for the next launch; it re-hydrates
        // WindowState from disk in its constructor.
        MainWindowViewModel second = BuildViewModel();
        await second.LoadAllWorkspacesAsync();
        await SettleRestoreAsync(second);

        Assert.Equal(MainWindowViewModel.NavIdAgentsSkills, second.SelectedNode?.NodeId);
        AgentsSkillsEditorViewModel restored = CurrentAgentsSkills(second);
        Assert.Equal(1, restored.SelectedSegmentIndex);
        MessageAssert.Equal("pdf", restored.SelectedArtifact?.DisplayName, "The item must be located…");
        Assert.False(restored.IsEditing, "…but edit mode must NOT be re-entered on a cold launch.");

        second.Dispose();
    }

    [Fact]
    public async Task Reload_DoesNotPersistTheUnsavedEditBuffer()
    {
        // The transient payload is contractually in-memory-only. An unsaved
        // buffer reaching ClaudeForge-gui-state.json would put user content in
        // a UI-state file that is world-readable and never cleaned up.
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        AgentsSkillsEditorViewModel before = await OpenAgentsSkillsAsync(vm);
        before.SelectSegment(AgentsSkillsEditorViewModel.SegmentSkillsId);
        await before.LoadArtifactAsync(
            before.SkillItems.OfType<ArtifactRowViewModel>().First(r => r.DisplayName == "pdf"));
        before.BeginEditCommand.Execute(null);
        before.EditDescription = "SENTINEL-UNSAVED-TEXT";

        await vm.ReloadCommand.ExecuteAsync(null);
        await SettleRestoreAsync(vm);

        string statePath = Path.Combine(_sandbox, ".claude", "cache", "ClaudeForge-gui-state.json");
        if (File.Exists(statePath))
        {
            string json = await File.ReadAllTextAsync(statePath, TestContext.Current.CancellationToken);
            Assert.False(json.Contains("SENTINEL-UNSAVED-TEXT", StringComparison.Ordinal),
                "The unsaved edit buffer must never be written to the UI-state file.");
        }

        vm.Dispose();
    }

    [Fact]
    public async Task Reload_LeavesTheBackStackAlone()
    {
        // A restore is not a user navigation, so it must not offer a Back
        // target — there is nowhere to go back to.
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        AgentsSkillsEditorViewModel before = await OpenAgentsSkillsAsync(vm);
        await before.LoadArtifactAsync(before.SkillItems.OfType<ArtifactRowViewModel>().First());

        await vm.ReloadCommand.ExecuteAsync(null);
        await SettleRestoreAsync(vm);

        Assert.False(vm.CanGoBack, "A deep-path restore must not populate the deep-link back stack.");

        vm.Dispose();
    }

    [Fact]
    public async Task DeepLinkArgument_LandsOnTheTargetItem()
    {
        DebugFlags.Initialize(["--deep-link", "agents-skills/skills/pdf"]);

        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();
        await SettleRestoreAsync(vm);

        MessageAssert.Equal(MainWindowViewModel.NavIdAgentsSkills, vm.SelectedNode?.NodeId,
            "--deep-link must select the addressed page.");

        AgentsSkillsEditorViewModel editor = CurrentAgentsSkills(vm);
        Assert.Equal(1, editor.SelectedSegmentIndex);
        Assert.Equal("pdf", editor.SelectedArtifact?.DisplayName);

        vm.Dispose();
    }

    [Fact]
    public async Task CopyDeepLink_RaisesTheShellStatusPill()
    {
        // The page-local line under the toolbar is easy to miss (11px grey), so the
        // copy also announces through the shell's status pill — the surface this app
        // uses to confirm every other completed action. The page can't reach the
        // status bar directly, so it goes via ShowStatusMessage; this asserts that
        // route is actually wired, not just that the message is sent.
        MainWindowViewModel vm = BuildViewModel();
        await vm.InitializeCommand.ExecuteAsync(null);

        AgentsSkillsEditorViewModel editor = await OpenAgentsSkillsAsync(vm);
        editor.SelectSegment(AgentsSkillsEditorViewModel.SegmentSkillsId);
        await editor.LoadArtifactAsync(
            editor.SkillItems.OfType<ArtifactRowViewModel>().First(r => r.DisplayName == "pdf"));

        Assert.True(editor.CanCopyDeepLink,
            "Precondition: the host must have supplied DeepLinkNodeId.");

        editor.CopyDeepLinkCommand.Execute(null);

        Assert.True(vm.Status.IsSuccess,
            "Copying a deep link must raise the shell status pill "
            + $"(kind was {vm.Status.Kind}, text '{vm.Status.Text}').");
        OrdinalAssert.Contains("pdf", vm.Status.Text);

        // And the page-local line still carries it too.
        OrdinalAssert.Contains("pdf", editor.LastActionMessage);

        vm.Dispose();
    }

    [Fact]
    public async Task DeepLinkArgument_UnresolvablePath_LeavesAVisibleStatusWarning()
    {
        // Regression: the warning is raised inside the nav-tree build, and
        // InitializeAsync unconditionally calls SetStatusState(StatusReady) the
        // moment LoadAllWorkspacesAsync returns. Emitting at detection time meant
        // "Ready" overwrote it microseconds later and the user never saw anything —
        // the same set-then-clobbered ordering trap as the deep-path capture.
        // So it must survive the FULL startup sequence, not just be set somewhere.
        DebugFlags.Initialize(["--deep-link", "no-such-page/no-such-tab"]);

        MainWindowViewModel vm = BuildViewModel();
        await vm.InitializeCommand.ExecuteAsync(null);
        await SettleRestoreAsync(vm);

        Assert.True(vm.Status.IsWarning,
            "An explicitly-typed --deep-link that resolves to nothing must leave a VISIBLE warning "
            + $"(kind was {vm.Status.Kind}, text '{vm.Status.Text}').");
        MessageAssert.Contains("no-such-page", vm.Status.Text,
            "The warning should name the path the user actually typed.");

        vm.Dispose();
    }

    [Fact]
    public async Task DeepLinkArgument_RealPageButUnknownItem_LeavesAVisibleStatusWarning()
    {
        // Regression, found by the maintainer testing the actual shipped feature: the
        // sibling test above only covers a path whose PAGE is bogus, which fails
        // synchronously inside TryQueueDeepRestore where announceFailure lives.
        //
        // The far more likely real-world failure is a REAL page and a REAL tab with a
        // stale item — a shared link to a skill that has since been renamed or deleted.
        // That path resolves at the node level (so TryQueueDeepRestore returns true and
        // never warns) and only fails deep inside the fire-and-forget async restore,
        // whose `false` return used to be logged and dropped. Observed live as
        // "[DeepLink] artifact not found" in the log with a completely silent UI.
        //
        // Both halves of the ordering matter: the restore can finish either side of
        // InitializeAsync's SetStatusState(StatusReady), so the announcement has to be
        // deferred when it lands early and applied directly when it lands late.
        DebugFlags.Initialize(["--deep-link", "agents-skills/skills/definitely-not-a-skill"]);

        MainWindowViewModel vm = BuildViewModel();
        await vm.InitializeCommand.ExecuteAsync(null);
        await SettleRestoreAsync(vm);

        Assert.True(vm.Status.IsWarning,
            "A --deep-link naming a real page but a nonexistent item must leave a VISIBLE warning "
            + $"(kind was {vm.Status.Kind}, text '{vm.Status.Text}').");
        MessageAssert.Contains("definitely-not-a-skill", vm.Status.Text,
            "The warning should name the path the user actually typed.");

        vm.Dispose();
    }

    [Fact]
    public async Task PersistedPathThatNoLongerResolves_StaysSilent()
    {
        // The other half of the contract: a persisted path failing is routine (the
        // artifact was deleted, a profile switch changed what exists). Warning about
        // it on every launch would be nagging, so only an explicit flag announces.
        MainWindowViewModel first = BuildViewModel();
        await first.InitializeCommand.ExecuteAsync(null);
        AgentsSkillsEditorViewModel editor = await OpenAgentsSkillsAsync(first);
        editor.SelectSegment(AgentsSkillsEditorViewModel.SegmentSkillsId);
        await editor.LoadArtifactAsync(
            editor.SkillItems.OfType<ArtifactRowViewModel>().First(r => r.DisplayName == "pdf"));
        first.CaptureDeepPathForShutdown();
        first.SaveWindowState();
        first.Dispose();

        // Delete the artifact behind the persisted path.
        Directory.Delete(Path.Combine(_sandbox, ".claude", "skills", "pdf"), recursive: true);

        MainWindowViewModel second = BuildViewModel();
        await second.InitializeCommand.ExecuteAsync(null);
        await SettleRestoreAsync(second);

        Assert.False(second.Status.IsWarning,
            "A stale PERSISTED path must not raise a warning — only an explicit --deep-link does "
            + $"(kind was {second.Status.Kind}, text '{second.Status.Text}').");

        second.Dispose();
    }

    [Fact]
    public async Task DeepLinkArgument_UnresolvablePath_StillLaunchesNormally()
    {
        // A stale shortcut must degrade to a normal launch, never block it.
        DebugFlags.Initialize(["--deep-link", "no-such-page/no-such-tab"]);

        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();
        await SettleRestoreAsync(vm);

        MessageAssert.NotNull(vm.SelectedNode, "An unresolvable deep link must still land somewhere.");
        Assert.NotEqual(MainWindowViewModel.NavIdAgentsSkills, vm.SelectedNode!.NodeId);

        vm.Dispose();
    }

    [Fact]
    public async Task DeepLinkArgument_IsConsumedOnce_SoALaterReloadDoesNotYankTheUserBack()
    {
        DebugFlags.Initialize(["--deep-link", "agents-skills/skills/pdf"]);

        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();
        await SettleRestoreAsync(vm);
        Assert.Equal(MainWindowViewModel.NavIdAgentsSkills, vm.SelectedNode?.NodeId);

        // User navigates elsewhere, then something triggers a reload.
        vm.SelectedNode = vm.NavigationTree.First(
            n => n.NodeId == MainWindowViewModel.NavIdEssentials);
        await vm.ReloadCommand.ExecuteAsync(null);
        await SettleRestoreAsync(vm);

        MessageAssert.Equal(MainWindowViewModel.NavIdEssentials, vm.SelectedNode?.NodeId,
            "The command-line target must not be re-applied on every reload.");

        vm.Dispose();
    }

    private string? ReadPersistedDeepPath()
    {
        string statePath = Path.Combine(_sandbox, ".claude", "cache", "ClaudeForge-gui-state.json");
        if (!File.Exists(statePath))
        {
            return null;
        }

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(statePath));
        return doc.RootElement.TryGetProperty("lastDeepPath", out JsonElement el)
            ? el.GetString()
            : null;
    }

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
