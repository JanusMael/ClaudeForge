using System.ComponentModel;
using System.IO;
using System.Linq;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Filter contract for the "Agents &amp; Skills" page.
///
/// <para>
/// Three of these guard silent-failure modes rather than ordinary behaviour:
/// </para>
/// <list type="bullet">
///   <item>
///     <see cref="RefreshAsync_RaisesFilteredListNotifications"/> — the view
///     binds the COMPUTED <c>Filtered*</c> projections, so the
///     <c>ObservableCollection</c> mutations inside <c>FillGrouped</c> no longer
///     reach the UI on their own. Drop the explicit notification and the lists
///     simply stop updating on refresh, with nothing failing loudly.
///   </item>
///   <item>
///     <see cref="ApplyFilter_ProjectsTheSameRowInstances"/> — filtering must not
///     rebuild rows, or per-row state (the <c>IsSelected</c> groundwork for
///     multi-select) would vanish whenever the user narrows the list.
///   </item>
///   <item>
///     <see cref="ApplyNavigationFilter_FlagsNavigationThenUserEditClearsIt"/> —
///     a deep link applies its filter through <c>ApplyNavigationFilter</c> so the
///     "navigated" frame appears; assigning <c>FilterText</c> directly would read
///     as a user edit and skip it.
///   </item>
/// </list>
/// </summary>
public sealed class AgentsSkillsFilterTests : IDisposable
{
    private string _sandbox = string.Empty;
    private string _project = string.Empty;

    public AgentsSkillsFilterTests() => Setup();

    private void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_asfilter_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        _project = Path.Combine(Path.GetTempPath(), "claudetest_asfilter_proj_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_project);
    }

    private void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        foreach (string dir in new[] { _sandbox, _project })
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    private string Home => Path.Combine(_sandbox, ".claude");

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    // ── ApplyFilter: pure, no VM needed ──────────────────────────────────

    private static ArtifactRowViewModel Row(string name, string source, string? subtitle = null)
    {
        var entry = new EditableMemoryEntry(
            AbsolutePath: Path.Combine(Path.GetTempPath(), name + ".md"),
            Category: UserMemoryCategory.Skill,
            Scope: source == "Plugin" ? EditableMemoryScope.Plugin : EditableMemoryScope.User,
            DisplayName: name,
            Source: source,
            IsWritable: source != "Plugin",
            SizeBytes: 0,
            LastWriteUtc: DateTime.UnixEpoch);
        return new ArtifactRowViewModel(entry) { Subtitle = subtitle };
    }

    [Fact]
    public void ApplyFilter_EmptyFilter_ReturnsEverythingIncludingHeaders()
    {
        List<object> flat =
        [
            new ArtifactSectionHeaderViewModel("Yours", IsReadOnly: false),
            Row("alpha", "User"),
            Row("beta", "User"),
        ];

        List<object> result = AgentsSkillsEditorViewModel.ApplyFilter(flat, string.Empty);

        Assert.Equal(3, result.Count);
        Assert.Equal(flat, result);
    }

    [Fact]
    public void ApplyFilter_MatchesName_CaseInsensitively()
    {
        List<object> flat = [Row("AlphaSkill", "User"), Row("beta", "User")];

        List<object> result = AgentsSkillsEditorViewModel.ApplyFilter(flat, "alpha");

        Assert.Single(result);
        Assert.Equal("AlphaSkill", ((ArtifactRowViewModel)result[0]).DisplayName);
    }

    [Fact]
    public void ApplyFilter_MatchesDescription()
    {
        List<object> flat =
        [
            Row("alpha", "User", "Converts PDF documents"),
            Row("beta", "User", "Unrelated"),
        ];

        List<object> result = AgentsSkillsEditorViewModel.ApplyFilter(flat, "pdf");

        Assert.Single(result);
        Assert.Equal("alpha", ((ArtifactRowViewModel)result[0]).DisplayName);
    }

    [Fact]
    public void ApplyFilter_MatchesSource()
    {
        List<object> flat = [Row("alpha", "User"), Row("beta", "Plugin")];

        List<object> result = AgentsSkillsEditorViewModel.ApplyFilter(flat, "plugin");

        Assert.Single(result);
        Assert.Equal("beta", ((ArtifactRowViewModel)result[0]).DisplayName);
    }

    [Fact]
    public void ApplyFilter_DropsHeaderWhoseGroupHasNoSurvivingRow()
    {
        List<object> flat =
        [
            new ArtifactSectionHeaderViewModel("Yours", IsReadOnly: false),
            Row("alpha", "User"),
            new ArtifactSectionHeaderViewModel("Plugin", IsReadOnly: true),
            Row("beta", "Plugin"),
        ];

        // "alpha" only matches under Yours, so the Plugin header must not survive
        // as an orphan above nothing.
        List<object> result = AgentsSkillsEditorViewModel.ApplyFilter(flat, "alpha");

        Assert.Equal(2, result.Count);
        Assert.Equal("Yours", ((ArtifactSectionHeaderViewModel)result[0]).Header);
        Assert.Equal("alpha", ((ArtifactRowViewModel)result[1]).DisplayName);
    }

    [Fact]
    public void ApplyFilter_KeepsHeaderWhenARowBeneathItSurvives()
    {
        List<object> flat =
        [
            new ArtifactSectionHeaderViewModel("Yours", IsReadOnly: false),
            Row("alpha", "User"),
            new ArtifactSectionHeaderViewModel("Plugin", IsReadOnly: true),
            Row("alpha-plugin", "Plugin"),
        ];

        List<object> result = AgentsSkillsEditorViewModel.ApplyFilter(flat, "alpha");

        MessageAssert.Equal(4, result.Count, "Both groups have a match, so both headers stay.");
        Assert.Equal("Yours", ((ArtifactSectionHeaderViewModel)result[0]).Header);
        Assert.Equal("Plugin", ((ArtifactSectionHeaderViewModel)result[2]).Header);
    }

    [Fact]
    public void ApplyFilter_NoMatches_ReturnsEmptyWithNoHeaders()
    {
        List<object> flat =
        [
            new ArtifactSectionHeaderViewModel("Yours", IsReadOnly: false),
            Row("alpha", "User"),
        ];

        Assert.Empty(AgentsSkillsEditorViewModel.ApplyFilter(flat, "zzz"));
    }

    [Fact]
    public void ApplyFilter_ProjectsTheSameRowInstances()
    {
        // Selection (the multi-select groundwork) lives on the row, so filtering
        // must project instances rather than rebuild them.
        ArtifactRowViewModel row = Row("alpha", "User");
        row.IsSelected = true;
        List<object> flat = [row];

        List<object> result = AgentsSkillsEditorViewModel.ApplyFilter(flat, "alpha");

        MessageAssert.Same(row, result[0], "ApplyFilter must not construct new rows.");
        Assert.True(((ArtifactRowViewModel)result[0]).IsSelected, "Row state must survive filtering.");
    }

    [Fact]
    public void ApplyFilter_ReturnsMaterializedList_NotALazyQuery()
    {
        // The view re-enumerates the bound collection on layout passes; a lazy
        // query would re-run the whole filter each time.
        List<object> flat = [Row("alpha", "User")];

        List<object> result = AgentsSkillsEditorViewModel.ApplyFilter(flat, "alpha");

        // Mutating the source afterwards must not change an already-returned list.
        flat.Add(Row("alpha2", "User"));
        Assert.Single(result);
    }

    [Fact]
    public void ApplyFilter_UnknownItemType_FailsOpen()
    {
        // A third item kind must stay visible rather than silently vanish the
        // moment the user types in the filter box.
        List<object> flat = ["a plain string", Row("alpha", "User")];

        List<object> result = AgentsSkillsEditorViewModel.ApplyFilter(flat, "zzz");

        Assert.Single(result);
        Assert.Equal("a plain string", result[0]);
    }

    // ── VM-level behaviour ───────────────────────────────────────────────

    [Fact]
    public async Task FilterText_NarrowsAllThreeSegments()
    {
        Write(Path.Combine(Home, "agents", "pdf-agent.md"), "---\nname: pdf-agent\n---\n\nB.\n");
        Write(Path.Combine(Home, "agents", "other.md"), "---\nname: other\n---\n\nB.\n");
        Write(Path.Combine(Home, "skills", "pdf-skill", "SKILL.md"), "---\nname: pdf-skill\n---\n\nB.\n");
        Write(Path.Combine(Home, "commands", "pdf-cmd.md"), "---\ndescription: d\n---\n\nB.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();

        vm.FilterText = "pdf";

        Assert.Single(vm.FilteredAgentItems.OfType<ArtifactRowViewModel>());
        Assert.Single(vm.FilteredSkillItems.OfType<ArtifactRowViewModel>());
        Assert.Single(vm.FilteredCommandItems.OfType<ArtifactRowViewModel>());
        Assert.True(vm.HasActiveFilter);
    }

    [Fact]
    public async Task ClearFilterCommand_RestoresFullLists()
    {
        Write(Path.Combine(Home, "agents", "alpha.md"), "---\nname: alpha\n---\n\nB.\n");
        Write(Path.Combine(Home, "agents", "beta.md"), "---\nname: beta\n---\n\nB.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        vm.FilterText = "alpha";
        Assert.Single(vm.FilteredAgentItems.OfType<ArtifactRowViewModel>());

        vm.ClearFilterCommand.Execute(null);

        Assert.Equal(2, vm.FilteredAgentItems.OfType<ArtifactRowViewModel>().Count());
        Assert.False(vm.HasActiveFilter);
    }

    [Fact]
    public async Task RefreshAsync_RaisesFilteredListNotifications()
    {
        // Regression guard: the view binds the computed projections, so a rebuild
        // that doesn't announce them leaves the UI rendering stale lists.
        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        Write(Path.Combine(Home, "agents", "alpha.md"), "---\nname: alpha\n---\n\nB.\n");

        List<string> raised = [];
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
            {
                raised.Add(e.PropertyName);
            }
        };

        await vm.RefreshAsync();

        Assert.True(raised.Contains(nameof(AgentsSkillsEditorViewModel.FilteredAgentItems)),
            "RefreshAsync must raise FilteredAgentItems.");
        Assert.True(raised.Contains(nameof(AgentsSkillsEditorViewModel.FilteredSkillItems)),
            "RefreshAsync must raise FilteredSkillItems.");
        Assert.True(raised.Contains(nameof(AgentsSkillsEditorViewModel.FilteredCommandItems)),
            "RefreshAsync must raise FilteredCommandItems.");
    }

    [Fact]
    public async Task DescriptionFill_ReRaisesFilteredLists_SoDescriptionMatchesAppear()
    {
        // Subtitles arrive asynchronously, so a filter on a description can only
        // match once the fill completes — which must re-announce the lists.
        Write(Path.Combine(Home, "skills", "alpha", "SKILL.md"),
            "---\nname: alpha\ndescription: Converts PDF documents\n---\n\nB.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);

        // Subscribe BEFORE RefreshAsync. The refresh starts the description fill
        // internally (LastDescriptionFill is assigned inside it), so subscribing
        // afterwards races the fill: if it finished first, its notification was
        // raised with no listener attached and the assert below saw nothing.
        List<string> raised = [];
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
            {
                raised.Add(e.PropertyName);
            }
        };

        await vm.RefreshAsync();

        if (vm.LastDescriptionFill is { } fill)
        {
            await fill;
        }

        // A successful refresh announces the lists exactly once; the fill announces
        // them again when it completes. Counting (rather than checking presence)
        // keeps the assertion race-free while still proving the SECOND, fill-driven
        // announcement happened — presence alone would pass on the refresh's raise.
        int skillRaises = raised.Count(
            n => n == nameof(AgentsSkillsEditorViewModel.FilteredSkillItems));
        Assert.True(skillRaises >= 2,
            "The description fill must re-raise the filtered lists when it completes; "
            + $"expected >= 2 FilteredSkillItems raises (refresh + fill), saw {skillRaises}.");

        // And the description is now actually matchable.
        vm.FilterText = "pdf";
        Assert.Single(vm.FilteredSkillItems.OfType<ArtifactRowViewModel>());
    }

    [Fact]
    public async Task ApplyNavigationFilter_FlagsNavigationThenUserEditClearsIt()
    {
        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();

        vm.ApplyNavigationFilter("alpha");
        Assert.Equal("alpha", vm.FilterText);
        Assert.True(vm.FilterFromNavigation, "A navigation-applied filter must raise the navigated frame.");

        // A subsequent user edit is not navigation, so the frame drops.
        vm.FilterText = "alphab";
        Assert.False(vm.FilterFromNavigation);
    }

    [Fact]
    public async Task ApplyNavigationFilter_WithEmpty_DoesNotFlagNavigation()
    {
        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();

        vm.ApplyNavigationFilter(null);

        Assert.Equal(string.Empty, vm.FilterText);
        Assert.False(vm.FilterFromNavigation, "An empty navigation filter narrows nothing, so no frame.");
    }

    [Fact]
    public async Task ClearFilter_AfterNavigationFilter_DropsTheFrame()
    {
        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        vm.ApplyNavigationFilter("alpha");

        vm.ClearFilterCommand.Execute(null);

        Assert.False(vm.FilterFromNavigation);
        Assert.False(vm.HasActiveFilter);
    }

    [Fact]
    public async Task RowCounts_TrackTheActiveSegmentAndFilter()
    {
        Write(Path.Combine(Home, "agents", "alpha.md"), "---\nname: alpha\n---\n\nB.\n");
        Write(Path.Combine(Home, "agents", "beta.md"), "---\nname: beta\n---\n\nB.\n");
        Write(Path.Combine(Home, "skills", "gamma", "SKILL.md"), "---\nname: gamma\n---\n\nB.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();

        // Segment 0 = sub-agents.
        Assert.Equal(2, vm.TotalRowCount);
        Assert.Equal(2, vm.VisibleRowCount);

        vm.FilterText = "alpha";
        MessageAssert.Equal(2, vm.TotalRowCount, "Total is the unfiltered count.");
        Assert.Equal(1, vm.VisibleRowCount);

        // FilterSummary is formatted in the VM because the format takes two
        // arguments — a single-binding AXAML StringFormat would leave a literal
        // "{1}" on screen.  Assert both numbers actually made it in.
        OrdinalAssert.Contains("1", vm.FilterSummary);
        OrdinalAssert.Contains("2", vm.FilterSummary);
        Assert.False(vm.FilterSummary.Contains('{'), "The format must be fully substituted.");

        // Switching segment re-reads both counts.
        vm.FilterText = string.Empty;
        vm.SelectedSegmentIndex = 1;
        Assert.Equal(1, vm.TotalRowCount);
        Assert.Equal(1, vm.VisibleRowCount);
    }

    [Fact]
    public async Task AllRows_CoversEverySegment()
    {
        Write(Path.Combine(Home, "agents", "alpha.md"), "---\nname: alpha\n---\n\nB.\n");
        Write(Path.Combine(Home, "skills", "beta", "SKILL.md"), "---\nname: beta\n---\n\nB.\n");
        Write(Path.Combine(Home, "commands", "gamma.md"), "---\ndescription: d\n---\n\nB.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();

        MessageAssert.SameElements(
            new[] { "alpha", "beta", "gamma" },
            vm.AllRows.Select(r => r.DisplayName).ToArray());
    }
}
