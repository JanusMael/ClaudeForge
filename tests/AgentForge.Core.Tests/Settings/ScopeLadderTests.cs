using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Settings;

/// <summary>
/// Covers <see cref="ScopeLadder"/> — the product-supplied scope ladder introduced in Phase
/// 4f — and specifically the wrong answers the two arrays it replaced used to give.
/// <para>
/// Until 4f the ladder was <c>["Managed", "Local", "Project", "User"]</c> and
/// <c>[true, false, false, false]</c>, hardcoded inside <see cref="ConfigScope"/> in
/// product-neutral <c>AgentForge.Core</c>. Handing that a longer ladder produced **silence,
/// not errors**: rungs past the fourth reported <see cref="ConfigScope.IsReadOnly"/> as
/// <see langword="false"/>, so policy-locked settings became editable. Every test named
/// <c>SixRungLadder_*</c> below is that failure, pinned.
/// </para>
/// </summary>
public sealed class ScopeLadderTests
{
    /// <summary>
    /// OpenCode's ladder as measured in Spike S1 — six rungs, and crucially <b>two</b>
    /// read-only ones rather than Claude's single top rung.
    /// </summary>
    private static ScopeLadder SixRungLadder() => new(
        "opencode-shaped",
        new ScopeRung("Mdm", IsReadOnly: true),
        new ScopeRung("Managed", IsReadOnly: true),
        new ScopeRung("Inline", IsReadOnly: false),
        new ScopeRung("Project", IsReadOnly: false),
        new ScopeRung("Custom", IsReadOnly: false),
        new ScopeRung("Global", IsReadOnly: false));

    // ── The bug the two hardcoded arrays caused ──────────────────────────

    [Fact]
    public void SixRungLadder_ReportsEveryPolicyRungReadOnly()
    {
        ScopeLadder ladder = SixRungLadder();

        // Ordinals 4 and 5 are past the end of the old four-entry array. Under the previous
        // shape they came back editable, which is a settings editor offering to overwrite a
        // value the product will refuse to change.
        Assert.True(ladder.ScopeAt(0).IsReadOnly, "MDM is policy-controlled.");
        Assert.True(ladder.ScopeAt(1).IsReadOnly, "Managed is policy-controlled.");
        Assert.False(ladder.ScopeAt(2).IsReadOnly);
        Assert.False(ladder.ScopeAt(3).IsReadOnly);
        Assert.False(ladder.ScopeAt(4).IsReadOnly);
        Assert.False(ladder.ScopeAt(5).IsReadOnly);
    }

    [Fact]
    public void SixRungLadder_NamesEveryRung()
    {
        ScopeLadder ladder = SixRungLadder();

        // Under the previous shape ordinals 4 and 5 stringified as "4" and "5", which then
        // became ConfigScopeAdapter.Id and fed the AXAML brush and tooltip lookups keyed by name.
        Assert.Equal(
            new[] { "Mdm", "Managed", "Inline", "Project", "Custom", "Global" },
            ladder.All.Select(s => s.DisplayName).ToArray());
        Assert.Equal(
            new[] { "mdm", "managed", "inline", "project", "custom", "global" },
            ladder.All.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void SixRungLadder_DefaultEditableScope_IsTheLowestEditableRung()
    {
        // Not simply "the last rung", and not Claude's User. This replaces
        // AgentConfigClientCore.EditableScopes' hardcoded [ConfigScope.User] fallback, which
        // named Claude's lowest rung from product-neutral code.
        Assert.Equal("global", SixRungLadder().DefaultEditableScope.Id);
    }

    [Fact]
    public void DefaultEditableScope_FallsBackToTheLastRung_WhenEveryRungIsReadOnly()
    {
        ScopeLadder locked = new(
            "all-policy",
            new ScopeRung("Mdm", IsReadOnly: true),
            new ScopeRung("Managed", IsReadOnly: true));

        // A product whose every rung is policy-controlled is its own statement, not this
        // type's to override — but it must still yield a scope rather than throwing, because
        // the UI asks for one before any file has been discovered.
        Assert.Equal("managed", locked.DefaultEditableScope.Id);
    }

    // ── Identity and equality across ladders ─────────────────────────────

    [Fact]
    public void ScopesFromDifferentLadders_AreNotEqual()
    {
        // Ordinal 3 is "User" on Claude's ladder and "Project" on the six-rung one. If these
        // compared equal, a process hosting both products could use one product's scope as a
        // dictionary key for the other's and silently resolve the wrong layer.
        ConfigScope claudeOrdinal3 = ConfigScope.User;
        ConfigScope otherOrdinal3 = SixRungLadder().ScopeAt(3);

        MessageAssert.Equal(claudeOrdinal3.Ordinal, otherOrdinal3.Ordinal,
            "Precondition: the same ordinal, so only the ladder distinguishes them.");
        Assert.NotEqual(claudeOrdinal3, otherOrdinal3);
        Assert.True(claudeOrdinal3 != otherOrdinal3);
    }

    [Fact]
    public void TwoScopesFromTheSameLadderInstance_AreEqual()
    {
        ScopeLadder ladder = SixRungLadder();

        Assert.Equal(ladder.ScopeAt(2), ladder.ScopeAt(2));
        Assert.Equal(ladder.ScopeAt(2).GetHashCode(), ladder.ScopeAt(2).GetHashCode());

        HashSet<ConfigScope> set = [ladder.ScopeAt(2), ladder.ScopeAt(2)];
        MessageAssert.Equal(1, set.Count, "The same rung twice must collapse to one entry.");
    }

    [Fact]
    public void SeparatelyConstructedIdenticalLadders_AreNotInterchangeable()
    {
        // Documents the consequence of ladder identity being by instance: two structurally
        // identical ladders still produce distinct scopes. This is why ClaudeConfigClientBase
        // returns ScopeLadder.Default rather than constructing its own copy — a product must
        // hold ONE ladder instance and hand it out, not rebuild it per call.
        MessageAssert.NotEqual(SixRungLadder().ScopeAt(0), SixRungLadder().ScopeAt(0),
            "If this ever becomes equal, ladder identity moved to structural equality — "
            + "re-check that ScopeLadder.Default is still distinguishable from a "
            + "hand-built copy of Claude's four rungs.");
    }

    // ── The default ladder, and why null encodes it ──────────────────────

    [Fact]
    public void DefaultLadder_ProducesExactlyTheConfigScopeStatics()
    {
        // The invariant that keeps ~1,100 existing test sites meaningful: a scope obtained
        // from the default ladder must be indistinguishable from the matching static. It is
        // achieved by encoding the default ladder as a null field inside ConfigScope, so
        // plain struct equality also leaves default(ConfigScope) == Managed.
        Assert.Equal(
            new[] { ConfigScope.Managed, ConfigScope.Local, ConfigScope.Project, ConfigScope.User },
            ScopeLadder.Default.All.ToArray());

        Assert.Equal(ConfigScope.User, ScopeLadder.Default.ScopeAt(3));
        MessageAssert.Equal(default, ScopeLadder.Default.ScopeAt(0),
            "default(ConfigScope) must stay Managed — a dozen uninitialised "
            + "`private ConfigScope _lastScope;` fields depend on it, and Phase 3 measured a "
            + "shape that broke it while passing 2,791 of 2,792 tests.");
    }

    [Fact]
    public void DefaultLadder_IsClaudesFourRungs()
    {
        Assert.Equal(4, ScopeLadder.Default.Count);
        MessageAssert.Equal("claude", ScopeLadder.Default.Id,
            "Named for what it is. AgentForge.Core must not grow a Claude vocabulary in its "
            + "API, but pretending the default ladder is product-neutral would be worse — a "
            + "second product is meant to supply its own, not inherit this.");
    }

    [Fact]
    public void EveryScope_KnowsItsOwnLadder()
    {
        ScopeLadder ladder = SixRungLadder();

        Assert.Same(ladder, ladder.ScopeAt(0).Ladder);
        Assert.Same(ScopeLadder.Default, ConfigScope.User.Ladder);
        MessageAssert.Same(ScopeLadder.Default, default(ConfigScope).Ladder,
            "An uninitialised scope must resolve to the default ladder, not throw.");
    }

    // ── Construction guards ──────────────────────────────────────────────

    [Fact]
    public void ScopeAt_RejectsARungTheLadderDoesNotHave()
    {
        ScopeLadder ladder = SixRungLadder();

        Assert.Throws<ArgumentOutOfRangeException>(() => ladder.ScopeAt(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => ladder.ScopeAt(-1));
    }

    [Fact]
    public void ALadderNeedsAtLeastOneRung()
    {
        Assert.Throws<ArgumentException>(() => new ScopeLadder("empty"));
    }

    [Fact]
    public void ALadderNeedsAnId()
    {
        Assert.Throws<ArgumentException>(
            () => new ScopeLadder("  ", new ScopeRung("User", IsReadOnly: false)));
    }

    [Fact]
    public void OutOfRangeName_DegradesToTheOrdinal_RatherThanThrowing()
    {
        // RungAt is reached from ToString(), Id and DisplayName, which are called from
        // logging and from AXAML converters. Throwing there turns a cosmetic mismatch into a
        // crash, so an unknown rung surfaces as a visible, searchable ordinal instead.
        ConfigScope offLadder = new ScopeLadder("one", new ScopeRung("Only", IsReadOnly: false))
            .ScopeAt(0);

        Assert.Equal("Only", offLadder.ToString());
        Assert.Equal("Only", offLadder.DisplayName);
    }
}
