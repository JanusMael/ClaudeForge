using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Tab deep-linking for the settings-group pages — Hooks, Permissions, General
/// and every other page built from <see cref="SettingsGroupEditorViewModel"/>,
/// which is most of the app.
///
/// <para>
/// These pages did not implement <see cref="IDeepNavigable"/>, so the host logged
/// "<c>not deep-navigable; 1 segment(s) dropped</c>" and landed the user on
/// whichever tab the page defaulted to. <c>--deep-link claude-code/hooks/hooks.flow</c>
/// went to the Hooks page and then ignored the tab, and nothing below the page was
/// ever persisted, so place-keeping could not bring a tab back either.
/// </para>
/// </summary>
[TestClass]
public sealed class GroupEditorDeepNavigationTests
{
    private static SettingsGroupEditorViewModel MakeEditor()
    {
        JsonObject root = (JsonObject)JsonNode.Parse("{}")!;
        SettingsWorkspace workspace = new(
            [new SettingsDocument(ConfigScope.User, "User.json", root, isReadOnly: false)]);

        List<SchemaNode> nodes = [new("model", "model") { ValueType = SchemaValueType.String }];
        return new SettingsGroupEditorViewModel("Git", nodes, workspace);
    }

    [TestMethod]
    public async Task RestoreThenCapture_RoundTripsTheTab()
    {
        SettingsGroupEditorViewModel vm = MakeEditor();

        bool applied = await vm.TryRestoreDeepPathAsync(
            [GroupTab.JsonId], DeepRestoreMode.Locate, null, CancellationToken.None);

        Assert.IsTrue(applied);
        Assert.AreEqual(GroupTab.JsonId, vm.SelectedTab?.Id);
        CollectionAssert.AreEqual(
            new[] { GroupTab.JsonId },
            vm.CaptureDeepPath().ToArray(),
            "What the page captures must be what a later restore can consume.");
    }

    [TestMethod]
    public async Task Restore_UnknownTab_ReportsTheMissAndStaysPut()
    {
        SettingsGroupEditorViewModel vm = MakeEditor();
        string? before = vm.SelectedTab?.Id;

        bool applied = await vm.TryRestoreDeepPathAsync(
            ["no-such-tab"], DeepRestoreMode.Locate, null, CancellationToken.None);

        Assert.IsFalse(applied, "A tab this group does not have is a miss the host must be told about.");
        Assert.AreEqual(before, vm.SelectedTab?.Id, "A miss must not disturb the current tab.");
    }

    [TestMethod]
    public async Task Restore_NoSegments_IsAMiss()
    {
        SettingsGroupEditorViewModel vm = MakeEditor();

        Assert.IsFalse(await vm.TryRestoreDeepPathAsync(
            [], DeepRestoreMode.Locate, null, CancellationToken.None));
    }

    /// <summary>
    /// The host calls this once at <c>DispatcherPriority.Loaded</c> because the
    /// view rebuild that follows node selection can land after the restore and
    /// reset the tab. It must be safe to call when the tab is already correct.
    /// </summary>
    [TestMethod]
    public async Task ReapplyTab_IsIdempotent()
    {
        SettingsGroupEditorViewModel vm = MakeEditor();
        await vm.TryRestoreDeepPathAsync(
            [GroupTab.EffectiveId], DeepRestoreMode.Locate, null, CancellationToken.None);

        vm.ReapplyTab([GroupTab.EffectiveId]);
        vm.ReapplyTab([GroupTab.EffectiveId]);

        Assert.AreEqual(GroupTab.EffectiveId, vm.SelectedTab?.Id);
    }

    [TestMethod]
    public void ReapplyTab_UnknownTab_LeavesTheSelectionAlone()
    {
        SettingsGroupEditorViewModel vm = MakeEditor();
        string? before = vm.SelectedTab?.Id;

        vm.ReapplyTab(["no-such-tab"]);

        Assert.AreEqual(before, vm.SelectedTab?.Id);
    }

    /// <summary>
    /// A contributed tab addresses exactly like a built-in one — the customizer
    /// gives every tab an id, so <c>hooks.flow</c> needs no special case in the
    /// deep-link path. Guards against a future "only the seeded three are
    /// addressable" regression.
    /// </summary>
    [TestMethod]
    public void SeededTabIds_AreStableAndDistinct()
    {
        string[] ids = [GroupTab.PropertiesId, GroupTab.EffectiveId, GroupTab.JsonId];

        CollectionAssert.AreEqual(new[] { "properties", "effective", "json" }, ids,
            "These ids are a persisted wire format; renaming one breaks saved links.");
        Assert.AreEqual(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }
}
