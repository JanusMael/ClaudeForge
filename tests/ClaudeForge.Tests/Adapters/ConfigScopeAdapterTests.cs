using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.ScopedEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Adapters;

/// <summary>
/// Guards the mapping between <see cref="ConfigScope"/> and its
/// <see cref="IEditorScope"/> wrapper.
/// <para>
/// This is the runtime check the old invariant explicitly did not have.
/// <c>ConfigScopeAdapter._cache</c> used to be an array indexed by <c>(int)scope</c>, and the
/// root <c>AGENTS.md</c> said in as many words that a mismatch "produces the wrong
/// wrapper silently" with no check to catch it — <c>For(ConfigScope.User)</c> would hand
/// back Project's priority and read-only flag, and permission checks would then pass
/// against the wrong scope. Phase 3 replaced the array with a dictionary keyed by scope,
/// which makes the mis-mapping unrepresentable; these tests make it also untestable-as-
/// broken, so the guarantee survives the next refactor of this class.
/// </para>
/// </summary>
public sealed class ConfigScopeAdapterTests
{
    /// <summary>
    /// Each test starts with no other ladder's scopes wrapped. Several tests below wrap an
    /// <c>other-product</c> ladder that also has a <c>Project</c> rung, and the adapter's cache is
    /// process-wide, so without this a foreign <c>"project"</c> resolves to whichever ladder was
    /// wrapped first — an order dependence xUnit exposed (see
    /// <see cref="ConfigScopeAdapter.ForgetNonDefaultLaddersForTesting"/>).
    /// </summary>
    public ConfigScopeAdapterTests() => ConfigScopeAdapter.ForgetNonDefaultLaddersForTesting();

    /// <summary>
    /// The mapping is exercised for <b>every</b> scope rather than a sampled one, because
    /// the failure mode being guarded is an off-by-one that leaves most entries correct.
    /// </summary>
    [Fact]
    public void For_ReturnsTheWrapperForTheScopeItWasAsked()
    {
        foreach (ConfigScope scope in ConfigScope.All)
        {
            MessageAssert.Equal(scope, ConfigScopeAdapter.For(scope).Source,
                $"ConfigScopeAdapter.For({scope}) returned a wrapper for a different scope.");
        }
    }

    /// <summary>Wrappers are cached, so reference equality (<c>AreSame</c>) is meaningful.</summary>
    [Fact]
    public void For_ReturnsTheSameInstanceEveryTime()
    {
        foreach (ConfigScope scope in ConfigScope.All)
        {
            Assert.Same(ConfigScopeAdapter.For(scope), ConfigScopeAdapter.For(scope));
        }
    }

    /// <summary>
    /// The library's convention is the inverse of Core's: higher <c>Priority</c> wins.
    /// Asserted as a whole-ladder inversion rather than four literals, so the formula
    /// stays correct if the ladder ever grows — which is the reason it now derives from
    /// <c>ConfigScope.All.Count</c> instead of a hardcoded 3.
    /// </summary>
    [Fact]
    public void ToLibraryPriority_InvertsTheLadder()
    {
        int last = ConfigScope.All.Count;
        foreach (ConfigScope scope in ConfigScope.All)
        {
            int priority = ConfigScopeAdapter.ToLibraryPriority(scope);
            Assert.True(priority < last,
                "Priority must decrease as the scope's ordinal increases.");
            last = priority;
        }

        MessageAssert.Equal(0, ConfigScopeAdapter.ToLibraryPriority(ConfigScope.User),
            "The lowest-priority scope must map to 0.");
        MessageAssert.Equal(ConfigScope.All.Count - 1, ConfigScopeAdapter.ToLibraryPriority(ConfigScope.Managed),
            "The highest-priority scope must map to the top of the range.");
    }

    /// <summary>
    /// <c>IsReadOnly</c> now comes from the scope itself rather than a
    /// <c>== ConfigScope.Managed</c> comparison in this class; the wrapper must agree with
    /// its source or the editors will offer to edit a policy-locked value.
    /// </summary>
    [Fact]
    public void IsReadOnly_AgreesWithTheUnderlyingScope()
    {
        foreach (ConfigScope scope in ConfigScope.All)
        {
            MessageAssert.Equal(scope.IsReadOnly, ConfigScopeAdapter.For(scope).IsReadOnly, $"scope: {scope}");
        }
    }

    /// <summary>
    /// The id-based fallback exists for test doubles that implement
    /// <see cref="IEditorScope"/> without being a <see cref="ConfigScopeAdapter"/>. It now
    /// resolves against <see cref="ConfigScope.All"/> instead of a hand-written list of
    /// four ids, so it cannot drift out of step with the ladder.
    /// </summary>
    [Fact]
    public void ToConfigScope_ResolvesRealWrappersAndForeignScopesAlike()
    {
        foreach (ConfigScope scope in ConfigScope.All)
        {
            Assert.Equal(scope, ConfigScopeAdapter.ToConfigScope(ConfigScopeAdapter.For(scope)));
            Assert.Equal(scope, ConfigScopeAdapter.ToConfigScope(new ForeignScope(scope.ToString().ToLowerInvariant())));
        }

        Assert.Throws<ArgumentException>(
            () => ConfigScopeAdapter.ToConfigScope(new ForeignScope("not-a-scope")));
    }

    /// <summary>A non-<see cref="ConfigScopeAdapter"/> implementation, as a test fake would supply.</summary>
    private sealed class ForeignScope(string id) : IEditorScope
    {
        public string Id { get; } = id;

        public int Priority => 0;

        public string DisplayName => Id;

        public bool IsReadOnly => false;
    }

    // ── any ladder, not just the default one ─────────────────────────────────
    //
    // ⚠⚠ These exist because the second app could not render a single settings page.
    // ConfigScopeAdapter was renamed from ClaudeScope in Phase 8b-1 and moved into the neutral
    // shell — but its cache was pre-built from ConfigScope.All, which IS the default ladder, and
    // its priority formula counted ConfigScope.All.Count. Renaming a type does not neutralise it.
    // The whole suite stayed green because every test used the default ladder.

    /// <summary>A five-rung ladder, deliberately unlike the default four.</summary>
    private static ScopeLadder OtherProductLadder() => new(
        "other-product",
        new ScopeRung("Managed", IsReadOnly: true),
        new ScopeRung("Inline", IsReadOnly: true),
        new ScopeRung("Project", IsReadOnly: false),
        new ScopeRung("Custom", IsReadOnly: false),
        new ScopeRung("Global", IsReadOnly: false));

    [Fact]
    public void For_WrapsAScopeFromANonDefaultLadder()
    {
        ScopeLadder ladder = OtherProductLadder();

        foreach (ConfigScope scope in ladder.All)
        {
            ConfigScopeAdapter wrapper = ConfigScopeAdapter.For(scope);
            MessageAssert.Equal(scope, wrapper.Source,
                $"'{scope.DisplayName}' from a non-default ladder must be wrappable. Throwing "
                + "here means no product but the first can render a settings page at all.");
        }
    }

    [Fact]
    public void Priority_InvertsWithinTheScopesOwnLadder()
    {
        ScopeLadder ladder = OtherProductLadder();

        // Five rungs: highest-priority (ordinal 0) becomes 4, lowest (ordinal 4) becomes 0.
        Assert.Equal(4, ConfigScopeAdapter.ToLibraryPriority(ladder.ScopeAt(0)));
        MessageAssert.Equal(0, ConfigScopeAdapter.ToLibraryPriority(ladder.ScopeAt(4)),
            "Counting the DEFAULT ladder's rungs instead of this scope's own gives -1 here, "
            + "which inverts precedence for the whole product with no error anywhere.");

        // And the default ladder is unaffected — this fix must not move Claude's values.
        Assert.Equal(3, ConfigScopeAdapter.ToLibraryPriority(ConfigScope.Managed));
        Assert.Equal(0, ConfigScopeAdapter.ToLibraryPriority(ConfigScope.User));
    }

    [Fact]
    public void For_ReturnsTheSameInstanceForTheSameScope_OnAnyLadder()
    {
        ConfigScope other = OtherProductLadder().ScopeAt(2);

        MessageAssert.Same(ConfigScopeAdapter.For(other), ConfigScopeAdapter.For(other),
            "The library compares scopes by reference through AreSame, so a second call must "
            + "return the same wrapper or scope comparisons silently start failing.");
        Assert.Same(ConfigScopeAdapter.For(ConfigScope.User), ConfigScopeAdapter.For(ConfigScope.User));
    }

    [Fact]
    public void TwoLaddersWithTheSameRungName_DoNotCollide()
    {
        ConfigScope otherProject = OtherProductLadder().ScopeAt(2);   // "Project", ordinal 2

        MessageAssert.NotEqual(ConfigScope.Project, otherProject,
            "Precondition: same name and ordinal, different ladder — these must not be equal.");
        MessageAssert.NotSame(
            ConfigScopeAdapter.For(ConfigScope.Project),
            ConfigScopeAdapter.For(otherProject),
            "Two products' scopes that share a rung name must get distinct wrappers, or editing "
            + "one product's Project scope would resolve to the other's.");
    }
}
