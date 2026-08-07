using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Guard tests for <see cref="NavigationNodeViewModel.NodeId"/> — the stable,
/// culture-invariant key that deep links and the persisted deep path resolve
/// against.
///
/// <para>
/// Two invariants, both silent-failure-shaped if broken. A node built without a
/// <c>NodeId</c> becomes permanently unaddressable — a deep link to it just
/// falls back to the default page with nothing to indicate why. Two SIBLINGS
/// sharing an id makes the path ambiguous, and resolution picks whichever comes
/// first in the tree, so a link lands on the wrong page.
/// </para>
/// <para>
/// Uniqueness is asserted <b>per parent</b>, not tree-wide, and that is
/// deliberate: <c>version-info</c> legitimately exists under both product
/// headers, and a settings-group name may repeat across products. The path
/// grammar is <c>&lt;parent-id&gt;/&lt;child-id&gt;</c>, so sibling-scoped
/// uniqueness is exactly what it requires — asserting global uniqueness here
/// would be wrong and would fail on the real tree.
/// </para>
/// </summary>
[TestClass]
public sealed class NavigationNodeIdTests
{
    private string _sandbox = null!;
    private MainWindowViewModel _vm = null!;

    [TestInitialize]
    public void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        Directory.CreateDirectory(Path.Combine(_sandbox, ".claude"));
        PlatformPaths.TestUserProfileOverride = _sandbox;

        _vm = new MainWindowViewModel(new SchemaRegistry(), new NullDialogService());
    }

    [TestCleanup]
    public void Cleanup()
    {
        _vm.Dispose();
        PlatformPaths.TestUserProfileOverride = null;
        if (Directory.Exists(_sandbox))
        {
            TestCleanupHelpers.DeleteDirectoryWithRetry(_sandbox);
        }
    }

    [TestMethod]
    public async Task EveryNonDividerNode_HasANodeId()
    {
        await _vm.InitializeCommand.ExecuteAsync(null);

        List<string> missing = [];
        foreach (NavigationNodeViewModel node in _vm.NavigationTree)
        {
            if (!node.IsDivider && string.IsNullOrEmpty(node.NodeId))
            {
                missing.Add(node.Title);
            }

            foreach (NavigationNodeViewModel child in node.Children)
            {
                if (!child.IsDivider && string.IsNullOrEmpty(child.NodeId))
                {
                    missing.Add($"{node.Title}/{child.Title}");
                }
            }
        }

        Assert.AreEqual(
            0,
            missing.Count,
            "Every non-divider nav node needs a NodeId or it can never be deep-linked. Missing: "
            + string.Join(", ", missing));
    }

    [TestMethod]
    public async Task Dividers_HaveNoNodeId()
    {
        await _vm.InitializeCommand.ExecuteAsync(null);

        // Several dividers share one placeholder title and none is selectable,
        // so giving them ids would both be meaningless and break the
        // sibling-uniqueness invariant below.
        foreach (NavigationNodeViewModel node in _vm.NavigationTree.Where(n => n.IsDivider))
        {
            Assert.IsNull(node.NodeId, "Divider nodes must not carry a NodeId.");
        }
    }

    [TestMethod]
    public async Task TopLevelNodeIds_AreUnique()
    {
        await _vm.InitializeCommand.ExecuteAsync(null);

        List<string> ids = _vm.NavigationTree
                              .Where(n => !n.IsDivider && !string.IsNullOrEmpty(n.NodeId))
                              .Select(n => n.NodeId!)
                              .ToList();

        List<string> duplicates = ids.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                                     .Where(g => g.Count() > 1)
                                     .Select(g => g.Key)
                                     .ToList();

        Assert.AreEqual(
            0,
            duplicates.Count,
            "Duplicate top-level NodeIds make a deep link ambiguous: " + string.Join(", ", duplicates));
    }

    [TestMethod]
    public async Task ChildNodeIds_AreUniqueWithinTheirParent()
    {
        await _vm.InitializeCommand.ExecuteAsync(null);

        List<string> problems = [];
        foreach (NavigationNodeViewModel parent in _vm.NavigationTree)
        {
            List<string> ids = parent.Children
                                     .Where(c => !c.IsDivider && !string.IsNullOrEmpty(c.NodeId))
                                     .Select(c => c.NodeId!)
                                     .ToList();

            problems.AddRange(
                ids.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                   .Where(g => g.Count() > 1)
                   .Select(g => $"{parent.Title}/{g.Key}"));
        }

        Assert.AreEqual(
            0,
            problems.Count,
            "Sibling NodeIds must be unique or a deep link resolves to the wrong child: "
            + string.Join(", ", problems));
    }

    [TestMethod]
    public async Task KnownNodeIds_ResolveThroughNavDeepPath()
    {
        await _vm.InitializeCommand.ExecuteAsync(null);

        // End-to-end sanity: the ids the tree actually carries are the ones the
        // grammar can address. Guards against a slug drifting from its constant.
        Assert.IsTrue(
            NavDeepPath.Resolve([MainWindowViewModel.NavIdAgentsSkills], _vm.NavigationTree).Resolved,
            "agents-skills must resolve.");
        Assert.IsTrue(
            NavDeepPath.Resolve([MainWindowViewModel.NavIdEssentials], _vm.NavigationTree).Resolved,
            "essentials must resolve.");

        NavDeepPathResolution cc = NavDeepPath.Resolve([MainWindowViewModel.NavIdClaudeCode], _vm.NavigationTree);
        Assert.IsTrue(cc.Resolved, "claude-code must resolve.");
        Assert.IsTrue(cc.Node!.Children.Count > 0, "Precondition: the Claude Code header should have children.");
    }

    [TestMethod]
    public async Task SettingsGroupChildIds_MatchTheirSluggedTitle()
    {
        await _vm.InitializeCommand.ExecuteAsync(null);

        NavigationNodeViewModel? cc = _vm.NavigationTree
                                         .FirstOrDefault(n => n.NodeId == MainWindowViewModel.NavIdClaudeCode);
        Assert.IsNotNull(cc);

        // Every settings-group child derives its id from its title, so a
        // deep-link author can predict the id from what the sidebar shows.
        // Version Information is the one explicitly-assigned exception.
        foreach (NavigationNodeViewModel child in cc!.Children)
        {
            if (child.NodeId == MainWindowViewModel.NavIdVersionInfo)
            {
                continue;
            }

            Assert.AreEqual(
                NavDeepPath.Slug(child.Title),
                child.NodeId,
                $"Group child '{child.Title}' should carry the slug of its title.");
        }
    }

    [TestMethod]
    public async Task KnownTopLevelNodeIds_MatchTheBuiltTree()
    {
        // The list backs the usage message a rejected --deep-link prints to the
        // terminal. A hand-maintained mirror of the tree is exactly the kind of
        // parallel list that drifts, so pin it to reality: every advertised id must
        // exist, and every addressable top-level page must be advertised.
        await _vm.InitializeCommand.ExecuteAsync(null);

        HashSet<string> actual = _vm.NavigationTree
                                    .Where(n => !n.IsDivider && !string.IsNullOrEmpty(n.NodeId))
                                    .Select(n => n.NodeId!)
                                    .ToHashSet(StringComparer.Ordinal);

        List<string> advertisedButAbsent = MainWindowViewModel.KnownTopLevelNodeIds
                                                              .Where(id => !actual.Contains(id))
                                                              .ToList();
        List<string> presentButUnadvertised = actual
                                              .Where(id => !MainWindowViewModel.KnownTopLevelNodeIds.Contains(id))
                                              .ToList();

        // Welcome is preference-gated, so it can legitimately be advertised while
        // absent from this particular tree.
        advertisedButAbsent.Remove(MainWindowViewModel.NavIdWelcome);

        Assert.AreEqual(0, advertisedButAbsent.Count,
            "Advertised ids that don't exist in the tree: " + string.Join(", ", advertisedButAbsent));
        Assert.AreEqual(0, presentButUnadvertised.Count,
            "Addressable top-level pages missing from the usage message: "
            + string.Join(", ", presentButUnadvertised));
    }

    // File-local stub, matching the convention in the sibling nav tests
    // (NavigationHeaderClickTests, Headless/ReloadHardeningTests): each test file
    // carries its own rather than sharing one helper.
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
