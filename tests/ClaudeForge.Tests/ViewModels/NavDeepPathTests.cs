using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.ScopedEditors.ViewModels;

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
public sealed class NavDeepPathTests
{
    // ── Slug ─────────────────────────────────────────────────────────────

    [Fact]
    public void Slug_CollapsesNonAlphanumericRuns()
    {
        // "Agents & Skills" must produce exactly the NavIdAgentsSkills constant.
        Assert.Equal("agents-skills", NavDeepPath.Slug("Agents & Skills"));
        Assert.Equal("mcp-servers", NavDeepPath.Slug("MCP Servers"));
        Assert.Equal("backup-restore", NavDeepPath.Slug("Backup / Restore"));
        Assert.Equal("general", NavDeepPath.Slug("General"));
    }

    [Fact]
    public void Slug_TrimsLeadingAndTrailingSeparators()
    {
        Assert.Equal("hooks", NavDeepPath.Slug("  Hooks!  "));
        Assert.Equal("hooks", NavDeepPath.Slug("---Hooks---"));
    }

    [Fact]
    public void Slug_NonAsciiBecomesSeparator_SoIdsStayTypeable()
    {
        // Deliberate: char.IsLetterOrDigit would keep these and produce an id
        // nobody can type on a command line.
        Assert.Equal("a-b", NavDeepPath.Slug("AéB"));
        Assert.Equal(string.Empty, NavDeepPath.Slug("エージェント"));
    }

    [Fact]
    public void Slug_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, NavDeepPath.Slug(null));
        Assert.Equal(string.Empty, NavDeepPath.Slug("   "));
    }

    // ── TryParse ─────────────────────────────────────────────────────────

    [Fact]
    public void TryParse_SingleSegment_Succeeds()
    {
        Assert.True(NavDeepPath.TryParse("agents-skills", out IReadOnlyList<string> segs, out string? err));
        Assert.Null(err);
        Assert.Equal(new[] { "agents-skills" }, segs.ToArray());
    }

    [Fact]
    public void TryParse_MaxSegments_Succeeds()
    {
        Assert.True(NavDeepPath.TryParse(
            "claude-code/permissions/properties/some-item", out IReadOnlyList<string> segs, out string? err));
        Assert.Null(err);
        Assert.Equal(4, segs.Count);
    }

    [Fact]
    public void TryParse_TooManySegments_Fails()
    {
        Assert.False(NavDeepPath.TryParse("a/b/c/d/e", out _, out string? err));
        Assert.NotNull(err);
    }

    [Fact]
    public void TryParse_EmptyOrWhitespace_Fails()
    {
        Assert.False(NavDeepPath.TryParse(null, out _, out _));
        Assert.False(NavDeepPath.TryParse(string.Empty, out _, out _));
        Assert.False(NavDeepPath.TryParse("   ", out _, out _));
    }

    [Fact]
    public void TryParse_LeadingOrTrailingSeparator_Fails()
    {
        Assert.False(NavDeepPath.TryParse("/agents-skills", out _, out _));
        Assert.False(NavDeepPath.TryParse("agents-skills/", out _, out _));
    }

    [Fact]
    public void TryParse_EmptyInteriorSegment_Fails()
    {
        Assert.False(NavDeepPath.TryParse("agents-skills//pdf", out _, out string? err));
        Assert.NotNull(err);
    }

    [Fact]
    public void TryParse_ControlCharacter_Fails()
    {
        // Built from a char code rather than an escape inside a literal so the
        // control byte cannot be silently normalised away by an editor or tool.
        string withControl = "agents-skills/sk" + (char)7 + "ills";

        Assert.False(NavDeepPath.TryParse(withControl, out _, out string? err));
        Assert.NotNull(err);

        // Sanity check that this test exercises the control-character branch and
        // not some other rejection: the same path without it is valid.
        Assert.True(NavDeepPath.TryParse("agents-skills/skills", out _, out _));
    }

    [Fact]
    public void TryParse_ItemKeyWithSpacesAndDots_Succeeds()
    {
        // Artifact names are file / directory names — spaces and dots are normal
        // and must not be rejected by the shape check.
        Assert.True(NavDeepPath.TryParse(
            "agents-skills/skills/my skill.v2@user", out IReadOnlyList<string> segs, out _));
        Assert.Equal("my skill.v2@user", segs[2]);
    }

    [Fact]
    public void Format_RoundTripsTryParse()
    {
        const string path = "claude-code/permissions/properties";
        Assert.True(NavDeepPath.TryParse(path, out IReadOnlyList<string> segs, out _));
        Assert.Equal(path, NavDeepPath.Format(segs));
    }

    // ── Item keys ────────────────────────────────────────────────────────

    [Fact]
    public void SplitItemKey_WithSource_SplitsOnLastAt()
    {
        (string name, string? source) = NavDeepPath.SplitItemKey("pdf@user");
        Assert.Equal("pdf", name);
        Assert.Equal("user", source);

        // Split on the LAST '@' so a name containing '@' still resolves.
        (string name2, string? source2) = NavDeepPath.SplitItemKey("a@b@plugin");
        Assert.Equal("a@b", name2);
        Assert.Equal("plugin", source2);
    }

    [Fact]
    public void SplitItemKey_WithoutSource_ReturnsNullSource()
    {
        (string name, string? source) = NavDeepPath.SplitItemKey("pdf");
        Assert.Equal("pdf", name);
        Assert.Null(source);
    }

    [Fact]
    public void SplitItemKey_EdgeAtPositions_TreatedAsPartOfName()
    {
        // Leading '@' is part of the name; trailing '@' is not an empty source.
        Assert.Equal("@pdf", NavDeepPath.SplitItemKey("@pdf").Name);
        Assert.Null(NavDeepPath.SplitItemKey("@pdf").Source);
        Assert.Equal("pdf@", NavDeepPath.SplitItemKey("pdf@").Name);
        Assert.Null(NavDeepPath.SplitItemKey("pdf@").Source);
    }

    [Fact]
    public void FormatItemKey_RoundTripsSplitItemKey()
    {
        string key = NavDeepPath.FormatItemKey("pdf", "user");
        Assert.Equal("pdf@user", key);
        Assert.Equal(("pdf", "user"), NavDeepPath.SplitItemKey(key));

        string bare = NavDeepPath.FormatItemKey("pdf", null);
        Assert.Equal("pdf", bare);
        Assert.Equal(("pdf", (string?)null), NavDeepPath.SplitItemKey(bare));
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

    [Fact]
    public void Resolve_TopLevelOnly_ResolvesWithNoRemainder()
    {
        NavDeepPathResolution r = NavDeepPath.Resolve(["agents-skills"], BuildTree());

        Assert.True(r.Resolved);
        Assert.Equal("agents-skills", r.Node!.NodeId);
        Assert.Empty(r.RemainingSegments);
        Assert.Null(r.TabId);
        Assert.Null(r.ItemKey);
    }

    [Fact]
    public void Resolve_ParentChildPath_ConsumesChildAsNode()
    {
        // THE ambiguity case: "permissions" is a CHILD NODE, not a tab of the
        // claude-code header. Left-to-right resolution is what settles it.
        NavDeepPathResolution r = NavDeepPath.Resolve(["claude-code", "permissions"], BuildTree());

        Assert.True(r.Resolved);
        Assert.Equal("permissions", r.Node!.NodeId);
        Assert.Equal("Permissions", r.Node.Title);
        MessageAssert.Equal(0, r.RemainingSegments.Count, "The child must be consumed as the node, not left as a tab.");
    }

    [Fact]
    public void Resolve_ParentChildTab_LeavesTabAsRemainder()
    {
        NavDeepPathResolution r =
            NavDeepPath.Resolve(["claude-code", "permissions", "properties"], BuildTree());

        Assert.True(r.Resolved);
        Assert.Equal("permissions", r.Node!.NodeId);
        Assert.Equal("properties", r.TabId);
        Assert.Null(r.ItemKey);
    }

    [Fact]
    public void Resolve_TabAndItemUnderChildlessNode()
    {
        NavDeepPathResolution r = NavDeepPath.Resolve(["agents-skills", "skills", "pdf@user"], BuildTree());

        Assert.True(r.Resolved);
        Assert.Equal("agents-skills", r.Node!.NodeId);
        Assert.Equal("skills", r.TabId);
        Assert.Equal("pdf@user", r.ItemKey);
    }

    [Fact]
    public void Resolve_SameChildIdUnderDifferentParents_DisambiguatedByPath()
    {
        List<NavigationNodeViewModel> tree = BuildTree();

        NavDeepPathResolution code = NavDeepPath.Resolve(["claude-code", "version-info"], tree);
        NavDeepPathResolution desktop = NavDeepPath.Resolve(["claude-desktop", "version-info"], tree);

        Assert.True(code.Resolved);
        Assert.True(desktop.Resolved);
        MessageAssert.NotSame(code.Node, desktop.Node,
            "version-info exists under both products; the parent segment must disambiguate.");
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        NavDeepPathResolution r = NavDeepPath.Resolve(["CLAUDE-CODE", "Permissions"], BuildTree());

        Assert.True(r.Resolved);
        Assert.Equal("permissions", r.Node!.NodeId);
    }

    [Fact]
    public void Resolve_UnknownTopLevel_Unresolved()
    {
        NavDeepPathResolution r = NavDeepPath.Resolve(["no-such-page"], BuildTree());

        Assert.False(r.Resolved);
        Assert.Null(r.Node);
    }

    [Fact]
    public void Resolve_UnknownChild_FallsBackToParentAndKeepsSegment()
    {
        // A stale shortcut must land on the right page rather than fail outright,
        // so the unknown segment survives as a best-effort tab id.
        NavDeepPathResolution r = NavDeepPath.Resolve(["claude-code", "no-such-child"], BuildTree());

        Assert.True(r.Resolved);
        Assert.Equal("claude-code", r.Node!.NodeId);
        Assert.Equal("no-such-child", r.TabId);
    }

    [Fact]
    public void Resolve_EmptySegments_Unresolved()
    {
        Assert.False(NavDeepPath.Resolve([], BuildTree()).Resolved);
    }

    [Fact]
    public void Resolve_NeverMatchesADivider()
    {
        // Dividers carry no NodeId; an empty / whitespace segment must not match
        // one by accident.
        List<NavigationNodeViewModel> tree = BuildTree();
        Assert.False(NavDeepPath.Resolve(["─────"], tree).Resolved);
        Assert.False(NavDeepPath.Resolve([string.Empty], tree).Resolved);
    }
}
