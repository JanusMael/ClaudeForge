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
[TestClass]
public sealed class SaveDialogBuilderTests
{
    private static SaveDialogText Text => ClaudeSaveDialogText.Create();

    private static AgentConfigClientCore MakeClient(string filePath = "user.json")
    {
        SettingsDocument doc = new(ConfigScope.User, filePath, new JsonObject(), isReadOnly: false);
        SettingsWorkspace ws = new([doc], ClaudeMergePolicy.Instance);
        return ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
    }

    private static AgentConfigClientCore DirtyClient(string key = "model", string value = "opus")
    {
        AgentConfigClientCore client = MakeClient();
        client.SetValue(key, value);
        return client;
    }

    [TestMethod]
    public void Build_NothingDirty_ReturnsNull()
    {
        Assert.IsNull(SaveDialogBuilder.Build([(MakeClient(), "Claude Code")], Text),
            "A save with no content difference must not raise a dialog at all.");
    }

    [TestMethod]
    public void Build_OneChange_ProducesOneSectionCarryingTheDiff()
    {
        SaveChangesDialogViewModel? dlg =
            SaveDialogBuilder.Build([(DirtyClient(), "Claude Code")], Text);

        Assert.IsNotNull(dlg);
        Assert.AreEqual(1, dlg!.Sections.Count);
        Assert.AreEqual("Claude Code", dlg.Sections[0].WorkspaceName,
            "The section is grouped under the name the caller paired with the client.");
        Assert.AreEqual("user", dlg.Sections[0].ScopeText);
        Assert.IsTrue(dlg.Sections[0].Entries.Any(e => e.Key == "model"));
    }

    /// <summary>
    /// The multi-source case. Every other fixture in the suite builds one client, so
    /// a builder that stopped after the first source would look perfectly healthy.
    /// </summary>
    [TestMethod]
    public void Build_TwoSources_ProducesASectionForEachInOrder()
    {
        SaveChangesDialogViewModel? dlg = SaveDialogBuilder.Build(
            [
                (DirtyClient("model", "opus"), "First Product"),
                (DirtyClient("outputStyle", "concise"), "Second Product"),
            ],
            Text);

        Assert.IsNotNull(dlg);
        CollectionAssert.AreEqual(
            new[] { "First Product", "Second Product" },
            dlg!.Sections.Select(s => s.WorkspaceName).ToArray(),
            "Both sources must contribute, in the order they were handed over.");
    }

    [TestMethod]
    public void Build_SkipsASourceWhoseDocumentsHaveNoRealDiff()
    {
        SaveChangesDialogViewModel? dlg = SaveDialogBuilder.Build(
            [
                (MakeClient(), "Clean Product"),
                (DirtyClient(), "Dirty Product"),
            ],
            Text);

        Assert.IsNotNull(dlg);
        CollectionAssert.AreEqual(
            new[] { "Dirty Product" },
            dlg!.Sections.Select(s => s.WorkspaceName).ToArray(),
            "A source with nothing to write must not contribute an empty section.");
    }

    [TestMethod]
    public void Build_RestoreContext_SwitchesModeAndEveryPieceOfWordingWithIt()
    {
        SaveChangesDialogViewModel? dlg = SaveDialogBuilder.Build(
            [(DirtyClient(), "Claude Code")], Text, isRestoreContext: true);

        Assert.IsNotNull(dlg);
        Assert.AreEqual(SaveDialogMode.Restore, dlg!.Mode);
        Assert.AreEqual(Text.WillBeRestoredTo, dlg.ActionVerb);
        Assert.AreEqual(Text.WillBeRestoredTo, dlg.Sections[0].ActionVerb,
            "The section carries the verb too, so it renders without reaching back to the dialog.");
        Assert.AreEqual(Text.RestoreTitle, dlg.WindowTitle);
        Assert.AreEqual(Text.RestoreConfirmButton, dlg.ConfirmButtonLabel);
    }

    [TestMethod]
    public void Build_SaveContext_UsesTheSaveWording()
    {
        SaveChangesDialogViewModel? dlg =
            SaveDialogBuilder.Build([(DirtyClient(), "Claude Code")], Text);

        Assert.IsNotNull(dlg);
        Assert.AreEqual(SaveDialogMode.Save, dlg!.Mode);
        Assert.AreEqual(Text.WillBeWrittenTo, dlg.Sections[0].ActionVerb);
        Assert.AreEqual(Text.SaveTitle, dlg.WindowTitle);
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
    [TestMethod]
    public void Build_GivesEveryEntryTheAccessibleNameForItsOwnKind()
    {
        // A baseline that already has the key makes the edit a Modified rather than
        // an Added, so both mappings are exercised in one pass.
        SettingsDocument seeded = new(
            ConfigScope.User, "user.json",
            (JsonObject)JsonNode.Parse("""{"model":"sonnet"}""")!, isReadOnly: false);
        AgentConfigClientCore modified = ClaudeCodeClient.FromExistingWorkspace(
            new SettingsWorkspace([seeded], ClaudeMergePolicy.Instance),
            ConfigScope.User, schemaRegistry: new SchemaRegistry());
        modified.SetValue("model", "opus");

        SaveChangesDialogViewModel? dlg = SaveDialogBuilder.Build(
            [(DirtyClient("outputStyle", "concise"), "Added"), (modified, "Modified")], Text);

        Assert.IsNotNull(dlg);
        List<SaveChangeEntryViewModel> entries = dlg!.Sections.SelectMany(s => s.Entries).ToList();
        Assert.IsTrue(entries.Count > 0, "Precondition: there is at least one entry.");

        bool sawAdded = false;
        bool sawModified = false;
        foreach (SaveChangeEntryViewModel e in entries)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(e.KindAccessibleName),
                $"Entry '{e.Key}' has no screen-reader name for its change pill.");

            switch (e.Kind)
            {
                case ChangeKind.Added:
                    Assert.AreEqual(Text.KindAdded, e.KindAccessibleName);
                    sawAdded = true;
                    break;
                case ChangeKind.Removed:
                    Assert.AreEqual(Text.KindRemoved, e.KindAccessibleName);
                    break;
                default:
                    Assert.AreEqual(Text.KindModified, e.KindAccessibleName);
                    sawModified = true;
                    break;
            }
        }

        Assert.IsTrue(sawAdded, "Precondition: the fixture produced an added change.");
        Assert.IsTrue(sawModified, "Precondition: the fixture produced a modified change.");
    }

    [TestMethod]
    public void Build_ShortensPathsUnderTheHomeDirectory_AndLeavesOthersAlone()
    {
        string home = PlatformPaths.UserProfile;
        Assert.IsFalse(string.IsNullOrEmpty(home), "Precondition: a home directory is resolvable.");

        AgentConfigClientCore inside = MakeClient(Path.Combine(home, ".claude", "settings.json"));
        inside.SetValue("model", "opus");

        // A path that cannot be under the home directory on any platform.
        AgentConfigClientCore outside = MakeClient(Path.Combine("/etc", "agent", "settings.json"));
        outside.SetValue("model", "opus");

        SaveChangesDialogViewModel? dlg = SaveDialogBuilder.Build(
            [(inside, "Inside"), (outside, "Outside")], Text);

        Assert.IsNotNull(dlg);
        string insidePath = dlg!.Sections.Single(s => s.WorkspaceName == "Inside").FilePath;
        string outsidePath = dlg.Sections.Single(s => s.WorkspaceName == "Outside").FilePath;

        Assert.IsTrue(insidePath.StartsWith('~'),
            $"A path under the home directory is shown with a leading '~'; got '{insidePath}'.");
        Assert.IsFalse(insidePath.Contains('\\'),
            "Separators are normalised to '/' so the display matches the scope-legend table.");
        Assert.IsFalse(outsidePath.StartsWith('~'),
            $"A path outside the user profile is shown verbatim; got '{outsidePath}'.");
    }

    [TestMethod]
    public void Build_TruncatesLongValues_ButKeepsTheFullOneForTheTooltip()
    {
        string longValue = new('x', 200);
        AgentConfigClientCore client = MakeClient();
        client.SetValue("model", longValue);

        SaveChangesDialogViewModel? dlg =
            SaveDialogBuilder.Build([(client, "Claude Code")], Text);

        Assert.IsNotNull(dlg);
        SaveChangeEntryViewModel entry = dlg!.Sections[0].Entries.Single(e => e.Key == "model");

        Assert.IsTrue(entry.NewValue!.EndsWith('…'), "The displayed value is truncated.");
        Assert.IsTrue(entry.NewValue.Length < entry.FullNewValue!.Length,
            "The untruncated value is kept so the hover tooltip can show all of it.");
        StringAssert.Contains(entry.FullNewValue, longValue);
    }
}
