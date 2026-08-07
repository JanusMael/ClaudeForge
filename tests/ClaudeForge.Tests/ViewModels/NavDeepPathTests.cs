using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Grammar + resolution contract for <see cref="NavDeepPath"/> — the
/// culture-invariant addressing scheme shared by the <c>--deep-link</c>
/// argument and the persisted <c>WindowState.LastDeepPath</c>.
/// <para>
/// The load-bearing case is <see cref="Resolve_ParentChildPath_ConsumesChildAsNode"/>:
/// <c>claude-code/permissions</c> must read as parent-node / child-node, NOT as
/// node + tab. Left-to-right resolution is what makes the grammar unambiguous,
/// so a regression there silently redirects every two-segment deep link.
/// </para>
/// </summary>
[TestClass]
public sealed class NavDeepPathTests
{
    // ── Slug ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void Slug_CollapsesNonAlphanumericRuns()
    {
        // "Agents & Skills" must produce exactly the NavIdAgentsSkills constant.
        Assert.AreEqual("agents-skills", NavDeepPath.Slug("Agents & Skills"));
        Assert.AreEqual("mcp-servers", NavDeepPath.Slug("MCP Servers"));
        Assert.AreEqual("backup-restore", NavDeepPath.Slug("Backup / Restore"));
        Assert.AreEqual("general", NavDeepPath.Slug("General"));
    }

    [TestMethod]
    public void Slug_TrimsLeadingAndTrailingSeparators()
    {
        Assert.AreEqual("hooks", NavDeepPath.Slug("  Hooks!  "));
        Assert.AreEqual("hooks", NavDeepPath.Slug("---Hooks---"));
    }

    [TestMethod]
    public void Slug_NonAsciiBecomesSeparator_SoIdsStayTypeable()
    {
        // Deliberate: char.IsLetterOrDigit would keep these and produce an id
        // nobody can type on a command line.
        Assert.AreEqual("a-b", NavDeepPath.Slug("AéB"));
        Assert.AreEqual(string.Empty, NavDeepPath.Slug("エージェント"));
    }

    [TestMethod]
    public void Slug_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, NavDeepPath.Slug(null));
        Assert.AreEqual(string.Empty, NavDeepPath.Slug("   "));
    }

    // ── TryParse ─────────────────────────────────────────────────────────

    [TestMethod]
    public void TryParse_SingleSegment_Succeeds()
    {
        Assert.IsTrue(NavDeepPath.TryParse("agents-skills", out IReadOnlyList<string> segs, out string? err));
        Assert.IsNull(err);
        CollectionAssert.AreEqual(new[] { "agents-skills" }, segs.ToArray());
    }

    [TestMethod]
    public void TryParse_MaxSegments_Succeeds()
    {
        Assert.IsTrue(NavDeepPath.TryParse(
            "claude-code/permissions/properties/some-item", out IReadOnlyList<string> segs, out string? err));
        Assert.IsNull(err);
        Assert.AreEqual(4, segs.Count);
    }

    [TestMethod]
    public void TryParse_TooManySegments_Fails()
    {
        Assert.IsFalse(NavDeepPath.TryParse("a/b/c/d/e", out _, out string? err));
        Assert.IsNotNull(err);
    }

    [TestMethod]
    public void TryParse_EmptyOrWhitespace_Fails()
    {
        Assert.IsFalse(NavDeepPath.TryParse(null, out _, out _));
        Assert.IsFalse(NavDeepPath.TryParse(string.Empty, out _, out _));
        Assert.IsFalse(NavDeepPath.TryParse("   ", out _, out _));
    }

    [TestMethod]
    public void TryParse_LeadingOrTrailingSeparator_Fails()
    {
        Assert.IsFalse(NavDeepPath.TryParse("/agents-skills", out _, out _));
        Assert.IsFalse(NavDeepPath.TryParse("agents-skills/", out _, out _));
    }

    [TestMethod]
    public void TryParse_EmptyInteriorSegment_Fails()
    {
        Assert.IsFalse(NavDeepPath.TryParse("agents-skills//pdf", out _, out string? err));
        Assert.IsNotNull(err);
    }

    [TestMethod]
    public void TryParse_ControlCharacter_Fails()
    {
        // Built from a char code rather than an escape inside a literal so the
        // control byte cannot be silently normalised away by an editor or tool.
        string withControl = "agents-skills/sk" + (char)7 + "ills";

        Assert.IsFalse(NavDeepPath.TryParse(withControl, out _, out string? err));
        Assert.IsNotNull(err);

        // Sanity check that this test exercises the control-character branch and
        // not some other rejection: the same path without it is valid.
        Assert.IsTrue(NavDeepPath.TryParse("agents-skills/skills", out _, out _));
    }

    [TestMethod]
    public void TryParse_ItemKeyWithSpacesAndDots_Succeeds()
    {
        // Artifact names are file / directory names — spaces and dots are normal
        // and must not be rejected by the shape check.
        Assert.IsTrue(NavDeepPath.TryParse(
            "agents-skills/skills/my skill.v2@user", out IReadOnlyList<string> segs, out _));
        Assert.AreEqual("my skill.v2@user", segs[2]);
    }

    [TestMethod]
    public void Format_RoundTripsTryParse()
    {
        const string path = "claude-code/permissions/properties";
        Assert.IsTrue(NavDeepPath.TryParse(path, out IReadOnlyList<string> segs, out _));
        Assert.AreEqual(path, NavDeepPath.Format(segs));
    }

    // ── Item keys ────────────────────────────────────────────────────────

    [TestMethod]
    public void SplitItemKey_WithSource_SplitsOnLastAt()
    {
        (string name, string? source) = NavDeepPath.SplitItemKey("pdf@user");
        Assert.AreEqual("pdf", name);
        Assert.AreEqual("user", source);

        // Split on the LAST '@' so a name containing '@' still resolves.
        (string name2, string? source2) = NavDeepPath.SplitItemKey("a@b@plugin");
        Assert.AreEqual("a@b", name2);
        Assert.AreEqual("plugin", source2);
    }

    [TestMethod]
    public void SplitItemKey_WithoutSource_ReturnsNullSource()
    {
        (string name, string? source) = NavDeepPath.SplitItemKey("pdf");
        Assert.AreEqual("pdf", name);
        Assert.IsNull(source);
    }

    [TestMethod]
    public void SplitItemKey_EdgeAtPositions_TreatedAsPartOfName()
    {
        // Leading '@' is part of the name; trailing '@' is not an empty source.
        Assert.AreEqual("@pdf", NavDeepPath.SplitItemKey("@pdf").Name);
        Assert.IsNull(NavDeepPath.SplitItemKey("@pdf").Source);
        Assert.AreEqual("pdf@", NavDeepPath.SplitItemKey("pdf@").Name);
        Assert.IsNull(NavDeepPath.SplitItemKey("pdf@").Source);
    }

    [TestMethod]
    public void FormatItemKey_RoundTripsSplitItemKey()
    {
        string key = NavDeepPath.FormatItemKey("pdf", "user");
        Assert.AreEqual("pdf@user", key);
        Assert.AreEqual(("pdf", "user"), NavDeepPath.SplitItemKey(key));

        string bare = NavDeepPath.FormatItemKey("pdf", null);
        Assert.AreEqual("pdf", bare);
        Assert.AreEqual(("pdf", (string?)null), NavDeepPath.SplitItemKey(bare));
    }

    // ── Resolve ──────────────────────────────────────────────────────────

    private static List<NavigationNodeViewModel> BuildTree()
    {
        NavigationNodeViewModel cc = new("Claude Code") { NodeId = "claude-code", IsTopLevel = true };
        cc.Children.Add(new NavigationNodeViewModel("Permissions") { NodeId = "permissions" });
        cc.Children.Add(new NavigationNodeViewModel("Version Information") { NodeId = "version-info" });

        NavigationNodeViewModel dt = new("Claude Desktop") { NodeId = "claude-desktop", IsTopLevel = true };
        // Same child id as under Claude Code — ids are unique per parent only.
        dt.Children.Add(new NavigationNodeViewModel("Version Information") { NodeId = "version-info" });

        return
        [
            new NavigationNodeViewModel("─────") { IsDivider = true, IsTopLevel = true },
            cc,
            dt,
            new NavigationNodeViewModel("Agents & Skills") { NodeId = "agents-skills", IsTopLevel = true },
        ];
    }

    [TestMethod]
    public void Resolve_TopLevelOnly_ResolvesWithNoRemainder()
    {
        NavDeepPathResolution r = NavDeepPath.Resolve(["agents-skills"], BuildTree());

        Assert.IsTrue(r.Resolved);
        Assert.AreEqual("agents-skills", r.Node!.NodeId);
        Assert.AreEqual(0, r.RemainingSegments.Count);
        Assert.IsNull(r.TabId);
        Assert.IsNull(r.ItemKey);
    }

    [TestMethod]
    public void Resolve_ParentChildPath_ConsumesChildAsNode()
    {
        // THE ambiguity case: "permissions" is a CHILD NODE, not a tab of the
        // claude-code header. Left-to-right resolution is what settles it.
        NavDeepPathResolution r = NavDeepPath.Resolve(["claude-code", "permissions"], BuildTree());

        Assert.IsTrue(r.Resolved);
        Assert.AreEqual("permissions", r.Node!.NodeId);
        Assert.AreEqual("Permissions", r.Node.Title);
        Assert.AreEqual(0, r.RemainingSegments.Count, "The child must be consumed as the node, not left as a tab.");
    }

    [TestMethod]
    public void Resolve_ParentChildTab_LeavesTabAsRemainder()
    {
        NavDeepPathResolution r =
            NavDeepPath.Resolve(["claude-code", "permissions", "properties"], BuildTree());

        Assert.IsTrue(r.Resolved);
        Assert.AreEqual("permissions", r.Node!.NodeId);
        Assert.AreEqual("properties", r.TabId);
        Assert.IsNull(r.ItemKey);
    }

    [TestMethod]
    public void Resolve_TabAndItemUnderChildlessNode()
    {
        NavDeepPathResolution r = NavDeepPath.Resolve(["agents-skills", "skills", "pdf@user"], BuildTree());

        Assert.IsTrue(r.Resolved);
        Assert.AreEqual("agents-skills", r.Node!.NodeId);
        Assert.AreEqual("skills", r.TabId);
        Assert.AreEqual("pdf@user", r.ItemKey);
    }

    [TestMethod]
    public void Resolve_SameChildIdUnderDifferentParents_DisambiguatedByPath()
    {
        List<NavigationNodeViewModel> tree = BuildTree();

        NavDeepPathResolution code = NavDeepPath.Resolve(["claude-code", "version-info"], tree);
        NavDeepPathResolution desktop = NavDeepPath.Resolve(["claude-desktop", "version-info"], tree);

        Assert.IsTrue(code.Resolved);
        Assert.IsTrue(desktop.Resolved);
        Assert.AreNotSame(code.Node, desktop.Node,
            "version-info exists under both products; the parent segment must disambiguate.");
    }

    [TestMethod]
    public void Resolve_IsCaseInsensitive()
    {
        NavDeepPathResolution r = NavDeepPath.Resolve(["CLAUDE-CODE", "Permissions"], BuildTree());

        Assert.IsTrue(r.Resolved);
        Assert.AreEqual("permissions", r.Node!.NodeId);
    }

    [TestMethod]
    public void Resolve_UnknownTopLevel_Unresolved()
    {
        NavDeepPathResolution r = NavDeepPath.Resolve(["no-such-page"], BuildTree());

        Assert.IsFalse(r.Resolved);
        Assert.IsNull(r.Node);
    }

    [TestMethod]
    public void Resolve_UnknownChild_FallsBackToParentAndKeepsSegment()
    {
        // A stale shortcut must land on the right page rather than fail outright,
        // so the unknown segment survives as a best-effort tab id.
        NavDeepPathResolution r = NavDeepPath.Resolve(["claude-code", "no-such-child"], BuildTree());

        Assert.IsTrue(r.Resolved);
        Assert.AreEqual("claude-code", r.Node!.NodeId);
        Assert.AreEqual("no-such-child", r.TabId);
    }

    [TestMethod]
    public void Resolve_EmptySegments_Unresolved()
    {
        Assert.IsFalse(NavDeepPath.Resolve([], BuildTree()).Resolved);
    }

    [TestMethod]
    public void Resolve_NeverMatchesADivider()
    {
        // Dividers carry no NodeId; an empty / whitespace segment must not match
        // one by accident.
        List<NavigationNodeViewModel> tree = BuildTree();
        Assert.IsFalse(NavDeepPath.Resolve(["─────"], tree).Resolved);
        Assert.IsFalse(NavDeepPath.Resolve([string.Empty], tree).Resolved);
    }
}
