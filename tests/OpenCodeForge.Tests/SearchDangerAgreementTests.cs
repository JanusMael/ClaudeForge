using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Search;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Adapters;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// A search hit and the settings row it navigates to must say the same thing about the same knob —
/// asserted through the real schema, the real factory, the real danger table and the real search
/// pass, not a stub.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>Two surfaces classifying independently is how they come to disagree.</b> Search holds
/// no editing scope and no value, so a classifier called from the search pass would have to guess
/// both — and severity is a function of all three. <c>provider.*.options.apiKey</c> is Caution in
/// a user-global file and Critical in a project file; <c>IsDangerNow</c> is meaningless without
/// the value. A user who searches "apiKey", sees amber, clicks through and lands on red has been
/// told two different things by one product.
/// </para>
/// <para>
/// The production code closes that by construction: the hit asks the editor
/// (<see cref="IDangerAnnotatedEditor"/>) and gets back the row's own assessment. This test is
/// what proves the wiring still goes through the editor rather than around it — a future change
/// that gave search its own classifier would keep every other test green.
/// </para>
/// </remarks>
[TestClass]
public sealed class SearchDangerAgreementTests
{
    /// <summary>
    /// A value that OpenCode's real table calls Critical AND unsafe-right-now, so the assertions
    /// below are not comparing two Unremarkables and calling it agreement.
    /// </summary>
    private const string UnsafeSharePath = "share";

    /// <summary>
    /// A policy that escalates one real schema key by scope.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Why not OpenCode's own escalating key?</b> Its only one is
    /// <c>provider.*.options.apiKey</c> — a wildcard segment under an open-ended provider
    /// dictionary, so no concrete schema node exists for it and it can never surface as a search
    /// hit. That gap is real and recorded; it makes the shipped escalation unobservable on every
    /// surface, which is why <c>ScopeEscalationRealScopeTests</c> asserts it against the
    /// classifier directly. Here the mechanism under test is whether the HIT follows the page's
    /// scope, so the policy is supplied rather than borrowed.
    /// </remarks>
    private sealed class EscalatingPolicy : IDangerClassifier
    {
        public IReadOnlyCollection<string> ClassifiedPaths => [UnsafeSharePath];

        public DangerAssessment Classify(string path, IEditorScope? scope, object? currentValue)
        {
            if (!string.Equals(path, UnsafeSharePath, StringComparison.Ordinal))
            {
                return DangerAssessment.Unremarkable;
            }

            bool committed = string.Equals(scope?.Id, "project", StringComparison.OrdinalIgnoreCase);
            return new DangerAssessment(
                committed ? AppSeverity.Critical : AppSeverity.Caution,
                currentValue is not null,
                committed ? "published to everyone with repo access" : "a local setting");
        }
    }

    private static Task<(SearchViewModel Search, SettingsGroupEditorViewModel Page)>
        BuildRealSearchOverConfigAsync(ConfigScope scope, JsonObject held) =>
        BuildRealSearchAsync(scope, OpenCodeDangerTable.Config, held);

    private static Task<(SearchViewModel Search, SettingsGroupEditorViewModel Page)>
        BuildRealSearchAsync(ConfigScope scope, IDangerClassifier danger) =>
        BuildRealSearchAsync(scope, danger, new JsonObject { ["share"] = "auto" });

    private static async Task<(SearchViewModel Search, SettingsGroupEditorViewModel Page)>
        BuildRealSearchAsync(ConfigScope scope, IDangerClassifier danger, JsonObject held)
    {
        SchemaRegistry registry = new();
        var root = await registry
            .GetSettingsNodeAsync(OpenCodeProducts.Config, CancellationToken.None)
            .ConfigureAwait(false);
        IReadOnlyList<SchemaNode> nodes = SchemaTreeBuilder.BuildTopLevel(root);
        Assert.IsTrue(nodes.Count > 0, "no top-level nodes read from the bundled config schema");

        SettingsWorkspace workspace = new(
            [new SettingsDocument(scope, "opencode.json", held, isReadOnly: false)],
            OpenCodeMergePolicy.Instance);

        // The real factory carrying a real policy — the same pairing MainWindowViewModel makes.
        SettingsGroupEditorViewModel page = new(
            "Sharing",
            nodes,
            workspace,
            new OpenCodeEditorFactory(danger),
            OpenCodeSettingsGroupText.Create(),
            initialScope: scope);

        NavigationNodeViewModel child = new("Sharing") { Editor = page };
        NavigationNodeViewModel header = new("OpenCode");
        header.Children.Add(child);
        ObservableCollection<NavigationNodeViewModel> tree = [header];

        return (new SearchViewModel(() => tree, () => false), page);
    }

    [TestMethod]
    public async Task AHitAndItsSettingsRowReportTheSameAssessmentInstance()
    {
        (SearchViewModel search, SettingsGroupEditorViewModel page) =
            await BuildRealSearchOverConfigAsync(
                ConfigScope.User,
                new JsonObject { ["share"] = "auto" });

        search.ExecuteSearch(UnsafeSharePath);

        SearchResultViewModel hit = search.SearchResults.Single(r => r.PropertyKey == UnsafeSharePath);
        PropertyEditorViewModel row = page.Editors.Single(e => e.Path == UnsafeSharePath);

        // Guard the premise first: an agreement test over two Unremarkables proves nothing.
        Assert.AreEqual(AppSeverity.Critical, row.Danger.Severity,
            "Premise broken: OpenCode's table must call 'share' Critical, or this test is vacuous. "
            + "If the policy changed deliberately, pick another Critical+unsafe key.");
        Assert.IsTrue(row.Danger.IsDangerNow,
            "Premise broken: share='auto' must read as unsafe-right-now.");

        Assert.AreEqual(row.Danger, hit.Danger,
            "The hit must carry the row's own assessment — same severity, same IsDangerNow, same "
            + "sentence. A difference here means search classified the knob itself.");
        Assert.AreEqual(row.DangerAccessibleText, hit.DangerAccessibleText,
            "Both surfaces must announce the same thing to a screen reader.");
        Assert.AreEqual(row.HasDangerSeverity, hit.HasDangerSeverity);
    }

    /// <summary>
    /// The same key, the same query, a different writing scope — and the hit must move with the
    /// row rather than staying on whatever the first scope said.
    /// </summary>
    /// <remarks>
    /// ⭐ This is the assertion that a search-side classifier could not satisfy without
    /// reimplementing the page's scope state, and the one that would have caught the natural
    /// shortcut of passing <see langword="null"/> for the scope.
    /// </remarks>
    [TestMethod]
    public async Task AScopeEscalatedKeyEscalatesOnTheHitToo()
    {
        (SearchViewModel userSearch, SettingsGroupEditorViewModel userPage) =
            await BuildRealSearchAsync(ConfigScope.User, new EscalatingPolicy());
        (SearchViewModel projectSearch, SettingsGroupEditorViewModel projectPage) =
            await BuildRealSearchAsync(ConfigScope.Project, new EscalatingPolicy());

        userSearch.ExecuteSearch(UnsafeSharePath);
        projectSearch.ExecuteSearch(UnsafeSharePath);

        DangerAssessment atUser = userPage.AssessDanger(UnsafeSharePath)!;
        DangerAssessment atProject = projectPage.AssessDanger(UnsafeSharePath)!;

        // Premise: the two pages really do disagree, or the comparison below is vacuous.
        Assert.AreEqual(AppSeverity.Caution, atUser.Severity);
        Assert.AreEqual(AppSeverity.Critical, atProject.Severity);

        Assert.AreEqual(atUser,
            userSearch.SearchResults.Single(r => r.PropertyKey == UnsafeSharePath).Danger,
            "At user scope the hit must match the user-scope row.");
        Assert.AreEqual(atProject,
            projectSearch.SearchResults.Single(r => r.PropertyKey == UnsafeSharePath).Danger,
            "At project scope the hit must match the project-scope row. A hit whose severity were "
            + "computed anywhere but the editor could not do this — search has no editing scope.");
    }

    /// <summary>
    /// The delegation itself: a group editor answers for the paths its editors render, and returns
    /// null — not <see cref="DangerAssessment.Unremarkable"/> — for anything else.
    /// </summary>
    [TestMethod]
    public async Task TheGroupEditorOwnsItsOwnPathsAndDisownsTheRest()
    {
        (_, SettingsGroupEditorViewModel page) =
            await BuildRealSearchOverConfigAsync(ConfigScope.User, []);

        string owned = page.Editors[0].Path;

        Assert.IsNotNull(page.AssessDanger(owned),
            $"The page renders '{owned}' and must answer for it.");
        Assert.IsNull(page.AssessDanger("no.such.key.anywhere"),
            "An unrendered path must come back null. Unremarkable would tell a caller walking "
            + "several pages that it had found the owner, and every later page would go unasked.");
    }
}
