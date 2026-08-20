using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

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
[TestClass]
public sealed class ConfigScopeAdapterTests
{
    /// <summary>
    /// The mapping is exercised for <b>every</b> scope rather than a sampled one, because
    /// the failure mode being guarded is an off-by-one that leaves most entries correct.
    /// </summary>
    [TestMethod]
    public void For_ReturnsTheWrapperForTheScopeItWasAsked()
    {
        foreach (ConfigScope scope in ConfigScope.All)
        {
            Assert.AreEqual(scope, ConfigScopeAdapter.For(scope).Source,
                $"ConfigScopeAdapter.For({scope}) returned a wrapper for a different scope.");
        }
    }

    /// <summary>Wrappers are cached, so reference equality (<c>AreSame</c>) is meaningful.</summary>
    [TestMethod]
    public void For_ReturnsTheSameInstanceEveryTime()
    {
        foreach (ConfigScope scope in ConfigScope.All)
        {
            Assert.AreSame(ConfigScopeAdapter.For(scope), ConfigScopeAdapter.For(scope));
        }
    }

    /// <summary>
    /// The library's convention is the inverse of Core's: higher <c>Priority</c> wins.
    /// Asserted as a whole-ladder inversion rather than four literals, so the formula
    /// stays correct if the ladder ever grows — which is the reason it now derives from
    /// <c>ConfigScope.All.Count</c> instead of a hardcoded 3.
    /// </summary>
    [TestMethod]
    public void ToLibraryPriority_InvertsTheLadder()
    {
        int last = ConfigScope.All.Count;
        foreach (ConfigScope scope in ConfigScope.All)
        {
            int priority = ConfigScopeAdapter.ToLibraryPriority(scope);
            Assert.IsTrue(priority < last,
                "Priority must decrease as the scope's ordinal increases.");
            last = priority;
        }

        Assert.AreEqual(0, ConfigScopeAdapter.ToLibraryPriority(ConfigScope.User),
            "The lowest-priority scope must map to 0.");
        Assert.AreEqual(ConfigScope.All.Count - 1, ConfigScopeAdapter.ToLibraryPriority(ConfigScope.Managed),
            "The highest-priority scope must map to the top of the range.");
    }

    /// <summary>
    /// <c>IsReadOnly</c> now comes from the scope itself rather than a
    /// <c>== ConfigScope.Managed</c> comparison in this class; the wrapper must agree with
    /// its source or the editors will offer to edit a policy-locked value.
    /// </summary>
    [TestMethod]
    public void IsReadOnly_AgreesWithTheUnderlyingScope()
    {
        foreach (ConfigScope scope in ConfigScope.All)
        {
            Assert.AreEqual(scope.IsReadOnly, ConfigScopeAdapter.For(scope).IsReadOnly, $"scope: {scope}");
        }
    }

    /// <summary>
    /// The id-based fallback exists for test doubles that implement
    /// <see cref="IEditorScope"/> without being a <see cref="ConfigScopeAdapter"/>. It now
    /// resolves against <see cref="ConfigScope.All"/> instead of a hand-written list of
    /// four ids, so it cannot drift out of step with the ladder.
    /// </summary>
    [TestMethod]
    public void ToConfigScope_ResolvesRealWrappersAndForeignScopesAlike()
    {
        foreach (ConfigScope scope in ConfigScope.All)
        {
            Assert.AreEqual(scope, ConfigScopeAdapter.ToConfigScope(ConfigScopeAdapter.For(scope)));
            Assert.AreEqual(scope, ConfigScopeAdapter.ToConfigScope(new ForeignScope(scope.ToString().ToLowerInvariant())));
        }

        Assert.ThrowsExactly<ArgumentException>(
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
}
