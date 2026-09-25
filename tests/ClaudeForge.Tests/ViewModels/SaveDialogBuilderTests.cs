using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.AgentForge.Sdk.Diagnostics;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// <see cref="SaveDialogBuilder"/> — the projection from dirty SDK documents to the
/// rows the confirmation dialog renders.
///
/// <para>
/// Barely covered before Phase 5 slice 5: exactly one test touched it, asserting that
/// one env change reached the diff for one product. Nothing exercised more than one
/// source, the restore wording, the path shortening, or the accessible names — and
/// the "one product at a time" blind spot has now been the cause of three separate
/// findings in this refactor, so the multi-source case is asserted here deliberately.
/// </para>
/// </summary>
public sealed class SaveDialogBuilderTests
{
    private static SaveDialogText Text => ClaudeSaveDialogText.Create();

    private static AgentConfigClientCore MakeClient(string filePath = "user.json")
    {
        SettingsDocument doc = new(ConfigScope.User, filePath, new JsonObject(), isReadOnly: false);
        SettingsWorkspace ws = new([doc], ClaudeMergePolicy.Instance);
        return ClaudeCodeClient.FromExistingWorkspace(ClaudeEnvironment.Empty, 
            ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
    }

    private static AgentConfigClientCore DirtyClient(string key = "model", string value = "opus")
    {
        AgentConfigClientCore client = MakeClient();
        client.SetValue(key, value);
        return client;
    }

    [Fact]
    public void Build_NothingDirty_ReturnsNull()
    {
        MessageAssert.Null(SaveDialogBuilder.Build([new DirtySource(MakeClient(), "Claude Code")], Text),
            "A save with no content difference must not raise a dialog at all.");
    }

    [Fact]
    public void Build_OneChange_ProducesOneSectionCarryingTheDiff()
    {
        SaveChangesDialogViewModel? dlg =
            SaveDialogBuilder.Build([new DirtySource(DirtyClient(), "Claude Code")], Text);

        Assert.NotNull(dlg);
        Assert.Single(dlg!.Sections);
        MessageAssert.Equal("Claude Code", dlg.Sections[0].WorkspaceName,
            "The section is grouped under the name the caller paired with the client.");
        Assert.Equal("user", dlg.Sections[0].ScopeText);
        Assert.Contains(dlg.Sections[0].Entries, e => e.Key == "model");
    }

    /// <summary>
    /// The multi-source case. Every other fixture in the suite builds one client, so
    /// a builder that stopped after the first source would look perfectly healthy.
    /// </summary>
    [Fact]
    public void Build_TwoSources_ProducesASectionForEachInOrder()
    {
        SaveChangesDialogViewModel? dlg = SaveDialogBuilder.Build(
            [
                new DirtySource(DirtyClient("model", "opus"), "First Product"),
                new DirtySource(DirtyClient("outputStyle", "concise"), "Second Product"),
            ],
            Text);

        Assert.NotNull(dlg);
        MessageAssert.SequenceEqual(
            new[] { "First Product", "Second Product" },
            dlg!.Sections.Select(s => s.WorkspaceName).ToArray(),
            "Both sources must contribute, in the order they were handed over.");
    }

    [Fact]
    public void Build_SkipsASourceWhoseDocumentsHaveNoRealDiff()
    {
        SaveChangesDialogViewModel? dlg = SaveDialogBuilder.Build(
            [
                new DirtySource(MakeClient(), "Clean Product"),
                new DirtySource(DirtyClient(), "Dirty Product"),
            ],
            Text);

        Assert.NotNull(dlg);
        MessageAssert.SequenceEqual(
            new[] { "Dirty Product" },
            dlg!.Sections.Select(s => s.WorkspaceName).ToArray(),
            "A source with nothing to write must not contribute an empty section.");
    }

    [Fact]
    public void Build_RestoreContext_SwitchesModeAndEveryPieceOfWordingWithIt()
    {
        SaveChangesDialogViewModel? dlg = SaveDialogBuilder.Build(
            [new DirtySource(DirtyClient(), "Claude Code")], Text, isRestoreContext: true);

        Assert.NotNull(dlg);
        Assert.Equal(SaveDialogMode.Restore, dlg!.Mode);
        Assert.Equal(Text.WillBeRestoredTo, dlg.ActionVerb);
        MessageAssert.Equal(Text.WillBeRestoredTo, dlg.Sections[0].ActionVerb,
            "The section carries the verb too, so it renders without reaching back to the dialog.");
        Assert.Equal(Text.RestoreTitle, dlg.WindowTitle);
        Assert.Equal(Text.RestoreConfirmButton, dlg.ConfirmButtonLabel);
    }

    [Fact]
    public void Build_SaveContext_UsesTheSaveWording()
    {
        SaveChangesDialogViewModel? dlg =
            SaveDialogBuilder.Build([new DirtySource(DirtyClient(), "Claude Code")], Text);

        Assert.NotNull(dlg);
        Assert.Equal(SaveDialogMode.Save, dlg!.Mode);
        Assert.Equal(Text.WillBeWrittenTo, dlg.Sections[0].ActionVerb);
        Assert.Equal(Text.SaveTitle, dlg.WindowTitle);
    }

    /// <summary>
    /// The pill shows a bare +/-/~ glyph, so an empty automation name reads to a
    /// screen reader as nothing at all — a silent failure with no visual symptom.
    /// <para>
    /// ⚠ Asserts against the raw <see cref="SaveDialogText"/> properties, deliberately
    /// NOT against <c>Text.AccessibleNameFor(e.Kind)</c>. The first version of this
    /// test did the latter, and a canary that broke the kind mapping left it green —
    /// both sides of the comparison were computed by the thing under test. A fixture
    /// derived from what it is checking cannot detect that thing moving.
    /// </para>
    /// </summary>
    [Fact]
    public void Build_GivesEveryEntryTheAccessibleNameForItsOwnKind()
    {
        // A baseline that already has the key makes the edit a Modified rather than
        // an Added, so both mappings are exercised in one pass.
        SettingsDocument seeded = new(
            ConfigScope.User, "user.json",
            (JsonObject)JsonNode.Parse("""{"model":"sonnet"}""")!, isReadOnly: false);
        AgentConfigClientCore modified = ClaudeCodeClient.FromExistingWorkspace(ClaudeEnvironment.Empty, 
            new SettingsWorkspace([seeded], ClaudeMergePolicy.Instance),
            ConfigScope.User, schemaRegistry: new SchemaRegistry());
        modified.SetValue("model", "opus");

        SaveChangesDialogViewModel? dlg = SaveDialogBuilder.Build(
            [new DirtySource(DirtyClient("outputStyle", "concise"), "Added"), new DirtySource(modified, "Modified")], Text);

        Assert.NotNull(dlg);
        List<SaveChangeEntryViewModel> entries = dlg!.Sections.SelectMany(s => s.Entries).ToList();
        Assert.True(entries.Count > 0, "Precondition: there is at least one entry.");

        bool sawAdded = false;
        bool sawModified = false;
        foreach (SaveChangeEntryViewModel e in entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.KindAccessibleName),
                $"Entry '{e.Key}' has no screen-reader name for its change pill.");

            switch (e.Kind)
            {
                case ChangeKind.Added:
                    Assert.Equal(Text.KindAdded, e.KindAccessibleName);
                    sawAdded = true;
                    break;
                case ChangeKind.Removed:
                    Assert.Equal(Text.KindRemoved, e.KindAccessibleName);
                    break;
                default:
                    Assert.Equal(Text.KindModified, e.KindAccessibleName);
                    sawModified = true;
                    break;
            }
        }

        Assert.True(sawAdded, "Precondition: the fixture produced an added change.");
        Assert.True(sawModified, "Precondition: the fixture produced a modified change.");
    }

    [Fact]
    public void Build_ShortensPathsUnderTheHomeDirectory_AndLeavesOthersAlone()
    {
        string home = PlatformPaths.UserProfile;
        Assert.False(string.IsNullOrEmpty(home), "Precondition: a home directory is resolvable.");

        AgentConfigClientCore inside = MakeClient(Path.Combine(home, ".claude", "settings.json"));
        inside.SetValue("model", "opus");

        // A path that cannot be under the home directory on any platform.
        AgentConfigClientCore outside = MakeClient(Path.Combine("/etc", "agent", "settings.json"));
        outside.SetValue("model", "opus");

        SaveChangesDialogViewModel? dlg = SaveDialogBuilder.Build(
            [new DirtySource(inside, "Inside"), new DirtySource(outside, "Outside")], Text);

        Assert.NotNull(dlg);
        string insidePath = dlg!.Sections.Single(s => s.WorkspaceName == "Inside").FilePath;
        string outsidePath = dlg.Sections.Single(s => s.WorkspaceName == "Outside").FilePath;

        Assert.True(insidePath.StartsWith('~'),
            $"A path under the home directory is shown with a leading '~'; got '{insidePath}'.");
        Assert.False(insidePath.Contains('\\'),
            "Separators are normalised to '/' so the display matches the scope-legend table.");
        Assert.False(outsidePath.StartsWith('~'),
            $"A path outside the user profile is shown verbatim; got '{outsidePath}'.");
    }

    [Fact]
    public void Build_TruncatesLongValues_ButKeepsTheFullOneForTheTooltip()
    {
        string longValue = new('x', 200);
        AgentConfigClientCore client = MakeClient();
        client.SetValue("model", longValue);

        SaveChangesDialogViewModel? dlg =
            SaveDialogBuilder.Build([new DirtySource(client, "Claude Code")], Text);

        Assert.NotNull(dlg);
        SaveChangeEntryViewModel entry = dlg!.Sections[0].Entries.Single(e => e.Key == "model");

        Assert.True(entry.NewValue!.EndsWith('…'), "The displayed value is truncated.");
        Assert.True(entry.NewValue.Length < entry.FullNewValue!.Length,
            "The untruncated value is kept so the hover tooltip can show all of it.");
        OrdinalAssert.Contains(longValue, entry.FullNewValue);
    }
}
