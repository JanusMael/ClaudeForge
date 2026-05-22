using Bennewitz.Ninja.ClaudeForge.ViewModels;
using PropertyEditorViewModel = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels.PropertyEditorViewModel;
// the App-bridge StringPropertyEditorViewModel was deleted;
// reference the library leaf via alias.
using StringEditor = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels.StringPropertyEditorViewModel;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// regression test for the Save-time deadlock surfaced during manual testing.
/// </summary>
/// <remarks>
/// <para>
/// Repro from the failing call stack:
/// </para>
/// <code>
/// ApplyToWorkspace
///   -&gt; WriteEditorValue
///     -&gt; SDK.SetValue                       (acquires _stateLock)
///       -&gt; workspace.SetValue
///         -&gt; workspace.Changed (sync, lock still held)
///           -&gt; SettingsGroupEditorViewModel.OnWorkspaceChanged
///             -&gt; RebuildEditors
///               -&gt; (compound editor).LoadFromLayered
///                 -&gt; (SDK accessor).GetXAt
///                   -&gt; SDK.GetScopeValue   (waits on _stateLock — DEADLOCK)
/// </code>
/// <para>
/// The fix: ApplyToWorkspace now sets <c>_selfWriting</c> for the duration of
/// its bulk-save loop, mirroring what OnEditorPropertyChanged does for the
/// single-editor live-write path. The flag short-circuits OnWorkspaceChanged
/// before the rebuild can fire SDK accessor reads under the held lock.
/// </para>
/// <para>
/// This test asserts the correct outcome by checking editor instance identity:
/// without the fix, RebuildEditors fires during the write and replaces the
/// editor list; with the fix, the original instances are preserved (no rebuild).
/// </para>
/// </remarks>
[TestClass]
public sealed class ApplyToWorkspaceDeadlockRegressionTests
{
    private static SettingsWorkspace MakeWorkspace(params (ConfigScope Scope, string Json)[] entries)
    {
        IEnumerable<SettingsDocument> docs = entries.Select(e =>
        {
            JsonObject root = (JsonObject)JsonNode.Parse(e.Json)!;
            return new SettingsDocument(e.Scope, $"{e.Scope}.json", root, isReadOnly: false);
        });
        return new SettingsWorkspace(docs);
    }

    private static SchemaNode MakeNode(string jsonPath, string name,
                                       SchemaValueType type = SchemaValueType.String)
    {
        return new SchemaNode(jsonPath, name) { ValueType = type };
    }

    [TestMethod]
    public void ApplyToWorkspace_SetsSelfWritingFlag_PreventingRebuildDuringWrite()
    {
        // Setup: a workspace with one editor, modified by the user.
        // ApplyToWorkspace must complete the write WITHOUT triggering an
        // intermediate RebuildEditors call — that would replace `editorBefore`
        // with a fresh instance.
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace);

        // Capture the editor instance BEFORE the write.
        PropertyEditorViewModel editorBefore = vm.Editors[0];
        ((StringEditor)editorBefore).Value = "opus";

        vm.ApplyToWorkspace();

        // The editor list must still contain the SAME instance — no rebuild
        // happened mid-write. Without the _selfWriting guard around the loop,
        // the synchronous workspace.Changed would call OnWorkspaceChanged ->
        // RebuildEditors -> editor list replaced.
        Assert.AreEqual(1, vm.Editors.Count);
        Assert.AreSame(editorBefore, vm.Editors[0],
            "ApplyToWorkspace must not trigger RebuildEditors mid-loop. " +
            "If editor identity changes, _selfWriting was not set during the bulk-save " +
            "(2026-04-29 deadlock fix regression).");

        // And the value did make it through.
        LayeredValue layered = workspace.GetLayeredValue("model");
        Assert.AreEqual("opus", layered.EffectiveValue!.GetValue<string>());
    }

    [TestMethod]
    public async Task ApplyToWorkspace_CompletesPromptly_EvenWithMultipleModifiedEditors()
    {
        // Bound the call by a generous timeout so a regressed lock pattern
        // would surface as a test failure (Wait timed out) rather than hang
        // the whole CI run.
        List<SchemaNode> nodes =
        [
            MakeNode("a", "a"),
            MakeNode("b", "b"),
            MakeNode("c", "c"),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace);

        foreach (StringEditor? editor in vm.Editors.Cast<StringEditor>())
        {
            editor.Value = "x-" + editor.Path;
        }

        bool done = await Task.Run(() =>
        {
            vm.ApplyToWorkspace();
            return true;
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(done);
        Assert.AreEqual("x-a", workspace.GetLayeredValue("a").EffectiveValue!.GetValue<string>());
        Assert.AreEqual("x-b", workspace.GetLayeredValue("b").EffectiveValue!.GetValue<string>());
        Assert.AreEqual("x-c", workspace.GetLayeredValue("c").EffectiveValue!.GetValue<string>());
    }
}