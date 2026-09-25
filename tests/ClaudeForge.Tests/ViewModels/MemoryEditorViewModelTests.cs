using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.AgentForge.Sdk.Backup;
using Bennewitz.Ninja.AgentForge.Sdk.Env;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Hooks;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Marketplaces;
using Bennewitz.Ninja.AgentForge.Sdk.McpServers;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Models;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Plugins;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.AppServices.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// VM-level tests for <see cref="MemoryEditorViewModel"/>. The VM delegates to
/// the SDK services via a real ClaudeCodeClient instance pointed at a sandbox
/// home directory, so these exercise the wiring (Tier 1 grouping, Tier 2 row
/// rebuild, viewer load) without needing a dialog renderer or shell launcher.
/// </summary>
public sealed class MemoryEditorViewModelTests : IDisposable
{
    private string _fakeHome = null!;
    private string _claudeHome => Path.Combine(_fakeHome, ".claude");

    public MemoryEditorViewModelTests() => Setup();

    private void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(),
            "claudeforge-mem-vm-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_claudeHome);
        PlatformPaths.TestUserProfileOverride = _fakeHome;
    }

    private void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        if (Directory.Exists(_fakeHome))
        {
            try
            {
                Directory.Delete(_fakeHome, recursive: true);
            }
            catch
            {
                /* leave on lock */
            }
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    private void Write(string relPath, string content)
    {
        string full = Path.Combine(_claudeHome, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static MemoryEditorViewModel NewVm(IAgentConfigClient? client)
    {
        return new MemoryEditorViewModel(client, projectRoot: null);
    }

    private FakeClaudeCodeClient NewFakeClient()
    {
        // Re-assert the sandbox override inside the test METHOD's async flow — not
        // only in [TestInitialize]. TestUserProfileOverride is an AsyncLocal (kept
        // that way because AgentForge.Sdk.Tests runs method-level parallel and needs
        // per-flow isolation). Under serial MSTest, the value set in the sync Setup
        // does not always propagate into the async test method's execution context, so
        // FootprintService.DeleteAsync's Task.Run — which reads PlatformPaths.ClaudeHome
        // on a pool thread — occasionally captured a context WITHOUT the override and
        // deleted from the real home instead of the sandbox. That surfaced as the flaky
        // DeleteFootprintAsync_* failures that only appeared in the full suite. Setting
        // it here (every real-delete test funnels through NewFakeClient, on its own
        // flow, before the client is used) makes the later Task.Run capture the sandbox.
        PlatformPaths.TestUserProfileOverride = _fakeHome;
        return new FakeClaudeCodeClient();
    }

    // -----------------------------------------------------------------------

    [Fact]
    public void NoClient_HasCodeMemory_False()
    {
        MemoryEditorViewModel vm = NewVm(client: null);
        Assert.False(vm.HasCodeMemory);
    }

    [Fact]
    public async Task Refresh_RebuildsTier1Groups_OneEntryPerCategory()
    {
        Write("CLAUDE.md", "# top");
        Write("agents/reviewer.md", "# reviewer");
        Write("commands/test.md", "# slash");

        FakeClaudeCodeClient client = NewFakeClient();
        MemoryEditorViewModel vm = NewVm(client);
        await vm.RefreshAsync();

        // One group per category, populated and empty alike. Derived from the enum rather
        // than a hardcoded count so adding a category (e.g. Configuration) doesn't fail
        // this assertion for a reason that has nothing to do with what it's testing.
        Assert.Equal(Enum.GetValues<UserMemoryCategory>().Length, vm.Tier1Groups.Count);
        UserMemoryGroupViewModel primary = vm.Tier1Groups.Single(g => g.Category == UserMemoryCategory.PrimaryMemory);
        UserMemoryGroupViewModel subagent = vm.Tier1Groups.Single(g => g.Category == UserMemoryCategory.Subagent);
        UserMemoryGroupViewModel slash = vm.Tier1Groups.Single(g => g.Category == UserMemoryCategory.SlashCommand);

        Assert.Single(primary.Files);
        Assert.Single(subagent.Files);
        Assert.Single(slash.Files);

        UserMemoryGroupViewModel emptyHook = vm.Tier1Groups.Single(g => g.Category == UserMemoryCategory.Hook);
        Assert.True(emptyHook.IsEmpty);
    }

    [Fact]
    public async Task Refresh_RebuildsFootprintRows_AllCategories()
    {
        Write("history.jsonl", "abcdef");
        Write("projects/repo/session-1.jsonl", "x");

        FakeClaudeCodeClient client = NewFakeClient();
        MemoryEditorViewModel vm = NewVm(client);
        await vm.RefreshAsync();

        Assert.Equal(7, vm.FootprintRows.Count);
        FootprintRowViewModel hist = vm.FootprintRows.Single(r => r.Category == FootprintCategory.PromptHistory);
        Assert.Equal(1, hist.FileCount);
        Assert.Equal(6, hist.TotalBytes);
        OrdinalAssert.Contains("B", hist.HumanSize);
    }

    [Fact]
    public async Task LoadFile_PopulatesViewerContent()
    {
        Write("CLAUDE.md", "hello world");
        FakeClaudeCodeClient client = NewFakeClient();
        MemoryEditorViewModel vm = NewVm(client);
        await vm.RefreshAsync();

        UserMemoryFile primary = vm.Tier1Groups
                                   .Single(g => g.Category == UserMemoryCategory.PrimaryMemory)
                                   .Files[0];

        await vm.LoadFileAsync(primary);

        Assert.True(vm.IsViewerVisible);
        Assert.Equal("hello world", vm.ViewerContent);
        Assert.Same(primary, vm.SelectedFile);
        Assert.False(vm.HasViewerFrontMatter, "Plain content has no front-matter card.");
    }

    [Fact]
    public async Task LoadFile_WithFrontMatter_SplitsCardFromBody()
    {
        Write("CLAUDE.md", "---\nname: Alpha\ndescription: does things\n---\n# Body\n\nHello.\n");
        FakeClaudeCodeClient client = NewFakeClient();
        MemoryEditorViewModel vm = NewVm(client);
        await vm.RefreshAsync();

        UserMemoryFile primary = vm.Tier1Groups
                                   .Single(g => g.Category == UserMemoryCategory.PrimaryMemory)
                                   .Files[0];

        await vm.LoadFileAsync(primary);

        // Front-matter is surfaced as a structured card…
        Assert.True(vm.HasViewerFrontMatter);
        Assert.NotNull(vm.ViewerFrontMatter);
        Assert.Equal("Alpha", vm.ViewerFrontMatter!.Single(r => r.Key == "name").Value);

        // …and stripped from the rendered body.
        OrdinalAssert.Contains("# Body", vm.ViewerContent);
        Assert.False(vm.ViewerContent!.Contains("name:"),
            "Front-matter must not leak into the rendered markdown body.");
        Assert.False(vm.ViewerContent!.TrimStart().StartsWith("---"),
            "Body must not begin with the front-matter delimiter.");
    }

    [Fact]
    public async Task CloseViewer_ClearsSelection()
    {
        Write("CLAUDE.md", "x");
        FakeClaudeCodeClient client = NewFakeClient();
        MemoryEditorViewModel vm = NewVm(client);
        await vm.RefreshAsync();

        UserMemoryFile primary = vm.Tier1Groups
                                   .Single(g => g.Category == UserMemoryCategory.PrimaryMemory)
                                   .Files[0];
        await vm.LoadFileAsync(primary);
        Assert.True(vm.IsViewerVisible);

        vm.CloseViewer();
        Assert.False(vm.IsViewerVisible);
        Assert.Null(vm.SelectedFile);
        Assert.Null(vm.ViewerContent);
    }

    // ── Per-project transcript drilldown ────────────────────

    [Fact]
    public async Task Refresh_PopulatesPerProjectBreakdown_FromSdk()
    {
        // Two projects on disk under ~/.claude/projects/.
        Write("projects/-Users-brian-foo/sess-1.jsonl", "abc");
        Write("projects/-Users-brian-foo/sess-2.jsonl", "abcd");
        Write("projects/-Users-brian-bar/sess-1.jsonl", "ab");

        FakeClaudeCodeClient client = NewFakeClient();
        MemoryEditorViewModel vm = NewVm(client);
        await vm.RefreshAsync();

        Assert.Equal(2, vm.ProjectTranscripts.Count);
        Assert.True(vm.HasProjectBreakdown);

        // Both projects appear (order is most-recent first; we don't assert
        // specific ordering since the temp files were written in a tight
        // window — assert SET equality instead).
        List<string> mangled = vm.ProjectTranscripts.Select(p => p.MangledName).ToList();
        MessageAssert.SameElements(
            new[] { "-Users-brian-foo", "-Users-brian-bar" },
            mangled);
    }

    [Fact]
    public async Task Refresh_NoProjectsOnDisk_BreakdownHidden()
    {
        // No ~/.claude/projects/ directory at all.
        FakeClaudeCodeClient client = NewFakeClient();
        MemoryEditorViewModel vm = NewVm(client);
        await vm.RefreshAsync();

        Assert.Empty(vm.ProjectTranscripts);
        Assert.False(vm.HasProjectBreakdown,
            "Empty projects directory must hide the breakdown Expander.");
    }

    [Fact]
    public async Task Refresh_ProjectsOrderedMostRecentFirst()
    {
        Write("projects/-old-project/s.jsonl", "x");
        string oldFile = Path.Combine(_claudeHome, "projects", "-old-project", "s.jsonl");
        File.SetLastWriteTimeUtc(oldFile, new DateTime(2020, 1, 1));

        Write("projects/-new-project/s.jsonl", "x");
        string newFile = Path.Combine(_claudeHome, "projects", "-new-project", "s.jsonl");
        File.SetLastWriteTimeUtc(newFile, new DateTime(2026, 5, 5));

        FakeClaudeCodeClient client = NewFakeClient();
        MemoryEditorViewModel vm = NewVm(client);
        await vm.RefreshAsync();

        MessageAssert.Equal("-new-project", vm.ProjectTranscripts[0].MangledName,
            "Most-recently-active project must surface first; user mental model "
            + "is 'what was I just doing' so the active project should be on top.");
        Assert.Equal("-old-project", vm.ProjectTranscripts[1].MangledName);
    }

    [Fact]
    public async Task DeleteProjectTranscripts_NoOpsWithNullRow()
    {
        // Defensive: passing null (e.g. from a misconfigured binding) must
        // not crash the page.
        MemoryEditorViewModel vm = NewVm(NewFakeClient());
        await vm.DeleteProjectTranscriptsAsync(null);
        // No assertion — the contract is "doesn't throw".
    }

    [Fact]
    public void ToggleProjectBreakdown_FlipsExpandedFlag()
    {
        MemoryEditorViewModel vm = NewVm(NewFakeClient());
        Assert.False(vm.IsProjectBreakdownExpanded,
            "Per-project breakdown must start collapsed on every navigate-in.");
        vm.ToggleProjectBreakdownCommand.Execute(null);
        Assert.True(vm.IsProjectBreakdownExpanded);
        vm.ToggleProjectBreakdownCommand.Execute(null);
        Assert.False(vm.IsProjectBreakdownExpanded);
    }

    [Fact]
    public async Task ProjectTranscriptRowViewModel_FormattingHelpers()
    {
        // Quick sanity for the row VM's formatting helpers — exercises the
        // FormatBytes branches and the LastWriteDisplay relative-time logic.
        ProjectTranscriptStats oldStats = new(
            MangledName: "-fake",
            DisplayName: "/fake",
            AbsolutePath: "/fake/path",
            FileCount: 3,
            TotalBytes: 2_500_000,
            LastWriteUtc: DateTime.UtcNow.AddDays(-3));
        ProjectTranscriptRowViewModel oldRow = new(oldStats);
        OrdinalAssert.Contains("MB", oldRow.HumanSize);
        OrdinalAssert.Contains("days ago", oldRow.LastWriteDisplay);

        ProjectTranscriptStats minStats = oldStats with { LastWriteUtc = DateTime.MinValue };
        ProjectTranscriptRowViewModel minRow = new(minStats);
        MessageAssert.Equal("—", minRow.LastWriteDisplay,
            "DateTime.MinValue (empty husk) must render as em-dash, not a literal date.");
    }

    // ── Footprint size threshold banner (Phase 5 v2) ─────────────────────

    [Fact]
    public async Task FootprintTotalBytes_SumsAllCategoryRows()
    {
        // Two non-zero categories; total should be the sum of their bytes.
        Write("history.jsonl", new string('a', 1000)); // PromptHistory: 1000 bytes
        Write("bash-commands.log", new string('b', 250)); // BashCommandLog: 250 bytes

        MemoryEditorViewModel vm = NewVm(NewFakeClient());
        await vm.RefreshAsync();

        Assert.Equal(1250, vm.FootprintTotalBytes);
        Assert.Equal("1.2 KB", vm.FootprintTotalDisplay);
    }

    [Fact]
    public async Task IsFootprintLarge_FalseBelowThreshold()
    {
        // Default footprint with tiny test files is far below the 5 GiB
        // threshold — banner must stay hidden.
        Write("history.jsonl", "small");

        MemoryEditorViewModel vm = NewVm(NewFakeClient());
        await vm.RefreshAsync();

        Assert.False(vm.IsFootprintLarge,
            "Footprint warning banner must not surface for normal-sized data.");
    }

    /// <summary>
    /// The Tier-1 memory scan walks
    /// <c>~/.claude/{agents,commands,hooks,plans,rules,skills,…}</c> and reads
    /// the first 4 KiB of every file to extract a subtitle.  On a workstation
    /// with hundreds of memory files (rules/ is walked recursively) this can
    /// take hundreds of milliseconds.  <see cref="MemoryEditorViewModel.RefreshAsync"/>
    /// must offload it to the thread pool so it doesn't freeze the dispatcher
    /// when called from the ctor's fire-and-forget <c>Refresh()</c> on the UI
    /// thread.  This test fails if anyone removes the <c>Task.Run(...)</c>
    /// wrap around <c>SnapshotUserMemoryFiles</c>.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_RunsTier1ScanOnThreadPool()
    {
        // Capture the thread on which SnapshotUserMemoryFiles is invoked.
        bool? snapshotThreadIsPool = (bool?)null;
        ThreadCapturingFakeClient client = new(() =>
            snapshotThreadIsPool = Thread.CurrentThread.IsThreadPoolThread);

        // Run RefreshAsync from a non-thread-pool context.  A normal test
        // method already runs on a thread-pool worker, so we synchronously
        // queue the call on a dedicated thread to give the "caller is NOT
        // the thread pool" baseline a fair shot of being violated if the
        // Task.Run wrap is missing.
        Thread callerThread = new(() => { client.Refresh().GetAwaiter().GetResult(); });
        callerThread.Start();
        callerThread.Join();

        Assert.True(snapshotThreadIsPool, "SnapshotUserMemoryFiles must run on a thread-pool thread — " +
            "did someone remove the Task.Run(...) wrap in MemoryEditorViewModel.RefreshAsync?");
    }

    /// <summary>
    /// Helper for the RefreshAsync_RunsTier1ScanOnThreadPool test.  Captures
    /// the thread context at the moment SnapshotUserMemoryFiles is invoked.
    /// Stripped to the minimum surface — the real FakeClaudeCodeClient
    /// (further down this file) is used by every other test in this class.
    /// </summary>
    private sealed class ThreadCapturingFakeClient
    {
        private readonly Action _onSnapshot;

        public ThreadCapturingFakeClient(Action onSnapshot)
        {
            _onSnapshot = onSnapshot;
        }

        public Task Refresh()
        {
            InnerClient stub = new(_onSnapshot);
            MemoryEditorViewModel vm = new(stub, projectRoot: null);
            return vm.RefreshAsync();
        }

        private sealed class InnerClient : IAgentConfigClient
        {
            private readonly Action _onSnapshot;

            public InnerClient(Action onSnapshot)
            {
                _onSnapshot = onSnapshot;
            }

            public IReadOnlyList<UserMemoryFile> SnapshotUserMemoryFiles(string? projectRoot = null)
            {
                _onSnapshot();
                return Array.Empty<UserMemoryFile>();
            }

            public Task<string?> ReadMemoryFileAsync(string absolutePath, CancellationToken ct)
            {
                return Task.FromResult<string?>(null);
            }

            public Task<IReadOnlyList<FootprintCategoryStats>> GetFootprintStatsAsync(CancellationToken ct)
            {
                return Task.FromResult<IReadOnlyList<FootprintCategoryStats>>(Array.Empty<FootprintCategoryStats>());
            }

            public Task DeleteFootprintCategoryAsync(FootprintCategory category, CancellationToken ct)
            {
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<ProjectTranscriptStats>> GetProjectTranscriptStatsAsync(CancellationToken ct)
            {
                return Task.FromResult<IReadOnlyList<ProjectTranscriptStats>>(Array.Empty<ProjectTranscriptStats>());
            }

            public Task DeleteProjectTranscriptsAsync(string mangledName, CancellationToken ct)
            {
                return Task.CompletedTask;
            }

            public ConfigScope DefaultScope => ConfigScope.User;
            public IReadOnlyList<ConfigScope> EditableScopes => [ConfigScope.User];
            public bool HasUnsavedChanges => false;

            public bool AutoSave
            {
                get => false;
                set { }
            }

            public IPermissionsAccessor Permissions => throw new NotSupportedException();
            public IHooksAccessor Hooks => throw new NotSupportedException();
            public IMcpServersAccessor McpServers => throw new NotSupportedException();
            public IMarketplacesAccessor Marketplaces => throw new NotSupportedException();
            public IEnabledPluginsAccessor Plugins => throw new NotSupportedException();
            public IEnvAccessor Env => throw new NotSupportedException();
            public IModelCatalogAccessor Models => throw new NotSupportedException();
            public IBackupClient Backup => throw new NotSupportedException();

            public event EventHandler<ClientChangedEventArgs>? Changed
            {
                add { }
                remove { }
            }

            public Task OpenAsync(string? projectRoot, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public Task ReloadAsync(CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public Task SaveAsync(bool force, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public Task SaveAsync(bool force, string? headerComment, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public Task<IReadOnlyList<SchemaValidationError>> ValidateAsync(CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public Task<IReadOnlyList<SchemaValidationError>> ValidateAllAsync(CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public T GetEffective<T>(string path)
            {
                throw new NotSupportedException();
            }

            public void SetValue<T>(string path, T value)
            {
                throw new NotSupportedException();
            }

            public void SetValue<T>(string path, T value, ConfigScope scope)
            {
                throw new NotSupportedException();
            }

            public bool RemoveValue(string path)
            {
                throw new NotSupportedException();
            }

            public void RemoveValue(string path, ConfigScope scope)
            {
                throw new NotSupportedException();
            }

            public IReadOnlyList<SchemaSearchResult> SearchSchema(string query, int max)
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
            }
        }
    }

    [Fact]
    public void IsFootprintLarge_TrueAboveThreshold()
    {
        // Force the property directly; we don't need real disk to validate
        // the threshold predicate.  Threshold constant is 5 GiB.
        MemoryEditorViewModel vm = NewVm(client: null);
        vm.FootprintTotalBytes = MemoryEditorViewModel.FootprintWarningThresholdBytes + 1;
        Assert.True(vm.IsFootprintLarge);
        OrdinalAssert.Contains("GB", vm.FootprintTotalDisplay);
        OrdinalAssert.Contains("5.0 GB", vm.FootprintWarningMessage);
    }

    [Fact]
    public void FootprintWarningMessage_ContainsHumanisedSize()
    {
        MemoryEditorViewModel vm = NewVm(client: null);
        vm.FootprintTotalBytes = 12L * 1024 * 1024 * 1024; // 12 GiB
        MessageAssert.Contains("12.0 GB", vm.FootprintWarningMessage,
            "Banner copy must include the humanised aggregate size for context.");
    }

    [Fact]
    public void FootprintTotalBytes_PropertyChange_NotifiesDerivedProperties()
    {
        MemoryEditorViewModel vm = NewVm(client: null);
        HashSet<string> fired = new();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
            {
                fired.Add(e.PropertyName);
            }
        };

        vm.FootprintTotalBytes = MemoryEditorViewModel.FootprintWarningThresholdBytes + 1;

        OrdinalAssert.Contains(nameof(MemoryEditorViewModel.FootprintTotalBytes), fired);
        OrdinalAssert.Contains(nameof(MemoryEditorViewModel.IsFootprintLarge), fired);
        OrdinalAssert.Contains(nameof(MemoryEditorViewModel.FootprintTotalDisplay), fired);
        Assert.True(fired.Contains(nameof(MemoryEditorViewModel.FootprintWarningMessage)),
            "All three derived properties must fire so the AXAML banner Visibility, "
            + "header text, and body text all update on threshold crossings.");
    }

    [Fact]
    public void EveryUserMemoryCategory_HasNonEmptyTooltip()
    {
        // Regression lock: adding a new UserMemoryCategory enum value
        // must also add a Tooltip mapping, otherwise the new category's
        // accordion header will silently lose its hover description.
        foreach (UserMemoryCategory cat in Enum.GetValues(typeof(UserMemoryCategory)))
        {
            UserMemoryGroupViewModel vm = new(cat, []);
            Assert.False(string.IsNullOrWhiteSpace(vm.Tooltip),
                $"UserMemoryCategory.{cat} has no Tooltip mapping in UserMemoryGroupViewModel.Tooltip.");
        }
    }

    [Fact]
    public void EveryFootprintCategory_HasNonEmptyTooltip()
    {
        // Same regression lock for FootprintRowViewModel.Tooltip.
        //
        // ⚠ This said `Enum.GetValues(typeof(FootprintCategory))` until the category set moved into
        // product data. That is a REFLECTION call, so it kept compiling after the type stopped
        // being an enum and failed at run time with "Type provided must be an Enum" — the one call
        // site in this conversion the compiler could not point at. `.All` is the replacement.
        foreach (FootprintCategory cat in FootprintCategory.All)
        {
            FootprintCategoryStats stats = new(
                Category: cat,
                AbsolutePath: "/fake",
                FileCount: 0,
                TotalBytes: 0,
                IsInStandardBackup: true);
            FootprintRowViewModel row = new(stats);
            Assert.False(string.IsNullOrWhiteSpace(row.Tooltip),
                $"FootprintCategory.{cat} has no Tooltip mapping in FootprintRowViewModel.Tooltip.");
        }
    }

    // ── DeleteFootprintAsync coverage ──
    //
    // Six tests covering MemoryEditorViewModel.DeleteFootprintAsync (32 lines, 0%
    // until this commit).  Each isolates one branch of the method's decision
    // tree: null guards, dialog confirm/decline/X-dismiss, no-dialog fast-path.

    [Fact]
    public async Task DeleteFootprintAsync_NullRow_NoOp()
    {
        // First guard: row is null → return immediately, no dialog, no delete.
        StubDialogService dlg = new();
        FakeClaudeCodeClient client = NewFakeClient();
        MemoryEditorViewModel vm = new(client, projectRoot: null, dialogService: dlg, shellLauncher: null);

        await vm.DeleteFootprintAsync(null);

        MessageAssert.Equal(0, dlg.ConfirmCalls,
            "Null row must short-circuit before any dialog appears.");
    }

    [Fact]
    public async Task DeleteFootprintAsync_NullCodeClient_NoOp()
    {
        // Second guard: _codeClient is null → return immediately, no dialog.
        StubDialogService dlg = new();
        MemoryEditorViewModel vm = new(codeClient: null, projectRoot: null, dialogService: dlg,
            shellLauncher: null);
        FootprintRowViewModel row = StubRow(FootprintCategory.PromptHistory);

        await vm.DeleteFootprintAsync(row);

        MessageAssert.Equal(0, dlg.ConfirmCalls,
            "Null code client must short-circuit before any dialog appears.");
    }

    [Fact]
    public async Task DeleteFootprintAsync_UserDeclinesConfirm_NoDelete()
    {
        // Pre-populate a PromptHistory file; user clicks Cancel → file survives.
        Write("history.jsonl", "{\"prompt\":\"hello\"}");
        string historyPath = Path.Combine(_claudeHome, "history.jsonl");
        Assert.True(File.Exists(historyPath));

        StubDialogService dlg = new() { ConfirmReturns = false };
        FakeClaudeCodeClient client = NewFakeClient();
        MemoryEditorViewModel vm = new(client, projectRoot: null, dialogService: dlg, shellLauncher: null);
        FootprintRowViewModel row = StubRow(FootprintCategory.PromptHistory);

        await vm.DeleteFootprintAsync(row);

        MessageAssert.Equal(1, dlg.ConfirmCalls, "Confirm dialog must have appeared once.");
        Assert.True(File.Exists(historyPath),
            "User declined → file must survive.");
    }

    [Fact]
    public async Task DeleteFootprintAsync_UserDismissesViaX_NoDelete()
    {
        // Pre-populate; dialog returns null (X-close) → file survives.
        // Per docs/CLAUDE.md the X-dismiss contract: null is "abort," NEVER
        // a synonym for cancel-side proceed.
        Write("history.jsonl", "{\"prompt\":\"hello\"}");
        string historyPath = Path.Combine(_claudeHome, "history.jsonl");

        StubDialogService dlg = new() { ConfirmReturnsNull = true };
        FakeClaudeCodeClient client = NewFakeClient();
        MemoryEditorViewModel vm = new(client, projectRoot: null, dialogService: dlg, shellLauncher: null);
        FootprintRowViewModel row = StubRow(FootprintCategory.PromptHistory);

        await vm.DeleteFootprintAsync(row);

        Assert.Equal(1, dlg.ConfirmCalls);
        Assert.True(File.Exists(historyPath),
            "X-dismissed dialog → file must survive (universal X-close-aborts contract).");
    }

    [Fact]
    public async Task DeleteFootprintAsync_UserConfirms_DeletesFileAndRebuildsRows()
    {
        // Pre-populate; user clicks Confirm → file deleted, FootprintRows refreshed.
        Write("history.jsonl", "{\"prompt\":\"hello\"}");
        string historyPath = Path.Combine(_claudeHome, "history.jsonl");

        StubDialogService dlg = new() { ConfirmReturns = true };
        FakeClaudeCodeClient client = NewFakeClient();
        MemoryEditorViewModel vm = new(client, projectRoot: null, dialogService: dlg, shellLauncher: null);
        FootprintRowViewModel row = StubRow(FootprintCategory.PromptHistory);

        await vm.DeleteFootprintAsync(row);

        Assert.Equal(1, dlg.ConfirmCalls);
        Assert.False(File.Exists(historyPath),
            "User confirmed → file must be deleted.");
        Assert.False(vm.IsBusy, "IsBusy must reset to false in the finally block.");
    }

    [Fact]
    public async Task DeleteFootprintAsync_NoDialogService_DeletesImmediately()
    {
        // Headless / scripted scenario: no IDialogService injected → no
        // confirm prompt, delete fires unconditionally.  Verifies the
        // _dialogService null-check guards the dialog block only, not
        // the delete block.
        Write("history.jsonl", "{\"prompt\":\"hello\"}");
        string historyPath = Path.Combine(_claudeHome, "history.jsonl");

        FakeClaudeCodeClient client = NewFakeClient();
        MemoryEditorViewModel vm = new(client, projectRoot: null, dialogService: null, shellLauncher: null);
        FootprintRowViewModel row = StubRow(FootprintCategory.PromptHistory);

        await vm.DeleteFootprintAsync(row);

        Assert.False(File.Exists(historyPath),
            "No dialog service → delete proceeds without confirmation.");
    }

    // ── DeleteUserMemoryFileAsync coverage ──
    //
    // Mirrors the footprint branch matrix for Tier 1 user-memory files: null
    // guard, the cross-tool non-deletable guard, dialog decline, confirm-deletes,
    // the no-dialog fast path, and skill = whole-directory delete.

    [Fact]
    public async Task DeleteUserMemoryFileAsync_NullFile_NoOp()
    {
        StubDialogService dlg = new();
        MemoryEditorViewModel vm = new(NewFakeClient(), projectRoot: null, dialogService: dlg, shellLauncher: null);
        await vm.DeleteUserMemoryFileAsync(null);
        Assert.Equal(0, dlg.ConfirmCalls);
    }

    [Fact]
    public async Task DeleteUserMemoryFileAsync_CrossToolFile_NotDeletable_NoOp()
    {
        // Cross-tool memory (Codex/Gemini/OpenCode) is owned by another tool — the
        // Delete command must short-circuit before any dialog or delete.
        string crossPath = Path.Combine(_fakeHome, ".codex", "AGENTS.md");
        Directory.CreateDirectory(Path.GetDirectoryName(crossPath)!);
        File.WriteAllText(crossPath, "codex memory");

        UserMemoryFile crossTool = new(
            AbsolutePath: crossPath,
            Category: UserMemoryCategory.CrossToolMemory,
            DisplayName: "AGENTS",
            SizeBytes: 12,
            LastWriteUtc: DateTime.UtcNow,
            Subtitle: null);
        Assert.False(crossTool.IsDeletable, "Cross-tool memory must report as non-deletable.");

        StubDialogService dlg = new() { ConfirmReturns = true };
        MemoryEditorViewModel vm = new(NewFakeClient(), projectRoot: null, dialogService: dlg, shellLauncher: null);

        await vm.DeleteUserMemoryFileAsync(crossTool);

        MessageAssert.Equal(0, dlg.ConfirmCalls, "Non-deletable cross-tool row must not even prompt.");
        Assert.True(File.Exists(crossPath), "Another tool's file must never be deleted.");
    }

    [Fact]
    public async Task DeleteUserMemoryFileAsync_UserDeclines_NoDelete()
    {
        Write("agents/reviewer.md", "# reviewer");
        string path = Path.Combine(_claudeHome, "agents", "reviewer.md");

        StubDialogService dlg = new() { ConfirmReturns = false };
        MemoryEditorViewModel vm = new(NewFakeClient(), projectRoot: null, dialogService: dlg, shellLauncher: null);
        await vm.RefreshAsync();
        UserMemoryFile file = vm.Tier1Groups.Single(g => g.Category == UserMemoryCategory.Subagent).Files[0];

        await vm.DeleteUserMemoryFileAsync(file);

        Assert.Equal(1, dlg.ConfirmCalls);
        Assert.True(File.Exists(path), "Declined confirm → file survives.");
    }

    [Fact]
    public async Task DeleteUserMemoryFileAsync_UserConfirms_DeletesFileAndRebuilds()
    {
        Write("agents/reviewer.md", "# reviewer");
        string path = Path.Combine(_claudeHome, "agents", "reviewer.md");

        StubDialogService dlg = new() { ConfirmReturns = true };
        MemoryEditorViewModel vm = new(NewFakeClient(), projectRoot: null, dialogService: dlg, shellLauncher: null);
        await vm.RefreshAsync();
        UserMemoryFile file = vm.Tier1Groups.Single(g => g.Category == UserMemoryCategory.Subagent).Files[0];

        await vm.DeleteUserMemoryFileAsync(file);

        Assert.False(File.Exists(path), "Confirmed → file deleted.");
        Assert.True(vm.Tier1Groups.Single(g => g.Category == UserMemoryCategory.Subagent).IsEmpty,
            "Tier 1 group must rebuild empty after the delete.");
        Assert.False(vm.IsBusy, "IsBusy must reset in the finally block.");
    }

    [Fact]
    public async Task DeleteUserMemoryFileAsync_Skill_DeletesWholeDirectory()
    {
        Write("skills/pdf/SKILL.md", "---\nname: pdf\n---\n");
        Write("skills/pdf/run.py", "print('x')");
        string skillDir = Path.Combine(_claudeHome, "skills", "pdf");

        StubDialogService dlg = new() { ConfirmReturns = true };
        MemoryEditorViewModel vm = new(NewFakeClient(), projectRoot: null, dialogService: dlg, shellLauncher: null);
        await vm.RefreshAsync();
        UserMemoryFile skill = vm.Tier1Groups.Single(g => g.Category == UserMemoryCategory.Skill).Files[0];
        Assert.True(skill.IsSkill);

        await vm.DeleteUserMemoryFileAsync(skill);

        Assert.False(Directory.Exists(skillDir),
            "Deleting a skill removes its whole directory, not just SKILL.md.");
    }

    [Fact]
    public async Task DeleteUserMemoryFileAsync_NoDialogService_DeletesImmediately()
    {
        Write("commands/summarise.md", "# summarise");
        string path = Path.Combine(_claudeHome, "commands", "summarise.md");

        MemoryEditorViewModel vm = new(NewFakeClient(), projectRoot: null, dialogService: null, shellLauncher: null);
        await vm.RefreshAsync();
        UserMemoryFile file = vm.Tier1Groups.Single(g => g.Category == UserMemoryCategory.SlashCommand).Files[0];

        await vm.DeleteUserMemoryFileAsync(file);

        Assert.False(File.Exists(path), "No dialog service → delete proceeds without confirmation.");
    }

    [Fact]
    public void UserMemoryFile_IsDeletable_FalseOnlyForCrossTool()
    {
        foreach (UserMemoryCategory cat in Enum.GetValues<UserMemoryCategory>())
        {
            UserMemoryFile f = new("/p/x.md", cat, "x", 1, DateTime.UtcNow, null);
            bool expected = cat != UserMemoryCategory.CrossToolMemory;
            MessageAssert.Equal(expected, f.IsDeletable, $"IsDeletable wrong for {cat}.");
        }
    }

    /// <summary>
    /// Helper: synthesise a FootprintRowViewModel for the given category
    /// without going through the full vm.Refresh() machinery.
    /// </summary>
    private static FootprintRowViewModel StubRow(FootprintCategory cat)
    {
        return new FootprintRowViewModel(new FootprintCategoryStats(
            Category: cat,
            AbsolutePath: "/fake/path",
            FileCount: 1,
            TotalBytes: 100,
            IsInStandardBackup: false));
    }

    /// <summary>
    /// Minimal IDialogService stub for DeleteFootprintAsync tests.  Mirrors
    /// the StubDialogService pattern in ProfilesViewModelTests but with
    /// trinary confirm-result support (true / false / null=X-close).
    /// </summary>
    private sealed class StubDialogService : IDialogService
    {
        public bool ConfirmReturns { get; set; }
        public bool ConfirmReturnsNull { get; set; }
        public int ConfirmCalls { get; private set; }

        public Task<string?> PickFolderAsync(string? title = null)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickFileAsync(string? title = null,
                                           IReadOnlyList<FilePickerFilter>? filters = null)
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

        public Task<bool?> ShowConfirmAsync(string title, string message,
                                            string confirmLabel = "Confirm", string cancelLabel = "Cancel")
        {
            ConfirmCalls++;
            return Task.FromResult<bool?>(ConfirmReturnsNull ? null : ConfirmReturns);
        }

        public Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt)
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Minimal client stub — exposes only the four IAgentConfigClient methods
    /// the Memory page touches. Avoids the full ClaudeCodeClient open/discover
    /// machinery so tests stay fast and don't require fixture settings files.
    /// </summary>
    private sealed class FakeClaudeCodeClient : IAgentConfigClient
    {
        // ── Memory + footprint methods (delegate to the static SDK helpers) ─
        public IReadOnlyList<UserMemoryFile> SnapshotUserMemoryFiles(string? projectRoot = null)
        {
            return UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty, projectRoot);
        }

        public Task<string?> ReadMemoryFileAsync(string absolutePath, CancellationToken ct)
        {
            return UserMemoryService.ReadAsync(absolutePath, ct);
        }

        public Task<IReadOnlyList<FootprintCategoryStats>> GetFootprintStatsAsync(CancellationToken ct)
        {
            return new FootprintService(() => ClaudeArtifactPaths.DefaultFor(ClaudeEnvironment.Empty), FootprintCatalog.Default).GetStatsAsync(ct);
        }

        public Task DeleteFootprintCategoryAsync(FootprintCategory category, CancellationToken ct)
        {
            return new FootprintService(() => ClaudeArtifactPaths.DefaultFor(ClaudeEnvironment.Empty), FootprintCatalog.Default).DeleteAsync(category, ct);
        }

        public Task<IReadOnlyList<ProjectTranscriptStats>> GetProjectTranscriptStatsAsync(CancellationToken ct)
        {
            return new FootprintService(() => ClaudeArtifactPaths.DefaultFor(ClaudeEnvironment.Empty), FootprintCatalog.Default).GetProjectTranscriptStatsAsync(ct);
        }

        public Task DeleteProjectTranscriptsAsync(string mangledName, CancellationToken ct)
        {
            return new FootprintService(() => ClaudeArtifactPaths.DefaultFor(ClaudeEnvironment.Empty), FootprintCatalog.Default).DeleteProjectTranscriptsAsync(mangledName, ct);
        }

        // ── Everything else: throw — these tests don't touch the rest of the surface. ─
        public ConfigScope DefaultScope => ConfigScope.User;
        public IReadOnlyList<ConfigScope> EditableScopes => [ConfigScope.User];
        public bool HasUnsavedChanges => false;

        public bool AutoSave
        {
            get => false;
            set { }
        }

        public IPermissionsAccessor Permissions => throw new NotSupportedException();
        public IHooksAccessor Hooks => throw new NotSupportedException();
        public IMcpServersAccessor McpServers => throw new NotSupportedException();
        public IMarketplacesAccessor Marketplaces => throw new NotSupportedException();
        public IEnabledPluginsAccessor Plugins => throw new NotSupportedException();
        public IEnvAccessor Env => throw new NotSupportedException();
        public IModelCatalogAccessor Models => throw new NotSupportedException();
        public IBackupClient Backup => throw new NotSupportedException();

        public event EventHandler<ClientChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task OpenAsync(string? projectRoot, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task ReloadAsync(CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task SaveAsync(bool force, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task SaveAsync(bool force, string? headerComment, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<SchemaValidationError>> ValidateAsync(CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<SchemaValidationError>> ValidateAllAsync(CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public T GetEffective<T>(string path)
        {
            throw new NotSupportedException();
        }

        public void SetValue<T>(string path, T value)
        {
            throw new NotSupportedException();
        }

        public void SetValue<T>(string path, T value, ConfigScope scope)
        {
            throw new NotSupportedException();
        }

        public bool RemoveValue(string path)
        {
            throw new NotSupportedException();
        }

        public void RemoveValue(string path, ConfigScope scope)
        {
            throw new NotSupportedException();
        }

        public IReadOnlyList<SchemaSearchResult> SearchSchema(string query, int max)
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }
}