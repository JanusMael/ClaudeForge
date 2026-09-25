using System.IO;
using System.Linq;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// <see cref="IDeepNavigable"/> contract as implemented by the Agents &amp; Skills
/// page: capture the in-page position, put it back, and treat an unsaved edit
/// buffer as in-memory-only.
///
/// <para>
/// The mode distinction is the behavioural heart of the feature.
/// <see cref="DeepRestoreMode.Full"/> is for an in-process Reload Window and
/// restores the editing experience with the user's ACTUAL typed text;
/// <see cref="DeepRestoreMode.Locate"/> is for a cold launch or an explicit
/// <c>--deep-link</c> and deliberately stops at selecting the item, because
/// re-entering an editor seeded from disk would look like unsaved work had
/// returned when it had not.
/// </para>
/// </summary>
public sealed class AgentsSkillsDeepPathTests : IDisposable
{
    private string _sandbox = string.Empty;
    private string _project = string.Empty;

    public AgentsSkillsDeepPathTests() => Setup();

    private void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_asdeep_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        _project = Path.Combine(Path.GetTempPath(), "claudetest_asdeep_proj_" + Guid.NewGuid().ToString("N"));
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

    private async Task<AgentsSkillsEditorViewModel> LoadedVmAsync()
    {
        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        vm.Refresh();
        if (vm.LastRefresh is { } r)
        {
            await r;
        }

        if (vm.LastDescriptionFill is { } f)
        {
            await f;
        }

        return vm;
    }

    // ── Segment ids ──────────────────────────────────────────────────────

    [Fact]
    public void SegmentIds_RoundTripWithIndices()
    {
        Assert.Equal(0, AgentsSkillsEditorViewModel.SegmentIndexFor("subagents"));
        Assert.Equal(1, AgentsSkillsEditorViewModel.SegmentIndexFor("skills"));
        Assert.Equal(2, AgentsSkillsEditorViewModel.SegmentIndexFor("commands"));

        Assert.Equal("subagents", AgentsSkillsEditorViewModel.SegmentIdFor(0));
        Assert.Equal("skills", AgentsSkillsEditorViewModel.SegmentIdFor(1));
        Assert.Equal("commands", AgentsSkillsEditorViewModel.SegmentIdFor(2));
    }

    [Fact]
    public void SegmentIndexFor_IsCaseInsensitive_AndNullForUnknown()
    {
        Assert.Equal(1, AgentsSkillsEditorViewModel.SegmentIndexFor("SKILLS"));
        Assert.Null(AgentsSkillsEditorViewModel.SegmentIndexFor("nope"));
        Assert.Null(AgentsSkillsEditorViewModel.SegmentIndexFor(null));
    }

    [Fact]
    public async Task SelectSegment_UnknownId_IsANoOp()
    {
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();
        vm.SelectedSegmentIndex = 2;

        vm.SelectSegment("not-a-segment");

        MessageAssert.Equal(2, vm.SelectedSegmentIndex, "An unknown segment id must not move the user.");
    }

    // ── Capture ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CaptureDeepPath_NoSelection_IsSegmentOnly()
    {
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();
        vm.SelectedSegmentIndex = 1;

        Assert.Equal(new[] { "skills" }, vm.CaptureDeepPath().ToArray());
    }

    [Fact]
    public async Task CaptureDeepPath_WithSelection_IsQualifiedBySource()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();
        vm.SelectedSegmentIndex = 1;
        await vm.LoadArtifactAsync(vm.SkillItems.OfType<ArtifactRowViewModel>().Single());

        IReadOnlyList<string> path = vm.CaptureDeepPath();

        Assert.Equal(2, path.Count);
        Assert.Equal("skills", path[0]);
        // name@source, not a bare name — otherwise a restore could land on a
        // same-named artifact from a different scope or plugin.
        OrdinalAssert.StartsWith("pdf@", path[1]);
    }

    [Fact]
    public async Task CaptureDeepPath_SegmentComesFromTheArtifact_NotTheVisibleTab()
    {
        // The segment is derived from the selected artifact's CATEGORY, not from
        // SelectedSegmentIndex, so the captured pair is self-consistent by
        // construction: a path can never name a segment that doesn't contain the
        // item it points at. (They normally agree; this pins the guarantee.)
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        await vm.LoadArtifactAsync(vm.SkillItems.OfType<ArtifactRowViewModel>().Single());
        vm.SelectedSegmentIndex = 0; // deliberately disagreeing with the open skill

        IReadOnlyList<string> path = vm.CaptureDeepPath();

        MessageAssert.Equal("skills", path[0], "The segment must follow the artifact, not the visible tab.");
    }

    [Fact]
    public async Task CaptureDeepPath_NeverContainsAPathSeparator()
    {
        // The segment separator is '/', so an absolute path in an item key would
        // make the persisted deep path unparseable.
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();
        vm.SelectedSegmentIndex = 1;
        await vm.LoadArtifactAsync(vm.SkillItems.OfType<ArtifactRowViewModel>().Single());

        foreach (string segment in vm.CaptureDeepPath())
        {
            Assert.False(segment.Contains('/'), $"Segment '{segment}' must not contain '/'.");
            Assert.False(segment.Contains('\\'), $"Segment '{segment}' must not contain '\\'.");
        }
    }

    [Fact]
    public async Task CaptureTransientState_NullWhenNotEditing()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();
        await vm.LoadArtifactAsync(vm.SkillItems.OfType<ArtifactRowViewModel>().Single());

        MessageAssert.Null(vm.CaptureTransientState(), "A plain viewing position needs no transient payload.");
    }

    [Fact]
    public async Task CaptureTransientState_CarriesTheUnsavedBuffer()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"),
            "---\nname: pdf\ndescription: original\n---\n\nBody.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();
        await vm.LoadArtifactAsync(vm.SkillItems.OfType<ArtifactRowViewModel>().Single());
        vm.BeginEditCommand.Execute(null);
        vm.EditDescription = "typed but not saved";

        object? state = vm.CaptureTransientState();

        MessageAssert.NotNull(state, "An in-progress edit must be captured or a reload discards it.");
    }

    // ── Copy deep link (discoverability) ─────────────────────────────────

    [Fact]
    public async Task CopyDeepLink_IsDisabledWithoutASelectionOrAHostNodeId()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        // No host node id (the unit-test case) and nothing selected.
        Assert.False(vm.CanCopyDeepLink);
        Assert.False(vm.CopyDeepLinkCommand.CanExecute(null));

        // A selection alone isn't enough — the full path needs the node prefix,
        // which only the host knows.
        await vm.LoadArtifactAsync(vm.SkillItems.OfType<ArtifactRowViewModel>().Single());
        Assert.False(vm.CanCopyDeepLink, "Without a host node id no full path can be composed.");

        vm.DeepLinkNodeId = "agents-skills";
        Assert.True(vm.CanCopyDeepLink);
        Assert.True(vm.CopyDeepLinkCommand.CanExecute(null));
    }

    [Fact]
    public async Task CopyDeepLink_EmitsAPathThatResolvesBackToTheSameArtifact()
    {
        // The whole point of the button: whatever it copies must be a path the
        // app will actually accept. Round-trip it through the real grammar.
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();
        vm.DeepLinkNodeId = "agents-skills";
        await vm.LoadArtifactAsync(vm.SkillItems.OfType<ArtifactRowViewModel>().Single());

        string? copied = null;
        vm.CopyMarkdownRequested += (_, text) => copied = text;

        vm.CopyDeepLinkCommand.Execute(null);

        MessageAssert.NotNull(copied, "The command must hand a payload to the view's clipboard bridge.");
        Assert.True(NavDeepPath.TryParse(copied, out IReadOnlyList<string> segs, out string? err),
            $"The copied path must be well-formed; parser said: {err}");
        Assert.Equal(new[] { "agents-skills", "skills" }, segs.Take(2).ToArray());
        OrdinalAssert.StartsWith("pdf@", segs[2]);

        // And the user gets told it happened.
        Assert.NotNull(vm.LastActionMessage);
        OrdinalAssert.Contains(copied!, vm.LastActionMessage!);
    }

    [Fact]
    public async Task CopyDeepLink_PathRestoresOnAFreshViewModel()
    {
        // End-to-end: copy on one instance, restore on another, land on the same row.
        Write(Path.Combine(Home, "agents", "reviewer.md"), "---\nname: reviewer\n---\n\nB.\n");
        AgentsSkillsEditorViewModel source = await LoadedVmAsync();
        source.DeepLinkNodeId = "agents-skills";
        await source.LoadArtifactAsync(source.AgentItems.OfType<ArtifactRowViewModel>().Single());

        string? copied = null;
        source.CopyMarkdownRequested += (_, text) => copied = text;
        source.CopyDeepLinkCommand.Execute(null);

        Assert.True(NavDeepPath.TryParse(copied, out IReadOnlyList<string> segs, out _));

        AgentsSkillsEditorViewModel target = await LoadedVmAsync();
        // Drop the node segment — that's the host's part; the page restores below it.
        Assert.True(await target.TryRestoreDeepPathAsync(
            segs.Skip(1).ToList(), DeepRestoreMode.Locate, null, CancellationToken.None));
        Assert.Equal("reviewer", target.SelectedArtifact?.DisplayName);
    }

    // ── Restore ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Restore_SelectsSegmentAndItem_AndRevealsItByFiltering()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        Write(Path.Combine(Home, "skills", "other", "SKILL.md"), "---\nname: other\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        bool ok = await vm.TryRestoreDeepPathAsync(
            ["skills", "pdf"], DeepRestoreMode.Locate, null, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(1, vm.SelectedSegmentIndex);
        Assert.Equal("pdf", vm.SelectedArtifact?.DisplayName);

        // Revealed by filtering — the mechanism the app already uses for property
        // jump links — with the navigated frame flagged.
        Assert.Equal("pdf", vm.FilterText);
        Assert.True(vm.FilterFromNavigation,
            "The reveal must go through ApplyNavigationFilter so the navigated frame shows.");
        Assert.Single(vm.FilteredSkillItems.OfType<ArtifactRowViewModel>());
    }

    [Fact]
    public async Task Restore_SegmentOnly_SelectsTabWithoutOpeningAnything()
    {
        Write(Path.Combine(Home, "commands", "c1.md"), "---\ndescription: d\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        bool ok = await vm.TryRestoreDeepPathAsync(
            ["commands"], DeepRestoreMode.Locate, null, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(2, vm.SelectedSegmentIndex);
        Assert.Null(vm.SelectedArtifact);
    }

    [Fact]
    public async Task Restore_QualifiedKey_PicksTheRightScope()
    {
        // Same NAME under two sources: only the qualified key disambiguates.
        Write(Path.Combine(Home, "skills", "dup", "SKILL.md"), "---\nname: dup\n---\n\nUser.\n");
        Write(Path.Combine(Home, "plugins", "mkt", "plug", "skills", "dup", "SKILL.md"),
            "---\nname: dup\n---\n\nPlugin.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        ArtifactRowViewModel pluginRow = vm.SkillItems.OfType<ArtifactRowViewModel>()
                                           .Single(r => r.IsPlugin);

        bool ok = await vm.TryRestoreDeepPathAsync(
            ["skills", NavDeepPath.FormatItemKey(pluginRow.DisplayName, pluginRow.Source)],
            DeepRestoreMode.Locate, null, CancellationToken.None);

        Assert.True(ok);
        Assert.Same(pluginRow, vm.SelectedArtifact);
    }

    [Fact]
    public async Task Restore_MissingItem_ReturnsFalseButStillSelectsTheTab()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        bool ok = await vm.TryRestoreDeepPathAsync(
            ["skills", "deleted-skill"], DeepRestoreMode.Locate, null, CancellationToken.None);

        Assert.False(ok, "A deleted artifact must report not-restored…");
        MessageAssert.Equal(1, vm.SelectedSegmentIndex, "…but landing on the right tab is still better than not.");
        Assert.Null(vm.SelectedArtifact);
    }

    [Fact]
    public async Task Restore_UnqualifiedKeyWhoseSourceIsGone_StillResolvesByName()
    {
        // A plugin can be uninstalled between capture and restore. The name
        // surviving elsewhere is a better outcome than refusing to navigate.
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        bool ok = await vm.TryRestoreDeepPathAsync(
            ["skills", "pdf@some-uninstalled-plugin"], DeepRestoreMode.Locate, null, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal("pdf", vm.SelectedArtifact?.DisplayName);
    }

    [Fact]
    public async Task Restore_Locate_DoesNotEnterEditMode_EvenWithATransientPayload()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"),
            "---\nname: pdf\ndescription: original\n---\n\nBody.\n");
        AgentsSkillsEditorViewModel source = await LoadedVmAsync();
        await source.LoadArtifactAsync(source.SkillItems.OfType<ArtifactRowViewModel>().Single());
        source.BeginEditCommand.Execute(null);
        source.EditDescription = "typed but not saved";
        object? transient = source.CaptureTransientState();

        AgentsSkillsEditorViewModel target = await LoadedVmAsync();
        await target.TryRestoreDeepPathAsync(
            ["skills", "pdf"], DeepRestoreMode.Locate, transient, CancellationToken.None);

        Assert.False(target.IsEditing,
            "Locate must not re-enter editing — a cold launch has no live buffer to justify it.");
    }

    [Fact]
    public async Task Restore_Full_ReturnsTheUsersActualUnsavedText()
    {
        // The core promise: Reload Window no longer eats an in-progress edit.
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"),
            "---\nname: pdf\ndescription: original\n---\n\nOriginal body.\n");

        AgentsSkillsEditorViewModel before = await LoadedVmAsync();
        await before.LoadArtifactAsync(before.SkillItems.OfType<ArtifactRowViewModel>().Single());
        before.BeginEditCommand.Execute(null);
        before.EditDescription = "typed but not saved";
        before.EditBody = "half-written body";

        IReadOnlyList<string> path = before.CaptureDeepPath();
        object? transient = before.CaptureTransientState();

        // A fresh VM stands in for the post-reload rebuild.
        AgentsSkillsEditorViewModel after = await LoadedVmAsync();
        bool ok = await after.TryRestoreDeepPathAsync(
            path, DeepRestoreMode.Full, transient, CancellationToken.None);

        Assert.True(ok);
        Assert.True(after.IsEditing, "Full restore must re-open the editing experience.");
        MessageAssert.Equal("typed but not saved", after.EditDescription,
            "The user's unsaved text must come back — not the value re-read from disk.");
        Assert.Equal("half-written body", after.EditBody);

        // And the file on disk is untouched, because nothing was saved.
        string onDisk = await File.ReadAllTextAsync(
            Path.Combine(Home, "skills", "pdf", "SKILL.md"), TestContext.Current.CancellationToken);
        OrdinalAssert.Contains("description: original", onDisk);
    }

    [Fact]
    public async Task Restore_Full_RestoresRawModeAndItsText()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"),
            "---\nname: pdf\ndescription: original\n---\n\nBody.\n");

        AgentsSkillsEditorViewModel before = await LoadedVmAsync();
        await before.LoadArtifactAsync(before.SkillItems.OfType<ArtifactRowViewModel>().Single());
        before.BeginEditCommand.Execute(null);
        before.ToggleRawModeCommand.Execute(null);
        before.EditRawFrontMatter = "name: pdf\ndescription: raw edit in flight";

        object? transient = before.CaptureTransientState();
        IReadOnlyList<string> path = before.CaptureDeepPath();

        AgentsSkillsEditorViewModel after = await LoadedVmAsync();
        await after.TryRestoreDeepPathAsync(path, DeepRestoreMode.Full, transient, CancellationToken.None);

        Assert.True(after.IsEditing);
        Assert.True(after.IsRawMode, "Raw mode is part of the editing experience.");
        // Toggling IsRawMode re-seeds the raw box from the typed fields, so the
        // captured raw text has to be applied AFTER the toggle or it is lost.
        Assert.Equal("name: pdf\ndescription: raw edit in flight", after.EditRawFrontMatter);
    }

    [Fact]
    public async Task Restore_EmptySegments_ReturnsFalse()
    {
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        Assert.False(await vm.TryRestoreDeepPathAsync(
            [], DeepRestoreMode.Locate, null, CancellationToken.None));
    }

    [Fact]
    public async Task Restore_UnrecognisedTransientPayload_IsIgnoredNotThrown()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        bool ok = await vm.TryRestoreDeepPathAsync(
            ["skills", "pdf"], DeepRestoreMode.Full, "not a snapshot", CancellationToken.None);

        Assert.True(ok);
        Assert.False(vm.IsEditing);
    }

    [Fact]
    public async Task CaptureThenRestore_RoundTripsThroughTheStringForm()
    {
        // End-to-end: what gets persisted is a plain string, so the whole loop has
        // to survive Format → TryParse → Resolve.
        Write(Path.Combine(Home, "agents", "reviewer.md"), "---\nname: reviewer\n---\n\nB.\n");
        AgentsSkillsEditorViewModel before = await LoadedVmAsync();
        before.SelectedSegmentIndex = 0;
        await before.LoadArtifactAsync(before.AgentItems.OfType<ArtifactRowViewModel>().Single());

        string persisted = NavDeepPath.Format(before.CaptureDeepPath());
        Assert.True(NavDeepPath.TryParse(persisted, out IReadOnlyList<string> parsed, out _));

        AgentsSkillsEditorViewModel after = await LoadedVmAsync();
        Assert.True(await after.TryRestoreDeepPathAsync(
            parsed, DeepRestoreMode.Locate, null, CancellationToken.None));
        Assert.Equal("reviewer", after.SelectedArtifact?.DisplayName);
    }
}
