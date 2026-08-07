using System.IO;
using System.Linq;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
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
[TestClass]
public sealed class AgentsSkillsDeepPathTests
{
    private string _sandbox = string.Empty;
    private string _project = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_asdeep_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        _project = Path.Combine(Path.GetTempPath(), "claudetest_asdeep_proj_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_project);
    }

    [TestCleanup]
    public void Cleanup()
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

    private string Home => Path.Combine(_sandbox, ".claude");

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task<AgentsSkillsEditorViewModel> LoadedVmAsync()
    {
        var vm = new AgentsSkillsEditorViewModel(_project);
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

    [TestMethod]
    public void SegmentIds_RoundTripWithIndices()
    {
        Assert.AreEqual(0, AgentsSkillsEditorViewModel.SegmentIndexFor("subagents"));
        Assert.AreEqual(1, AgentsSkillsEditorViewModel.SegmentIndexFor("skills"));
        Assert.AreEqual(2, AgentsSkillsEditorViewModel.SegmentIndexFor("commands"));

        Assert.AreEqual("subagents", AgentsSkillsEditorViewModel.SegmentIdFor(0));
        Assert.AreEqual("skills", AgentsSkillsEditorViewModel.SegmentIdFor(1));
        Assert.AreEqual("commands", AgentsSkillsEditorViewModel.SegmentIdFor(2));
    }

    [TestMethod]
    public void SegmentIndexFor_IsCaseInsensitive_AndNullForUnknown()
    {
        Assert.AreEqual(1, AgentsSkillsEditorViewModel.SegmentIndexFor("SKILLS"));
        Assert.IsNull(AgentsSkillsEditorViewModel.SegmentIndexFor("nope"));
        Assert.IsNull(AgentsSkillsEditorViewModel.SegmentIndexFor(null));
    }

    [TestMethod]
    public async Task SelectSegment_UnknownId_IsANoOp()
    {
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();
        vm.SelectedSegmentIndex = 2;

        vm.SelectSegment("not-a-segment");

        Assert.AreEqual(2, vm.SelectedSegmentIndex, "An unknown segment id must not move the user.");
    }

    // ── Capture ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CaptureDeepPath_NoSelection_IsSegmentOnly()
    {
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();
        vm.SelectedSegmentIndex = 1;

        CollectionAssert.AreEqual(new[] { "skills" }, vm.CaptureDeepPath().ToArray());
    }

    [TestMethod]
    public async Task CaptureDeepPath_WithSelection_IsQualifiedBySource()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();
        vm.SelectedSegmentIndex = 1;
        await vm.LoadArtifactAsync(vm.SkillItems.OfType<ArtifactRowViewModel>().Single());

        IReadOnlyList<string> path = vm.CaptureDeepPath();

        Assert.AreEqual(2, path.Count);
        Assert.AreEqual("skills", path[0]);
        // name@source, not a bare name — otherwise a restore could land on a
        // same-named artifact from a different scope or plugin.
        StringAssert.StartsWith(path[1], "pdf@");
    }

    [TestMethod]
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

        Assert.AreEqual("skills", path[0], "The segment must follow the artifact, not the visible tab.");
    }

    [TestMethod]
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
            Assert.IsFalse(segment.Contains('/'), $"Segment '{segment}' must not contain '/'.");
            Assert.IsFalse(segment.Contains('\\'), $"Segment '{segment}' must not contain '\\'.");
        }
    }

    [TestMethod]
    public async Task CaptureTransientState_NullWhenNotEditing()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();
        await vm.LoadArtifactAsync(vm.SkillItems.OfType<ArtifactRowViewModel>().Single());

        Assert.IsNull(vm.CaptureTransientState(), "A plain viewing position needs no transient payload.");
    }

    [TestMethod]
    public async Task CaptureTransientState_CarriesTheUnsavedBuffer()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"),
            "---\nname: pdf\ndescription: original\n---\n\nBody.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();
        await vm.LoadArtifactAsync(vm.SkillItems.OfType<ArtifactRowViewModel>().Single());
        vm.BeginEditCommand.Execute(null);
        vm.EditDescription = "typed but not saved";

        object? state = vm.CaptureTransientState();

        Assert.IsNotNull(state, "An in-progress edit must be captured or a reload discards it.");
    }

    // ── Copy deep link (discoverability) ─────────────────────────────────

    [TestMethod]
    public async Task CopyDeepLink_IsDisabledWithoutASelectionOrAHostNodeId()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        // No host node id (the unit-test case) and nothing selected.
        Assert.IsFalse(vm.CanCopyDeepLink);
        Assert.IsFalse(vm.CopyDeepLinkCommand.CanExecute(null));

        // A selection alone isn't enough — the full path needs the node prefix,
        // which only the host knows.
        await vm.LoadArtifactAsync(vm.SkillItems.OfType<ArtifactRowViewModel>().Single());
        Assert.IsFalse(vm.CanCopyDeepLink, "Without a host node id no full path can be composed.");

        vm.DeepLinkNodeId = "agents-skills";
        Assert.IsTrue(vm.CanCopyDeepLink);
        Assert.IsTrue(vm.CopyDeepLinkCommand.CanExecute(null));
    }

    [TestMethod]
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

        Assert.IsNotNull(copied, "The command must hand a payload to the view's clipboard bridge.");
        Assert.IsTrue(NavDeepPath.TryParse(copied, out IReadOnlyList<string> segs, out string? err),
            $"The copied path must be well-formed; parser said: {err}");
        CollectionAssert.AreEqual(new[] { "agents-skills", "skills" }, segs.Take(2).ToArray());
        StringAssert.StartsWith(segs[2], "pdf@");

        // And the user gets told it happened.
        Assert.IsNotNull(vm.LastActionMessage);
        StringAssert.Contains(vm.LastActionMessage!, copied!);
    }

    [TestMethod]
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

        Assert.IsTrue(NavDeepPath.TryParse(copied, out IReadOnlyList<string> segs, out _));

        AgentsSkillsEditorViewModel target = await LoadedVmAsync();
        // Drop the node segment — that's the host's part; the page restores below it.
        Assert.IsTrue(await target.TryRestoreDeepPathAsync(
            segs.Skip(1).ToList(), DeepRestoreMode.Locate, null, CancellationToken.None));
        Assert.AreEqual("reviewer", target.SelectedArtifact?.DisplayName);
    }

    // ── Restore ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Restore_SelectsSegmentAndItem_AndRevealsItByFiltering()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        Write(Path.Combine(Home, "skills", "other", "SKILL.md"), "---\nname: other\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        bool ok = await vm.TryRestoreDeepPathAsync(
            ["skills", "pdf"], DeepRestoreMode.Locate, null, CancellationToken.None);

        Assert.IsTrue(ok);
        Assert.AreEqual(1, vm.SelectedSegmentIndex);
        Assert.AreEqual("pdf", vm.SelectedArtifact?.DisplayName);

        // Revealed by filtering — the mechanism the app already uses for property
        // jump links — with the navigated frame flagged.
        Assert.AreEqual("pdf", vm.FilterText);
        Assert.IsTrue(vm.FilterFromNavigation,
            "The reveal must go through ApplyNavigationFilter so the navigated frame shows.");
        Assert.AreEqual(1, vm.FilteredSkillItems.OfType<ArtifactRowViewModel>().Count());
    }

    [TestMethod]
    public async Task Restore_SegmentOnly_SelectsTabWithoutOpeningAnything()
    {
        Write(Path.Combine(Home, "commands", "c1.md"), "---\ndescription: d\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        bool ok = await vm.TryRestoreDeepPathAsync(
            ["commands"], DeepRestoreMode.Locate, null, CancellationToken.None);

        Assert.IsTrue(ok);
        Assert.AreEqual(2, vm.SelectedSegmentIndex);
        Assert.IsNull(vm.SelectedArtifact);
    }

    [TestMethod]
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

        Assert.IsTrue(ok);
        Assert.AreSame(pluginRow, vm.SelectedArtifact);
    }

    [TestMethod]
    public async Task Restore_MissingItem_ReturnsFalseButStillSelectsTheTab()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        bool ok = await vm.TryRestoreDeepPathAsync(
            ["skills", "deleted-skill"], DeepRestoreMode.Locate, null, CancellationToken.None);

        Assert.IsFalse(ok, "A deleted artifact must report not-restored…");
        Assert.AreEqual(1, vm.SelectedSegmentIndex, "…but landing on the right tab is still better than not.");
        Assert.IsNull(vm.SelectedArtifact);
    }

    [TestMethod]
    public async Task Restore_UnqualifiedKeyWhoseSourceIsGone_StillResolvesByName()
    {
        // A plugin can be uninstalled between capture and restore. The name
        // surviving elsewhere is a better outcome than refusing to navigate.
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        bool ok = await vm.TryRestoreDeepPathAsync(
            ["skills", "pdf@some-uninstalled-plugin"], DeepRestoreMode.Locate, null, CancellationToken.None);

        Assert.IsTrue(ok);
        Assert.AreEqual("pdf", vm.SelectedArtifact?.DisplayName);
    }

    [TestMethod]
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

        Assert.IsFalse(target.IsEditing,
            "Locate must not re-enter editing — a cold launch has no live buffer to justify it.");
    }

    [TestMethod]
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

        Assert.IsTrue(ok);
        Assert.IsTrue(after.IsEditing, "Full restore must re-open the editing experience.");
        Assert.AreEqual("typed but not saved", after.EditDescription,
            "The user's unsaved text must come back — not the value re-read from disk.");
        Assert.AreEqual("half-written body", after.EditBody);

        // And the file on disk is untouched, because nothing was saved.
        string onDisk = await File.ReadAllTextAsync(
            Path.Combine(Home, "skills", "pdf", "SKILL.md"));
        StringAssert.Contains(onDisk, "description: original");
    }

    [TestMethod]
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

        Assert.IsTrue(after.IsEditing);
        Assert.IsTrue(after.IsRawMode, "Raw mode is part of the editing experience.");
        // Toggling IsRawMode re-seeds the raw box from the typed fields, so the
        // captured raw text has to be applied AFTER the toggle or it is lost.
        Assert.AreEqual("name: pdf\ndescription: raw edit in flight", after.EditRawFrontMatter);
    }

    [TestMethod]
    public async Task Restore_EmptySegments_ReturnsFalse()
    {
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        Assert.IsFalse(await vm.TryRestoreDeepPathAsync(
            [], DeepRestoreMode.Locate, null, CancellationToken.None));
    }

    [TestMethod]
    public async Task Restore_UnrecognisedTransientPayload_IsIgnoredNotThrown()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nB.\n");
        AgentsSkillsEditorViewModel vm = await LoadedVmAsync();

        bool ok = await vm.TryRestoreDeepPathAsync(
            ["skills", "pdf"], DeepRestoreMode.Full, "not a snapshot", CancellationToken.None);

        Assert.IsTrue(ok);
        Assert.IsFalse(vm.IsEditing);
    }

    [TestMethod]
    public async Task CaptureThenRestore_RoundTripsThroughTheStringForm()
    {
        // End-to-end: what gets persisted is a plain string, so the whole loop has
        // to survive Format → TryParse → Resolve.
        Write(Path.Combine(Home, "agents", "reviewer.md"), "---\nname: reviewer\n---\n\nB.\n");
        AgentsSkillsEditorViewModel before = await LoadedVmAsync();
        before.SelectedSegmentIndex = 0;
        await before.LoadArtifactAsync(before.AgentItems.OfType<ArtifactRowViewModel>().Single());

        string persisted = NavDeepPath.Format(before.CaptureDeepPath());
        Assert.IsTrue(NavDeepPath.TryParse(persisted, out IReadOnlyList<string> parsed, out _));

        AgentsSkillsEditorViewModel after = await LoadedVmAsync();
        Assert.IsTrue(await after.TryRestoreDeepPathAsync(
            parsed, DeepRestoreMode.Locate, null, CancellationToken.None));
        Assert.AreEqual("reviewer", after.SelectedArtifact?.DisplayName);
    }
}
