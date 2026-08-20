using System.Reflection;
using Avalonia.Headless;
using Bennewitz.Ninja.ClaudeForge.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Services;

/// <summary>
/// Guards the startup offload path in <c>MainWindowViewModel.BuildNavigationTreeAsync</c>.
/// </summary>
/// <remarks>
/// ⚠⚠ <b>Both tests here were INERT and could not fail.</b> They used
/// <c>Session.Dispatch(async () =&gt; { … })</c>, which binds
/// <c>Dispatch&lt;T&gt;(Func&lt;T&gt;, …)</c> with <c>T = Task</c> and returns
/// <c>Task&lt;Task&gt;</c>: the framework awaits only the outer task, so every assertion in the
/// lambda ran unobserved and its failure was swallowed. Found by canarying — an
/// <c>Assert.Fail</c> as the first statement left both tests green.
/// <para>
/// The fix is the shape <c>SampleHeadlessTests</c> documents: <b>return a value from the
/// lambda</b> so it binds <c>Dispatch&lt;T&gt;(Func&lt;Task&lt;T&gt;&gt;, …)</c>, and assert
/// outside. Keep them that way, and canary any new test added here — a headless test that
/// cannot fail is worse than a missing one, because it reads as coverage.
/// </para>
/// </remarks>
[TestClass]
public sealed class NavigationTreeBuilderThreadSafetyTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    private static async Task<IReadOnlyList<SchemaNode>> RealClaudeCodeNodesAsync()
    {
        SchemaRegistry registry = new();
        // var: GetClaudeCodeSettingsNodeAsync returns Json.Schema.JsonSchemaNode, and importing
        // that namespace would make SchemaRegistry ambiguous (Json.Schema also defines one).
        var root = await registry.GetClaudeCodeSettingsNodeAsync();
        return SchemaTreeBuilder.BuildTopLevel(root);
    }

    private static SettingsWorkspace OneUserDocument() => new(
        [new SettingsDocument(ConfigScope.User, "User.json", new JsonObject(), isReadOnly: false)],
        ClaudeMergePolicy.Instance);

    /// <summary>
    /// The entire settings-editor set is constructed inside a <c>Task.Run</c> worker so the
    /// just-painted window stays responsive. This builds the FULL real editor set (every group
    /// plus its child editors — including the bespoke Permissions / MCP / Hooks / Plugins /
    /// Environment editors) on a worker thread, exactly as startup does, and asserts it
    /// completes and produces the expected editors. It backstops the offload PATH: if a future
    /// change makes an editor constructor fail when run off the UI thread (throws, deadlocks, or
    /// depends on construction order), this fails.
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
    public async Task BuildGroups_ConstructsAllEditors_OffTheUiThread()
    {
        (int Groups, bool EveryGroupHasEditor, int LeafEditors) built =
            await Session.Dispatch(async () =>
            {
                IReadOnlyList<SchemaNode> nodes = await RealClaudeCodeNodesAsync();
                SettingsWorkspace workspace = OneUserDocument();

                // Construct every group plus its child property editors on a worker thread,
                // exactly as MainWindowViewModel.BuildNavigationTreeAsync does on startup.
                IReadOnlyList<NavigationGroup> groups =
                    await Task.Run(() => NavigationTreeBuilder.BuildGroups(nodes, workspace));

                return (groups.Count,
                        groups.All(g => g.Editor is not null),
                        groups.Sum(g => g.Editor.Editors.Count));
            }, CancellationToken.None);

        Assert.IsTrue(built.Groups > 0,
            "The real Claude Code schema should bucket into navigation groups.");
        Assert.IsTrue(built.EveryGroupHasEditor,
            "Every group must carry a constructed editor view-model.");
        Assert.IsTrue(built.LeafEditors > 0,
            "The off-thread build must construct the leaf property editors too.");
    }

    /// <summary>
    /// The production path must wire the product's <i>specialised</i> editors, not merely some
    /// editor for every node.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PropertyEditorFactoryTests</c> already proves the factory can produce these. Nothing
    /// proved that the path which builds real pages <i>uses</i> such a factory — so the factory
    /// was covered and its use was not.
    /// </para>
    /// <para>
    /// This is the guard for <c>ISchemaEditorFactory</c> being a required constructor argument.
    /// A settings page handed a generic factory still renders: every complex node falls back to
    /// a raw-JSON editor. Nothing throws, and the user reports that the permissions page is
    /// broken rather than that a registration went missing.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task BuildGroups_WiresTheSpecialisedEditors_NotJustGenericFallbacks()
    {
        List<string> editorTypes = await Session.Dispatch(async () =>
        {
            IReadOnlyList<SchemaNode> nodes = await RealClaudeCodeNodesAsync();
            IReadOnlyList<NavigationGroup> groups =
                NavigationTreeBuilder.BuildGroups(nodes, OneUserDocument());

            return groups.SelectMany(g => g.Editor.Editors)
                         .Select(e => e.GetType().Name)
                         .ToList();
        }, CancellationToken.None);

        foreach (string specialised in
                 new[] { "PermissionsEditorViewModel", "HooksEditorViewModel", "McpServerListEditorViewModel" })
        {
            Assert.IsTrue(
                editorTypes.Contains(specialised),
                $"The real schema built through the production path produced no {specialised}. "
                + "The factory that page composition passes is no longer registering this "
                + "product's specialised editors, so its complex settings are rendering as raw "
                + "JSON.\nEditors built: "
                + string.Join(", ", editorTypes.Distinct().Order(StringComparer.Ordinal)));
        }
    }
}
