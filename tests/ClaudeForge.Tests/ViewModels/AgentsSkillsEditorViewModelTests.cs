using System.IO;
using System.Linq;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Locks the "Agents &amp; Skills" page VM (group #2): scope-aware
/// population of the three segment lists, lazy load → typed card + body
/// projection, plugin rows surfaced read-only, and the viewer open/close
/// toggle.
/// </summary>
[TestClass]
public sealed class AgentsSkillsEditorViewModelTests
{
    private string _sandbox = string.Empty;
    private string _project = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_asvm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        _project = Path.Combine(Path.GetTempPath(), "claudetest_asvm_proj_" + Guid.NewGuid().ToString("N"));
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

    // The segment collections are flat [headers + rows]; these helpers extract
    // just the artifact rows so the assertions read like the pre-grouping shape.
    private static IReadOnlyList<ArtifactRowViewModel> AgentRows(AgentsSkillsEditorViewModel vm) =>
        vm.AgentItems.OfType<ArtifactRowViewModel>().ToList();

    private static IReadOnlyList<ArtifactRowViewModel> SkillRows(AgentsSkillsEditorViewModel vm) =>
        vm.SkillItems.OfType<ArtifactRowViewModel>().ToList();

    private static IReadOnlyList<ArtifactRowViewModel> CommandRows(AgentsSkillsEditorViewModel vm) =>
        vm.CommandItems.OfType<ArtifactRowViewModel>().ToList();

    [TestMethod]
    public async Task Refresh_PopulatesThreeSegmentLists_AcrossScopes()
    {
        Write(Path.Combine(Home, "agents", "reviewer.md"),
            "---\nname: reviewer\ndescription: Reviews code\ntools: Read, Grep\nmodel: sonnet\n---\n\nAgent body.\n");
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"),
            "---\nname: pdf\ndescription: PDF tools\n---\n\nSkill body.\n");
        Write(Path.Combine(Home, "commands", "summarise.md"),
            "---\ndescription: Summarise PR\n---\n\nCommand body.\n");
        Write(Path.Combine(_project, ".claude", "agents", "proj-agent.md"),
            "---\nname: proj-agent\n---\n\nProject agent.\n");
        Write(Path.Combine(Home, "plugins", "mkt", "plug", "skills", "widget", "SKILL.md"),
            "---\nname: widget\ndescription: Plugin skill\n---\n\nPlugin body.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();

        // Agents: user reviewer + project proj-agent.
        CollectionAssert.AreEquivalent(
            new[] { "reviewer", "proj-agent" },
            AgentRows(vm).Select(a => a.DisplayName).ToArray());
        // Skills: user pdf + plugin widget.
        CollectionAssert.AreEquivalent(
            new[] { "pdf", "widget" },
            SkillRows(vm).Select(s => s.DisplayName).ToArray());
        // Commands: user summarise.
        CollectionAssert.AreEqual(
            new[] { "summarise" },
            CommandRows(vm).Select(c => c.DisplayName).ToArray());
    }

    [TestMethod]
    public async Task Refresh_GroupsYoursBeforePlugin_WithSectionHeaders()
    {
        Write(Path.Combine(Home, "skills", "user-skill", "SKILL.md"), "---\nname: user-skill\n---\n\nB.\n");
        Write(Path.Combine(Home, "plugins", "mkt", "skills", "plug-skill", "SKILL.md"), "---\nname: plug-skill\n---\n\nB.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();

        // Expected flat shape: [Yours header, user-skill row, Plugin header, plug-skill row].
        var items = vm.SkillItems.ToList();
        Assert.AreEqual(4, items.Count);

        var h0 = (ArtifactSectionHeaderViewModel)items[0];
        Assert.AreEqual("Yours", h0.Header);
        Assert.IsFalse(h0.IsReadOnly);
        Assert.AreEqual("user-skill", ((ArtifactRowViewModel)items[1]).DisplayName);

        var h2 = (ArtifactSectionHeaderViewModel)items[2];
        Assert.AreEqual("Plugin", h2.Header);
        Assert.IsTrue(h2.IsReadOnly, "The Plugin section header carries the read-only badge.");
        Assert.AreEqual("plug-skill", ((ArtifactRowViewModel)items[3]).DisplayName);
    }

    [TestMethod]
    public async Task Refresh_OmitsHeaderForEmptyGroup()
    {
        // Only user items → no Plugin header.
        Write(Path.Combine(Home, "agents", "only-user.md"), "---\nname: only-user\n---\n\nB.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();

        Assert.IsFalse(vm.AgentItems.OfType<ArtifactSectionHeaderViewModel>().Any(h => h.Header == "Plugin"),
            "A group with no rows must not get a header.");
        Assert.IsTrue(vm.AgentItems.OfType<ArtifactSectionHeaderViewModel>().Any(h => h.Header == "Yours"));
    }

    [TestMethod]
    public async Task Refresh_PluginRowSource_DerivedFromPluginPath()
    {
        Write(Path.Combine(Home, "plugins", "everything-claude-code", "skills", "widget", "SKILL.md"),
            "---\nname: widget\n---\n\nB.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();

        ArtifactRowViewModel row = SkillRows(vm).Single(r => r.DisplayName == "widget");
        Assert.AreEqual("everything-claude-code", row.Source,
            "Plugin row Source disambiguates by the providing plugin's name.");
    }

    [TestMethod]
    public async Task Refresh_LazyDescriptions_FillSubtitlesAfterRowsAppear()
    {
        Write(Path.Combine(Home, "agents", "a.md"), "---\nname: a\ndescription: has desc\n---\n\nB.\n");
        Write(Path.Combine(Home, "agents", "b.md"), "---\nname: b\n---\n\nB.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();

        // Rows are present immediately (stat-only) — subtitles fill in via the
        // background pass, which the test seam lets us await.
        Assert.IsNotNull(vm.LastDescriptionFill);
        await vm.LastDescriptionFill!;

        Assert.AreEqual("has desc", AgentRows(vm).Single(r => r.DisplayName == "a").Subtitle);
        Assert.AreEqual("(no description)", AgentRows(vm).Single(r => r.DisplayName == "b").Subtitle);
    }

    [TestMethod]
    public async Task Refresh_PluginSkillRow_IsReadOnly()
    {
        Write(Path.Combine(Home, "plugins", "mkt", "plug", "skills", "widget", "SKILL.md"),
            "---\nname: widget\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();

        ArtifactRowViewModel widget = SkillRows(vm).Single(s => s.DisplayName == "widget");
        Assert.IsTrue(widget.IsPlugin);
        Assert.IsFalse(widget.IsWritable, "Plugin skill row must be read-only.");
        Assert.AreEqual("Plugin", widget.ScopeLabel);
    }

    [TestMethod]
    public async Task LoadArtifact_Agent_PopulatesCardAndBody_ShowsViewer()
    {
        Write(Path.Combine(Home, "agents", "reviewer.md"),
            "---\nname: reviewer\ndescription: Reviews code\ntools: Read, Grep, Bash\nmodel: sonnet\n---\n\nYou are a reviewer.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        ArtifactRowViewModel row = AgentRows(vm).Single();

        Assert.IsFalse(vm.IsViewerVisible, "Viewer starts hidden.");
        await vm.LoadArtifactAsync(row);

        Assert.IsTrue(vm.IsViewerVisible, "Selecting a row shows the detail pane.");
        Assert.AreSame(row, vm.SelectedArtifact);
        Assert.AreEqual("reviewer", vm.CardName);
        Assert.AreEqual("Reviews code", vm.CardDescription);
        Assert.AreEqual("sonnet", vm.CardModel);
        Assert.AreEqual("Read, Grep, Bash", vm.CardTools);
        Assert.IsTrue(vm.CardShowName);
        Assert.IsTrue(vm.CardShowToolsAndModel);
        StringAssert.Contains(vm.ViewerBody!, "You are a reviewer.");
        // The card carries the front-matter; the body excludes it.
        Assert.IsFalse(vm.ViewerBody!.Contains("name: reviewer"),
            "Body viewer shows the post-front-matter prose, not the front-matter (that's the card's job).");
    }

    [TestMethod]
    public async Task LoadArtifact_SlashCommand_HidesNameAndToolsRows()
    {
        Write(Path.Combine(Home, "commands", "summarise.md"),
            "---\ndescription: Summarise the PR\n---\n\nPrompt template.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(CommandRows(vm).Single());

        Assert.AreEqual("Summarise the PR", vm.CardDescription);
        Assert.IsFalse(vm.CardShowName, "Slash commands have no name field — name row hidden.");
        Assert.IsFalse(vm.CardShowToolsAndModel, "Slash commands have no tools/model — those rows hidden.");
    }

    [TestMethod]
    public async Task CloseViewer_ResetsViewerState()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"),
            "---\nname: pdf\ndescription: PDF tools\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(SkillRows(vm).Single());
        Assert.IsTrue(vm.IsViewerVisible);

        vm.CloseViewerCommand.Execute(null);

        Assert.IsFalse(vm.IsViewerVisible);
        Assert.IsNull(vm.SelectedArtifact);
        Assert.IsNull(vm.ViewerBody);
        Assert.IsNull(vm.CardName);
    }

    [TestMethod]
    public async Task LoadArtifact_MissingFile_ShowsPlaceholderNotCrash()
    {
        string path = Path.Combine(Home, "agents", "ghost.md");
        Write(path, "---\nname: ghost\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        // Drain the background description fill so no read handle is open when
        // we delete the file below.
        if (vm.LastDescriptionFill is not null)
        {
            await vm.LastDescriptionFill;
        }
        ArtifactRowViewModel row = AgentRows(vm).Single();

        // Delete the file out from under the VM, then load.
        File.Delete(path);
        await vm.LoadArtifactAsync(row);

        Assert.IsTrue(vm.IsViewerVisible);
        StringAssert.Contains(vm.ViewerBody!, "no longer available");
    }

    // ── Group #3 — editor flow ───────────────────────────────────────────

    [TestMethod]
    public async Task BeginEdit_SeedsEditFieldsFromCard()
    {
        Write(Path.Combine(Home, "agents", "reviewer.md"),
            "---\nname: reviewer\ndescription: Reviews code\ntools: Read, Grep\nmodel: sonnet\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());

        Assert.IsTrue(vm.CanEdit);
        vm.BeginEditCommand.Execute(null);

        Assert.IsTrue(vm.IsEditing);
        Assert.AreEqual("reviewer", vm.EditName);
        Assert.AreEqual("Reviews code", vm.EditDescription);
        Assert.AreEqual("Read, Grep", vm.EditTools);
        Assert.AreEqual("sonnet", vm.EditModel);
    }

    [TestMethod]
    public async Task PluginRow_CanEditFalse_BeginEditIsNoOp()
    {
        Write(Path.Combine(Home, "plugins", "p", "skills", "w", "SKILL.md"),
            "---\nname: w\ndescription: plugin\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(SkillRows(vm).Single());

        Assert.IsFalse(vm.CanEdit, "Plugin rows are read-only.");
        Assert.IsFalse(vm.BeginEditCommand.CanExecute(null), "BeginEdit must be disabled for plugin rows.");
        vm.BeginEditCommand.Execute(null);
        Assert.IsFalse(vm.IsEditing, "BeginEdit must be a no-op on a read-only row.");
    }

    [TestMethod]
    public async Task Save_PersistsEditsToDisk_AndRefreshesCard()
    {
        string path = Path.Combine(Home, "agents", "reviewer.md");
        Write(path, "---\nname: reviewer\ndescription: old desc\ntools: Read\nmodel: sonnet\n---\n\nOriginal body.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());
        vm.BeginEditCommand.Execute(null);

        vm.EditDescription = "new desc";
        vm.EditTools = "Read, Grep, Bash";
        vm.EditBody = "Rewritten body.";
        await vm.SaveAsync();

        Assert.IsFalse(vm.IsEditing, "Save exits edit mode.");
        Assert.AreEqual("new desc", vm.CardDescription, "Card reflects the saved description.");
        Assert.AreEqual("Read, Grep, Bash", vm.CardTools);

        // Confirm it actually hit disk and round-trips.
        string onDisk = await File.ReadAllTextAsync(path);
        StringAssert.Contains(onDisk, "description: new desc");
        StringAssert.Contains(onDisk, "tools: Read, Grep, Bash");
        StringAssert.Contains(onDisk, "Rewritten body.");
        Assert.IsFalse(onDisk.Contains("Original body."), "Old body must be replaced.");
    }

    [TestMethod]
    public async Task Save_PreservesUnknownKeysAndComments()
    {
        string path = Path.Combine(Home, "agents", "reviewer.md");
        Write(path,
            "---\n" +
            "# leading comment\n" +
            "name: reviewer\n" +
            "x-custom: keep-me\n" +
            "description: old\n" +
            "---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());
        vm.BeginEditCommand.Execute(null);
        vm.EditDescription = "new";
        await vm.SaveAsync();

        string onDisk = await File.ReadAllTextAsync(path);
        StringAssert.Contains(onDisk, "# leading comment", "Comments must survive an edit-save.");
        StringAssert.Contains(onDisk, "x-custom: keep-me", "Un-modelled keys must survive an edit-save.");
        StringAssert.Contains(onDisk, "description: new");
    }

    [TestMethod]
    public async Task Save_FirstThisSession_ShowsRestartHint()
    {
        // Note: the restart-hint flag is static (process-session lifetime), so
        // by the time the full suite runs this may already be tripped.  Assert
        // the message is non-empty and starts with "Saved." either way — the
        // once-per-session nuance is covered by the hint's own logic.
        string path = Path.Combine(Home, "skills", "s", "SKILL.md");
        Write(path, "---\nname: s\ndescription: d\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(SkillRows(vm).Single());
        vm.BeginEditCommand.Execute(null);
        vm.EditName = "s2";
        await vm.SaveAsync();

        Assert.IsNotNull(vm.LastSaveMessage);
        StringAssert.StartsWith(vm.LastSaveMessage!, "Saved.");
    }

    [TestMethod]
    public async Task CancelEdit_DiscardsChanges()
    {
        string path = Path.Combine(Home, "agents", "reviewer.md");
        Write(path, "---\nname: reviewer\ndescription: keep\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());
        vm.BeginEditCommand.Execute(null);
        vm.EditDescription = "discarded";
        vm.CancelEditCommand.Execute(null);

        Assert.IsFalse(vm.IsEditing);
        Assert.AreEqual("keep", vm.CardDescription, "Card retains the original after cancel.");

        string onDisk = await File.ReadAllTextAsync(path);
        StringAssert.Contains(onDisk, "description: keep", "Cancelled edits must not touch disk.");
        Assert.IsFalse(onDisk.Contains("discarded"));
    }

    [TestMethod]
    public async Task Save_EmptyTools_RemovesKey()
    {
        string path = Path.Combine(Home, "agents", "reviewer.md");
        Write(path, "---\nname: reviewer\ntools: Read, Grep\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());
        vm.BeginEditCommand.Execute(null);
        vm.EditTools = "";
        await vm.SaveAsync();

        string onDisk = await File.ReadAllTextAsync(path);
        Assert.IsFalse(onDisk.Contains("tools:"), "Clearing the tools field removes the key entirely.");
    }

    // ── Raw front-matter editing ─────────────────────────────────────────

    [TestMethod]
    public async Task ToggleRawMode_SeedsRawFromTypedState()
    {
        Write(Path.Combine(Home, "agents", "reviewer.md"),
            "---\nname: reviewer\ndescription: orig\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());
        vm.BeginEditCommand.Execute(null);

        // A typed edit BEFORE toggling should be reflected in the seeded raw.
        vm.EditDescription = "typed-desc";
        vm.ToggleRawModeCommand.Execute(null);

        Assert.IsTrue(vm.IsRawMode);
        Assert.IsFalse(vm.IsTypedEditVisible, "Typed card hides when raw mode is on.");
        Assert.IsTrue(vm.IsRawEditVisible);
        StringAssert.Contains(vm.EditRawFrontMatter!, "name: reviewer");
        StringAssert.Contains(vm.EditRawFrontMatter!, "description: typed-desc",
            "Raw box is seeded from the current typed edits, not the on-disk original.");
        // Raw block excludes the --- fences (the editor manages those).
        Assert.IsFalse(vm.EditRawFrontMatter!.Contains("---"), "Raw block excludes the delimiter fences.");
    }

    [TestMethod]
    public async Task Save_FromRawMode_WritesRawContent_IncludingNewArbitraryKey()
    {
        string path = Path.Combine(Home, "agents", "reviewer.md");
        Write(path, "---\nname: reviewer\ndescription: orig\n---\n\nOriginal body.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());
        vm.BeginEditCommand.Execute(null);
        vm.ToggleRawModeCommand.Execute(null);

        // Author the raw front-matter directly, including a key the typed
        // fields don't model.
        vm.EditRawFrontMatter = "name: reviewer\ndescription: via-raw\nx-custom: arbitrary-value";
        vm.EditBody = "Raw body.";
        await vm.SaveAsync();

        Assert.IsFalse(vm.IsRawMode, "Save exits raw mode.");
        string onDisk = await File.ReadAllTextAsync(path);
        StringAssert.Contains(onDisk, "description: via-raw");
        StringAssert.Contains(onDisk, "x-custom: arbitrary-value",
            "An arbitrary key authored in raw mode is written to disk.");
        StringAssert.Contains(onDisk, "Raw body.");

        // And the un-modelled key survives a reload (round-trip).
        Assert.AreEqual("arbitrary-value", YamlFrontMatter.Parse(onDisk).FindScalar("x-custom"));
    }

    [TestMethod]
    public async Task ToggleRawMode_Off_DiscardsRawEdits_RevertsToTyped()
    {
        Write(Path.Combine(Home, "agents", "reviewer.md"),
            "---\nname: reviewer\ndescription: orig\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());
        vm.BeginEditCommand.Execute(null);
        vm.EditDescription = "typed-desc";

        vm.ToggleRawModeCommand.Execute(null);   // on
        vm.EditRawFrontMatter = "name: reviewer\ndescription: raw-desc";   // diverge in raw
        vm.ToggleRawModeCommand.Execute(null);   // off → discard raw

        Assert.IsFalse(vm.IsRawMode);
        Assert.IsNull(vm.EditRawFrontMatter, "Leaving raw mode clears the raw text.");
        Assert.AreEqual("typed-desc", vm.EditDescription,
            "Typed fields keep their values — raw edits are discarded on toggle-off.");
    }

    [TestMethod]
    public async Task RawMode_DoesNotApplyToReadOnlyPluginRows()
    {
        Write(Path.Combine(Home, "plugins", "p", "skills", "w", "SKILL.md"), "---\nname: w\n---\n\nB.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(SkillRows(vm).Single());

        Assert.IsFalse(vm.ToggleRawModeCommand.CanExecute(null),
            "Raw-mode toggle is gated on CanEdit — disabled for read-only plugin rows.");
    }

    // ── Robustness / error conditions ────────────────────────────────────

    [TestMethod]
    public async Task Refresh_NoArtifacts_LeavesEmptyListsNoCrash()
    {
        // Fresh sandbox, nothing under ~/.claude — refresh must not throw and
        // the segment lists end up empty.
        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();

        Assert.AreEqual(0, vm.AgentItems.Count);
        Assert.AreEqual(0, vm.SkillItems.Count);
        Assert.AreEqual(0, vm.CommandItems.Count);
        Assert.IsFalse(vm.IsBusy);
    }

    [TestMethod]
    public async Task LoadArtifact_MalformedContent_DegradesToRawTextNoCrash()
    {
        // A file whose front-matter never closes (unterminated) — Parse reports
        // not-present; the page must show the raw text rather than break.
        string path = Path.Combine(Home, "agents", "broken.md");
        Write(path, "---\nname: broken\nthis never closes the front-matter block\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        if (vm.LastDescriptionFill is not null)
        {
            await vm.LastDescriptionFill;
        }

        await vm.LoadArtifactAsync(AgentRows(vm).Single());

        Assert.IsTrue(vm.IsViewerVisible, "Even a malformed file opens the detail pane.");
        StringAssert.Contains(vm.ViewerBody!, "this never closes",
            "Malformed front-matter degrades to showing the raw text.");
    }

    [TestMethod]
    public async Task Save_IntoReadOnlyDirectory_SurfacesFailureMessageNoCrash()
    {
        // Windows-only: the read-only file attribute reliably blocks
        // File.Replace there.  On Unix, rename(2) depends on the containing
        // directory's permissions, not the file's read-only bit, so the save
        // would succeed and this assertion wouldn't hold — skip rather than
        // assert false (same convention as the other OS-specific tests).
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Make the target file read-only so the atomic write fails; the VM must
        // surface a "Save failed" status rather than throw.
        string path = Path.Combine(Home, "agents", "ro.md");
        Write(path, "---\nname: ro\ndescription: d\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(_project);
        await vm.RefreshAsync();
        if (vm.LastDescriptionFill is not null)
        {
            await vm.LastDescriptionFill;
        }
        await vm.LoadArtifactAsync(AgentRows(vm).Single());
        vm.BeginEditCommand.Execute(null);
        vm.EditDescription = "changed";

        var fi = new FileInfo(path) { IsReadOnly = true };
        try
        {
            await vm.SaveAsync();

            // File.Replace into a read-only target throws → caught → status set.
            Assert.IsTrue(vm.IsEditing, "On save failure the editor stays in edit mode.");
            Assert.IsNotNull(vm.LastSaveMessage);
            StringAssert.StartsWith(vm.LastSaveMessage!, "Save failed");
        }
        finally
        {
            fi.IsReadOnly = false;
        }
    }
}
