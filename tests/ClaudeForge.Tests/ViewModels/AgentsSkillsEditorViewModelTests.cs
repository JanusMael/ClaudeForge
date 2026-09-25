using System.IO;
using System.Linq;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AppServices.Abstractions.Dialogs;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.AppServices.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Locks the "Agents &amp; Skills" page VM (group #2): scope-aware
/// population of the three segment lists, lazy load → typed card + body
/// projection, plugin rows surfaced read-only, and the viewer open/close
/// toggle.
/// </summary>
public sealed class AgentsSkillsEditorViewModelTests : IDisposable
{
    private string _sandbox = string.Empty;
    private string _project = string.Empty;

    public AgentsSkillsEditorViewModelTests() => Setup();

    private void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_asvm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        _project = Path.Combine(Path.GetTempPath(), "claudetest_asvm_proj_" + Guid.NewGuid().ToString("N"));
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

    // The segment collections are flat [headers + rows]; these helpers extract
    // just the artifact rows so the assertions read like the pre-grouping shape.
    private static IReadOnlyList<ArtifactRowViewModel> AgentRows(AgentsSkillsEditorViewModel vm) =>
        vm.AgentItems.OfType<ArtifactRowViewModel>().ToList();

    private static IReadOnlyList<ArtifactRowViewModel> SkillRows(AgentsSkillsEditorViewModel vm) =>
        vm.SkillItems.OfType<ArtifactRowViewModel>().ToList();

    private static IReadOnlyList<ArtifactRowViewModel> CommandRows(AgentsSkillsEditorViewModel vm) =>
        vm.CommandItems.OfType<ArtifactRowViewModel>().ToList();

    [Fact]
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

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();

        // Agents: user reviewer + project proj-agent.
        MessageAssert.SameElements(
            new[] { "reviewer", "proj-agent" },
            AgentRows(vm).Select(a => a.DisplayName).ToArray());
        // Skills: user pdf + plugin widget.
        MessageAssert.SameElements(
            new[] { "pdf", "widget" },
            SkillRows(vm).Select(s => s.DisplayName).ToArray());
        // Commands: user summarise.
        Assert.Equal(
            new[] { "summarise" },
            CommandRows(vm).Select(c => c.DisplayName).ToArray());
    }

    [Fact]
    public async Task Refresh_GroupsYoursBeforePlugin_WithSectionHeaders()
    {
        Write(Path.Combine(Home, "skills", "user-skill", "SKILL.md"), "---\nname: user-skill\n---\n\nB.\n");
        Write(Path.Combine(Home, "plugins", "mkt", "skills", "plug-skill", "SKILL.md"), "---\nname: plug-skill\n---\n\nB.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();

        // Expected flat shape: [Yours header, user-skill row, Plugin header, plug-skill row].
        var items = vm.SkillItems.ToList();
        Assert.Equal(4, items.Count);

        var h0 = (ArtifactSectionHeaderViewModel)items[0];
        Assert.Equal("Yours", h0.Header);
        Assert.False(h0.IsReadOnly);
        Assert.Equal("user-skill", ((ArtifactRowViewModel)items[1]).DisplayName);

        var h2 = (ArtifactSectionHeaderViewModel)items[2];
        Assert.Equal("Plugin", h2.Header);
        Assert.True(h2.IsReadOnly, "The Plugin section header carries the read-only badge.");
        Assert.Equal("plug-skill", ((ArtifactRowViewModel)items[3]).DisplayName);
    }

    [Fact]
    public async Task Refresh_OmitsHeaderForEmptyGroup()
    {
        // Only user items → no Plugin header.
        Write(Path.Combine(Home, "agents", "only-user.md"), "---\nname: only-user\n---\n\nB.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();

        Assert.False(vm.AgentItems.OfType<ArtifactSectionHeaderViewModel>().Any(h => h.Header == "Plugin"),
            "A group with no rows must not get a header.");
        Assert.Contains(vm.AgentItems.OfType<ArtifactSectionHeaderViewModel>(), h => h.Header == "Yours");
    }

    [Fact]
    public async Task Refresh_PluginRowSource_DerivedFromPluginPath()
    {
        Write(Path.Combine(Home, "plugins", "everything-claude-code", "skills", "widget", "SKILL.md"),
            "---\nname: widget\n---\n\nB.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();

        ArtifactRowViewModel row = SkillRows(vm).Single(r => r.DisplayName == "widget");
        MessageAssert.Equal("everything-claude-code", row.Source,
            "Plugin row Source disambiguates by the providing plugin's name.");
    }

    [Fact]
    public async Task Refresh_LazyDescriptions_FillSubtitlesAfterRowsAppear()
    {
        Write(Path.Combine(Home, "agents", "a.md"), "---\nname: a\ndescription: has desc\n---\n\nB.\n");
        Write(Path.Combine(Home, "agents", "b.md"), "---\nname: b\n---\n\nB.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();

        // Rows are present immediately (stat-only) — subtitles fill in via the
        // background pass, which the test seam lets us await.
        Assert.NotNull(vm.LastDescriptionFill);
        await vm.LastDescriptionFill!;

        Assert.Equal("has desc", AgentRows(vm).Single(r => r.DisplayName == "a").Subtitle);
        Assert.Equal("(no description)", AgentRows(vm).Single(r => r.DisplayName == "b").Subtitle);
    }

    [Fact]
    public async Task Refresh_PluginSkillRow_IsReadOnly()
    {
        Write(Path.Combine(Home, "plugins", "mkt", "plug", "skills", "widget", "SKILL.md"),
            "---\nname: widget\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();

        ArtifactRowViewModel widget = SkillRows(vm).Single(s => s.DisplayName == "widget");
        Assert.True(widget.IsPlugin);
        Assert.False(widget.IsWritable, "Plugin skill row must be read-only.");
        Assert.Equal("Plugin", widget.ScopeLabel);
    }

    [Fact]
    public async Task LoadArtifact_Agent_PopulatesCardAndBody_ShowsViewer()
    {
        Write(Path.Combine(Home, "agents", "reviewer.md"),
            "---\nname: reviewer\ndescription: Reviews code\ntools: Read, Grep, Bash\nmodel: sonnet\n---\n\nYou are a reviewer.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        ArtifactRowViewModel row = AgentRows(vm).Single();

        Assert.False(vm.IsViewerVisible, "Viewer starts hidden.");
        await vm.LoadArtifactAsync(row);

        Assert.True(vm.IsViewerVisible, "Selecting a row shows the detail pane.");
        Assert.Same(row, vm.SelectedArtifact);
        Assert.Equal("reviewer", vm.CardName);
        Assert.Equal("Reviews code", vm.CardDescription);
        Assert.Equal("sonnet", vm.CardModel);
        Assert.Equal("Read, Grep, Bash", vm.CardTools);
        Assert.True(vm.CardShowName);
        Assert.True(vm.CardShowToolsAndModel);
        OrdinalAssert.Contains("You are a reviewer.", vm.ViewerBody!);
        // The card carries the front-matter; the body excludes it.
        Assert.False(vm.ViewerBody!.Contains("name: reviewer"),
            "Body viewer shows the post-front-matter prose, not the front-matter (that's the card's job).");
    }

    [Fact]
    public async Task LoadArtifact_SlashCommand_HidesNameAndToolsRows()
    {
        Write(Path.Combine(Home, "commands", "summarise.md"),
            "---\ndescription: Summarise the PR\n---\n\nPrompt template.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(CommandRows(vm).Single());

        Assert.Equal("Summarise the PR", vm.CardDescription);
        Assert.False(vm.CardShowName, "Slash commands have no name field — name row hidden.");
        Assert.False(vm.CardShowToolsAndModel, "Slash commands have no tools/model — those rows hidden.");
    }

    [Fact]
    public async Task CloseViewer_ResetsViewerState()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"),
            "---\nname: pdf\ndescription: PDF tools\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(SkillRows(vm).Single());
        Assert.True(vm.IsViewerVisible);

        vm.CloseViewerCommand.Execute(null);

        Assert.False(vm.IsViewerVisible);
        Assert.Null(vm.SelectedArtifact);
        Assert.Null(vm.ViewerBody);
        Assert.Null(vm.CardName);
    }

    [Fact]
    public async Task LoadArtifact_MissingFile_ShowsPlaceholderNotCrash()
    {
        string path = Path.Combine(Home, "agents", "ghost.md");
        Write(path, "---\nname: ghost\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
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

        Assert.True(vm.IsViewerVisible);
        OrdinalAssert.Contains("no longer available", vm.ViewerBody!);
    }

    // ── Group #3 — editor flow ───────────────────────────────────────────

    [Fact]
    public async Task BeginEdit_SeedsEditFieldsFromCard()
    {
        Write(Path.Combine(Home, "agents", "reviewer.md"),
            "---\nname: reviewer\ndescription: Reviews code\ntools: Read, Grep\nmodel: sonnet\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());

        Assert.True(vm.CanEdit);
        vm.BeginEditCommand.Execute(null);

        Assert.True(vm.IsEditing);
        Assert.Equal("reviewer", vm.EditName);
        Assert.Equal("Reviews code", vm.EditDescription);
        Assert.Equal("Read, Grep", vm.EditTools);
        Assert.Equal("sonnet", vm.EditModel);
    }

    [Fact]
    public async Task PluginRow_CanEditFalse_BeginEditIsNoOp()
    {
        Write(Path.Combine(Home, "plugins", "p", "skills", "w", "SKILL.md"),
            "---\nname: w\ndescription: plugin\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(SkillRows(vm).Single());

        Assert.False(vm.CanEdit, "Plugin rows are read-only.");
        Assert.False(vm.BeginEditCommand.CanExecute(null), "BeginEdit must be disabled for plugin rows.");
        vm.BeginEditCommand.Execute(null);
        Assert.False(vm.IsEditing, "BeginEdit must be a no-op on a read-only row.");
    }

    [Fact]
    public async Task Save_PersistsEditsToDisk_AndRefreshesCard()
    {
        string path = Path.Combine(Home, "agents", "reviewer.md");
        Write(path, "---\nname: reviewer\ndescription: old desc\ntools: Read\nmodel: sonnet\n---\n\nOriginal body.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());
        vm.BeginEditCommand.Execute(null);

        vm.EditDescription = "new desc";
        vm.EditTools = "Read, Grep, Bash";
        vm.EditBody = "Rewritten body.";
        await vm.SaveAsync();

        Assert.False(vm.IsEditing, "Save exits edit mode.");
        MessageAssert.Equal("new desc", vm.CardDescription, "Card reflects the saved description.");
        Assert.Equal("Read, Grep, Bash", vm.CardTools);

        // Confirm it actually hit disk and round-trips.
        string onDisk = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        OrdinalAssert.Contains("description: new desc", onDisk);
        OrdinalAssert.Contains("tools: Read, Grep, Bash", onDisk);
        OrdinalAssert.Contains("Rewritten body.", onDisk);
        Assert.False(onDisk.Contains("Original body."), "Old body must be replaced.");
    }

    [Fact]
    public async Task Save_UpdatesTheListRowSubtitle_NotJustTheDetailCard()
    {
        // Regression: SaveAsync used to refresh only the detail pane's Card*
        // properties. The LIST renders ArtifactRowViewModel.Subtitle, so a saved
        // description edit kept showing the OLD text in the list until the next
        // full RefreshAsync — the user's edit appeared not to have taken.
        string path = Path.Combine(Home, "agents", "reviewer.md");
        Write(path, "---\nname: reviewer\ndescription: old desc\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        if (vm.LastDescriptionFill is { } fill)
        {
            await fill;
        }

        ArtifactRowViewModel row = AgentRows(vm).Single();
        MessageAssert.Equal("old desc", row.Subtitle, "Precondition: the row shows the pre-edit description.");

        await vm.LoadArtifactAsync(row);
        vm.BeginEditCommand.Execute(null);
        vm.EditDescription = "new desc";
        await vm.SaveAsync();

        MessageAssert.Equal("new desc", row.Subtitle,
            "The row that launched the editor must show the saved description.");
    }

    [Fact]
    public async Task Save_ClearingDescription_ShowsThePlaceholderOnTheRow()
    {
        // Emptying the description removes the key, so the row must fall back to
        // the same placeholder the initial load would have used — not to an empty
        // string, and not to the stale old text.
        string path = Path.Combine(Home, "agents", "reviewer.md");
        Write(path, "---\nname: reviewer\ndescription: old desc\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        if (vm.LastDescriptionFill is { } fill)
        {
            await fill;
        }

        ArtifactRowViewModel row = AgentRows(vm).Single();
        await vm.LoadArtifactAsync(row);
        vm.BeginEditCommand.Execute(null);
        vm.EditDescription = string.Empty;
        await vm.SaveAsync();

        Assert.Equal("(no description)", row.Subtitle);
    }

    [Fact]
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

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());
        vm.BeginEditCommand.Execute(null);
        vm.EditDescription = "new";
        await vm.SaveAsync();

        string onDisk = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        MessageAssert.Contains("# leading comment", onDisk, "Comments must survive an edit-save.");
        MessageAssert.Contains("x-custom: keep-me", onDisk, "Un-modelled keys must survive an edit-save.");
        OrdinalAssert.Contains("description: new", onDisk);
    }

    [Fact]
    public async Task Save_FirstThisSession_ShowsRestartHint()
    {
        // Note: the restart-hint flag is static (process-session lifetime), so
        // by the time the full suite runs this may already be tripped.  Assert
        // the message is non-empty and starts with "Saved." either way — the
        // once-per-session nuance is covered by the hint's own logic.
        string path = Path.Combine(Home, "skills", "s", "SKILL.md");
        Write(path, "---\nname: s\ndescription: d\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(SkillRows(vm).Single());
        vm.BeginEditCommand.Execute(null);
        vm.EditName = "s2";
        await vm.SaveAsync();

        Assert.NotNull(vm.LastActionMessage);
        OrdinalAssert.StartsWith("Saved.", vm.LastActionMessage!);
    }

    [Fact]
    public async Task CancelEdit_DiscardsChanges()
    {
        string path = Path.Combine(Home, "agents", "reviewer.md");
        Write(path, "---\nname: reviewer\ndescription: keep\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());
        vm.BeginEditCommand.Execute(null);
        vm.EditDescription = "discarded";
        vm.CancelEditCommand.Execute(null);

        Assert.False(vm.IsEditing);
        MessageAssert.Equal("keep", vm.CardDescription, "Card retains the original after cancel.");

        string onDisk = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        MessageAssert.Contains("description: keep", onDisk, "Cancelled edits must not touch disk.");
        OrdinalAssert.DoesNotContain("discarded", onDisk);
    }

    [Fact]
    public async Task Save_EmptyTools_RemovesKey()
    {
        string path = Path.Combine(Home, "agents", "reviewer.md");
        Write(path, "---\nname: reviewer\ntools: Read, Grep\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());
        vm.BeginEditCommand.Execute(null);
        vm.EditTools = "";
        await vm.SaveAsync();

        string onDisk = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.False(onDisk.Contains("tools:"), "Clearing the tools field removes the key entirely.");
    }

    // ── Raw front-matter editing ─────────────────────────────────────────

    [Fact]
    public async Task ToggleRawMode_SeedsRawFromTypedState()
    {
        Write(Path.Combine(Home, "agents", "reviewer.md"),
            "---\nname: reviewer\ndescription: orig\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());
        vm.BeginEditCommand.Execute(null);

        // A typed edit BEFORE toggling should be reflected in the seeded raw.
        vm.EditDescription = "typed-desc";
        vm.ToggleRawModeCommand.Execute(null);

        Assert.True(vm.IsRawMode);
        Assert.False(vm.IsTypedEditVisible, "Typed card hides when raw mode is on.");
        Assert.True(vm.IsRawEditVisible);
        OrdinalAssert.Contains("name: reviewer", vm.EditRawFrontMatter!);
        MessageAssert.Contains("description: typed-desc", vm.EditRawFrontMatter!,
            "Raw box is seeded from the current typed edits, not the on-disk original.");
        // Raw block excludes the --- fences (the editor manages those).
        Assert.False(vm.EditRawFrontMatter!.Contains("---"), "Raw block excludes the delimiter fences.");
    }

    [Fact]
    public async Task Save_FromRawMode_WritesRawContent_IncludingNewArbitraryKey()
    {
        string path = Path.Combine(Home, "agents", "reviewer.md");
        Write(path, "---\nname: reviewer\ndescription: orig\n---\n\nOriginal body.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());
        vm.BeginEditCommand.Execute(null);
        vm.ToggleRawModeCommand.Execute(null);

        // Author the raw front-matter directly, including a key the typed
        // fields don't model.
        vm.EditRawFrontMatter = "name: reviewer\ndescription: via-raw\nx-custom: arbitrary-value";
        vm.EditBody = "Raw body.";
        await vm.SaveAsync();

        Assert.False(vm.IsRawMode, "Save exits raw mode.");
        string onDisk = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        OrdinalAssert.Contains("description: via-raw", onDisk);
        MessageAssert.Contains("x-custom: arbitrary-value", onDisk,
            "An arbitrary key authored in raw mode is written to disk.");
        OrdinalAssert.Contains("Raw body.", onDisk);

        // And the un-modelled key survives a reload (round-trip).
        Assert.Equal("arbitrary-value", YamlFrontMatter.Parse(onDisk).FindScalar("x-custom"));
    }

    [Fact]
    public async Task ToggleRawMode_Off_DiscardsRawEdits_RevertsToTyped()
    {
        Write(Path.Combine(Home, "agents", "reviewer.md"),
            "---\nname: reviewer\ndescription: orig\n---\n\nBody.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(AgentRows(vm).Single());
        vm.BeginEditCommand.Execute(null);
        vm.EditDescription = "typed-desc";

        vm.ToggleRawModeCommand.Execute(null);   // on
        vm.EditRawFrontMatter = "name: reviewer\ndescription: raw-desc";   // diverge in raw
        vm.ToggleRawModeCommand.Execute(null);   // off → discard raw

        Assert.False(vm.IsRawMode);
        MessageAssert.Null(vm.EditRawFrontMatter, "Leaving raw mode clears the raw text.");
        MessageAssert.Equal("typed-desc", vm.EditDescription,
            "Typed fields keep their values — raw edits are discarded on toggle-off.");
    }

    [Fact]
    public async Task RawMode_DoesNotApplyToReadOnlyPluginRows()
    {
        Write(Path.Combine(Home, "plugins", "p", "skills", "w", "SKILL.md"), "---\nname: w\n---\n\nB.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        await vm.LoadArtifactAsync(SkillRows(vm).Single());

        Assert.False(vm.ToggleRawModeCommand.CanExecute(null),
            "Raw-mode toggle is gated on CanEdit — disabled for read-only plugin rows.");
    }

    // ── Robustness / error conditions ────────────────────────────────────

    [Fact]
    public async Task Refresh_NoArtifacts_LeavesEmptyListsNoCrash()
    {
        // Fresh sandbox, nothing under ~/.claude — refresh must not throw and
        // the segment lists end up empty.
        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();

        Assert.Empty(vm.AgentItems);
        Assert.Empty(vm.SkillItems);
        Assert.Empty(vm.CommandItems);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task LoadArtifact_MalformedContent_DegradesToRawTextNoCrash()
    {
        // A file whose front-matter never closes (unterminated) — Parse reports
        // not-present; the page must show the raw text rather than break.
        string path = Path.Combine(Home, "agents", "broken.md");
        Write(path, "---\nname: broken\nthis never closes the front-matter block\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        if (vm.LastDescriptionFill is not null)
        {
            await vm.LastDescriptionFill;
        }

        await vm.LoadArtifactAsync(AgentRows(vm).Single());

        Assert.True(vm.IsViewerVisible, "Even a malformed file opens the detail pane.");
        MessageAssert.Contains("this never closes", vm.ViewerBody!,
            "Malformed front-matter degrades to showing the raw text.");
    }

    [Fact]
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

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
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
            Assert.True(vm.IsEditing, "On save failure the editor stays in edit mode.");
            Assert.NotNull(vm.LastActionMessage);
            OrdinalAssert.StartsWith("Save failed", vm.LastActionMessage!);
        }
        finally
        {
            fi.IsReadOnly = false;
        }
    }

    // ── Delete (writable artifacts only) ─────────────────────────────────

    [Fact]
    public async Task DeleteArtifact_NullRow_NoOp()
    {
        StubDialogService dlg = new();
        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project, shellLauncher: null, dialogService: dlg);
        await vm.DeleteArtifactAsync(null);
        Assert.Equal(0, dlg.ConfirmCalls);
    }

    [Fact]
    public async Task DeleteArtifact_PluginRow_NotDeletable_NoOp()
    {
        string skillMd = Path.Combine(Home, "plugins", "mkt", "plug", "skills", "widget", "SKILL.md");
        Write(skillMd, "---\nname: widget\n---\n\nBody.\n");

        StubDialogService dlg = new() { ConfirmReturns = true };
        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project, shellLauncher: null, dialogService: dlg);
        await vm.RefreshAsync();
        if (vm.LastDescriptionFill is not null)
        {
            await vm.LastDescriptionFill;
        }

        ArtifactRowViewModel widget = SkillRows(vm).Single(s => s.DisplayName == "widget");
        Assert.False(widget.IsDeletable, "Plugin rows must report as non-deletable.");

        await vm.DeleteArtifactAsync(widget);

        MessageAssert.Equal(0, dlg.ConfirmCalls, "Plugin (read-only) rows must not prompt for delete.");
        Assert.True(File.Exists(skillMd), "Plugin-provided artifacts must never be deleted.");
    }

    [Fact]
    public async Task DeleteArtifact_UserAgent_Confirms_DeletesFile()
    {
        string path = Path.Combine(Home, "agents", "reviewer.md");
        Write(path, "---\nname: reviewer\n---\n\nBody.\n");

        StubDialogService dlg = new() { ConfirmReturns = true };
        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project, shellLauncher: null, dialogService: dlg);
        await vm.RefreshAsync();
        if (vm.LastDescriptionFill is not null)
        {
            await vm.LastDescriptionFill;
        }

        ArtifactRowViewModel row = AgentRows(vm).Single();
        Assert.True(row.IsDeletable);

        await vm.DeleteArtifactAsync(row);

        Assert.Equal(1, dlg.ConfirmCalls);
        Assert.False(File.Exists(path), "Confirmed → file deleted.");
        Assert.False(AgentRows(vm).Any(r => r.DisplayName == "reviewer"),
            "Deleted row drops out of the list after refresh.");
    }

    [Fact]
    public async Task DeleteArtifact_Skill_Confirms_DeletesWholeDirectory()
    {
        Write(Path.Combine(Home, "skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n\nBody.\n");
        Write(Path.Combine(Home, "skills", "pdf", "run.py"), "print('x')");
        string skillDir = Path.Combine(Home, "skills", "pdf");

        StubDialogService dlg = new() { ConfirmReturns = true };
        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project, shellLauncher: null, dialogService: dlg);
        await vm.RefreshAsync();
        if (vm.LastDescriptionFill is not null)
        {
            await vm.LastDescriptionFill;
        }

        ArtifactRowViewModel skill = SkillRows(vm).Single();
        Assert.True(skill.IsSkill);

        await vm.DeleteArtifactAsync(skill);

        Assert.False(Directory.Exists(skillDir),
            "Deleting a skill removes its whole directory, not just SKILL.md.");
    }

    [Fact]
    public async Task DeleteArtifact_UserDeclines_NoDelete()
    {
        string path = Path.Combine(Home, "commands", "summarise.md");
        Write(path, "---\ndescription: Summarise\n---\n\nBody.\n");

        StubDialogService dlg = new() { ConfirmReturns = false };
        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project, shellLauncher: null, dialogService: dlg);
        await vm.RefreshAsync();
        if (vm.LastDescriptionFill is not null)
        {
            await vm.LastDescriptionFill;
        }

        ArtifactRowViewModel row = CommandRows(vm).Single();

        await vm.DeleteArtifactAsync(row);

        Assert.Equal(1, dlg.ConfirmCalls);
        Assert.True(File.Exists(path), "Declined → file survives.");
    }

    [Fact]
    public async Task DeleteArtifact_OpenRow_ClosesViewerOnDelete()
    {
        string path = Path.Combine(Home, "agents", "reviewer.md");
        Write(path, "---\nname: reviewer\n---\n\nBody.\n");

        StubDialogService dlg = new() { ConfirmReturns = true };
        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project, shellLauncher: null, dialogService: dlg);
        await vm.RefreshAsync();
        if (vm.LastDescriptionFill is not null)
        {
            await vm.LastDescriptionFill;
        }

        ArtifactRowViewModel row = AgentRows(vm).Single();
        await vm.LoadArtifactAsync(row);
        Assert.True(vm.IsViewerVisible);

        await vm.DeleteArtifactAsync(row);

        Assert.False(vm.IsViewerVisible, "Deleting the open row closes the detail pane.");
    }

    /// <summary>
    /// Minimal IDialogService stub with trinary confirm support
    /// (true / false / null = X-close).  Mirrors the one in
    /// MemoryEditorViewModelTests; the rich DialogMessage/category confirm
    /// overload flattens to this four-string one via its default interface impl.
    /// </summary>
    /// <summary>
    /// Every skill Claude Code ships writes its description as a folded block
    /// scalar. The row must show the prose, not the "&gt;-" header token.
    /// </summary>
    /// <remarks>ⓘ Ported from <c>main</c> on 2026-09-16 with the parser it guards.</remarks>
    [Fact]
    public async Task Refresh_FoldedBlockDescription_ShowsTheProseNotTheHeaderToken()
    {
        Write(Path.Combine(Home, "skills", "folded", "SKILL.md"),
            "---\n" +
            "name: folded\n" +
            "description: >-\n" +
            "  Fetch, vet, and act on review feedback left on a pull request.\n" +
            "  Use this WHENEVER the developer says there is review feedback.\n" +
            "---\n" +
            "\n" +
            "Body.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        await vm.LastDescriptionFill!;

        MessageAssert.Equal(
            "Fetch, vet, and act on review feedback left on a pull request. "
            + "Use this WHENEVER the developer says there is review feedback.",
            SkillRows(vm).Single(r => r.DisplayName == "folded").Subtitle,
            "The folded block must reach the row as its prose; showing '>-' is the bug this guards.");
    }

    /// <summary>
    /// A row is one ellipsised line, so a description carrying newlines — a blank
    /// line inside a folded block, or any literal block — is flattened for the
    /// list. The detail pane and editor keep the real multi-line value.
    /// </summary>
    /// <remarks>ⓘ Ported from <c>main</c> on 2026-09-16 with the parser it guards.</remarks>
    [Fact]
    public async Task Refresh_MultiLineDescription_IsFlattenedToOneLineInTheList()
    {
        Write(Path.Combine(Home, "skills", "para", "SKILL.md"),
            "---\n" +
            "name: para\n" +
            "description: |-\n" +
            "  first line\n" +
            "  second line\n" +
            "---\n" +
            "\n" +
            "Body.\n");

        var vm = new AgentsSkillsEditorViewModel(ClaudeEnvironment.Empty, _project);
        await vm.RefreshAsync();
        await vm.LastDescriptionFill!;

        string subtitle = SkillRows(vm).Single(r => r.DisplayName == "para").Subtitle!;

        Assert.Equal("first line second line", subtitle);
        Assert.False(subtitle.Contains('\n'), "A list row must never carry an embedded newline.");
    }

    private sealed class StubDialogService : IDialogService
    {
        public bool ConfirmReturns { get; set; }
        public bool ConfirmReturnsNull { get; set; }
        public int ConfirmCalls { get; private set; }

        public Task<string?> PickFolderAsync(string? title = null) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? title = null, IReadOnlyList<FilePickerFilter>? filters = null) =>
            Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string? title, string defaultFileName,
                                               IReadOnlyList<FilePickerFilter>? filters = null) =>
            Task.FromResult<string?>(null);

        public Task ShowAlertAsync(string title, string message) => Task.CompletedTask;

        public Task<string?> ShowInputAsync(string title, string prompt, string? placeholder = null) =>
            Task.FromResult<string?>(null);

        public Task<bool?> ShowConfirmAsync(string title, string message,
                                            string confirmLabel = "Confirm", string cancelLabel = "Cancel")
        {
            ConfirmCalls++;
            return Task.FromResult<bool?>(ConfirmReturnsNull ? null : ConfirmReturns);
        }

        public Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt) => Task.FromResult(false);
    }
}
