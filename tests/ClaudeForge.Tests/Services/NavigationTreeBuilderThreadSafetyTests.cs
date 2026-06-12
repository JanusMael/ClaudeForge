using System.Reflection;
using Avalonia.Headless;
using Bennewitz.Ninja.ClaudeForge.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Services;

[TestClass]
public sealed class NavigationTreeBuilderThreadSafetyTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    /// <summary>
    /// Regression test for the startup offload in
    /// <c>MainWindowViewModel.BuildNavigationTreeAsync</c>: the entire settings-editor set is
    /// constructed inside a <c>Task.Run</c> worker so the just-painted window stays responsive.
    /// This builds the FULL real editor set (every group plus its child editors — including the
    /// bespoke Permissions / MCP / Hooks / Plugins / Environment editors) on a worker thread,
    /// exactly as startup does, and asserts it completes and produces the expected editors. It
    /// backstops the offload PATH: if a future change makes an editor constructor fail when run
    /// off the UI thread (throws, deadlocks, or depends on construction order), this fails.
    /// <para>
    /// SCOPE NOTE: Avalonia headless does NOT enforce dispatcher thread-affinity, so this test
    /// cannot catch a ctor that merely calls <c>Dispatcher.UIThread.VerifyAccess()</c> or creates
    /// a control off-thread — that class of regression surfaces as a crash in the REAL app at
    /// startup, not here. The "editor constructors do no synchronous UI work" invariant is held
    /// by the upfront constructor audit and code review; this test guards the off-thread build
    /// itself (and verifies BuildGroups stays a pure, thread-agnostic factory).
    /// </para>
    /// </summary>
    [TestMethod]
    public Task BuildGroups_ConstructsAllEditors_OffTheUiThread() => Session.Dispatch(async () =>
    {
        SchemaRegistry registry = new();
        // var: GetClaudeCodeSettingsNodeAsync returns Json.Schema.JsonSchemaNode, and importing
        // that namespace would make SchemaRegistry ambiguous (Json.Schema also defines one).
        var root = await registry.GetClaudeCodeSettingsNodeAsync();
        IReadOnlyList<SchemaNode> nodes = SchemaTreeBuilder.BuildTopLevel(root);
        SettingsWorkspace workspace = new(
            [new SettingsDocument(ConfigScope.User, "User.json", new JsonObject(), isReadOnly: false)]);

        // Construct every group plus its child property editors on a worker thread, exactly as
        // MainWindowViewModel.BuildNavigationTreeAsync does on startup.
        IReadOnlyList<NavigationGroup> groups =
            await Task.Run(() => NavigationTreeBuilder.BuildGroups(nodes, workspace));

        Assert.IsTrue(groups.Count > 0,
            "The real Claude Code schema should bucket into navigation groups.");
        Assert.IsTrue(groups.All(g => g.Editor is not null),
            "Every group must carry a constructed editor view-model.");
        Assert.IsTrue(groups.Sum(g => g.Editor.Editors.Count) > 0,
            "The off-thread build must construct the leaf property editors too.");
    }, CancellationToken.None);
}
