using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Settings;

/// <summary>
/// Pins the properties <see cref="ConfigScope"/> carried as an <c>enum</c> and must keep
/// carrying now that it is a struct (Phase 3 of the OpenCodeForge plan).
/// <para>
/// Every assertion here exists because the corresponding failure is <b>silent</b>. The
/// compiler catches the loud half of an enum-to-struct conversion — constant patterns,
/// <c>Enum.GetValues</c>, defaulted parameters — and the full suite was green before any
/// of these tests existed, which is precisely why they are needed: nothing else notices
/// if <c>default</c> stops meaning Managed or <c>ToString</c> stops saying "User".
/// </para>
/// <para>
/// When the next commit threads a per-product scope set through the workspace, these are
/// the tests that should be <b>updated deliberately</b> rather than deleted — a change
/// here means Claude's own scope ladder moved, which is a behavioural change and not a
/// refactor.
/// </para>
/// </summary>
public class ConfigScopeTests
{
    /// <summary>
    /// The ordinals are the merge precedence: lower wins. <c>LayeredValue</c>,
    /// <c>SettingsWorkspace</c> and <c>PermissionResolver</c> all sort by
    /// <c>(int)scope</c>, and <c>ConfigScopeAdapter.ToLibraryPriority</c> inverts it with
    /// <c>3 - (int)scope</c>. Change a number here and settings silently resolve to the
    /// wrong file.
    /// </summary>
    [Fact]
    public void Ordinals_AreUnchangedFromTheEnum()
    {
        Assert.Equal(0, (int)ConfigScope.Managed);
        Assert.Equal(1, (int)ConfigScope.Local);
        Assert.Equal(2, (int)ConfigScope.Project);
        Assert.Equal(3, (int)ConfigScope.User);
    }

    /// <summary>
    /// The single most dangerous property of the conversion. A dozen view-models declare
    /// <c>private ConfigScope _lastScope;</c> with no initialiser and rely on it starting
    /// as Managed. A struct backed by anything other than the ordinal would give them an
    /// all-zero value whose identity is nothing at all, and no test would notice.
    /// </summary>
    [Fact]
    public void Default_IsManaged_SoUninitialisedFieldsKeepTheirOldMeaning()
    {
        Assert.Equal(ConfigScope.Managed, default(ConfigScope));
        Assert.Equal(0, (int)default(ConfigScope));
    }

    /// <summary>
    /// <c>ToString</c> is consumed as data, not shown as prose: <c>ConfigScopeAdapter</c> builds
    /// its <c>Id</c> from <c>ToLowerInvariant()</c> and its <c>DisplayName</c> from
    /// <c>ToUpperInvariant()</c>, and three AXAML converters resolve brushes, tooltips and
    /// labels from it. A record struct's compiler-generated <c>ToString</c> would emit
    /// <c>ConfigScope { … }</c> and break all of them without a single compile error.
    /// </summary>
    [Fact]
    public void ToString_ReturnsTheFormerEnumMemberNames()
    {
        Assert.Equal("Managed", ConfigScope.Managed.ToString());
        Assert.Equal("Local", ConfigScope.Local.ToString());
        Assert.Equal("Project", ConfigScope.Project.ToString());
        Assert.Equal("User", ConfigScope.User.ToString());
    }

    /// <summary>
    /// <c>All</c> replaced <c>Enum.GetValues&lt;ConfigScope&gt;()</c>, which enumerated in
    /// declaration order. The scope legend and the property editor's per-scope rows render
    /// in that order, so it is visible behaviour rather than an implementation detail.
    /// </summary>
    [Fact]
    public void All_IsEveryScopeInPriorityOrder()
    {
        Assert.Equal(
            new[] { ConfigScope.Managed, ConfigScope.Local, ConfigScope.Project, ConfigScope.User },
            ConfigScope.All.ToArray());
    }

    /// <summary>
    /// Two independently obtained values of the same scope must be equal and hash alike.
    /// Scopes are used as dictionary keys and set members throughout the editors; a struct
    /// whose equality covered a display string would still compile and would still mostly
    /// work, failing only where two instances were built differently.
    /// </summary>
    [Fact]
    public void Equality_IsByValue_SoScopesWorkAsDictionaryKeys()
    {
        ConfigScope a = ConfigScope.Project;
        ConfigScope b = ConfigScope.Project;

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(ConfigScope.Project, ConfigScope.Local);

        Dictionary<ConfigScope, string> byScope = new()
        {
            [ConfigScope.User] = "user",
            [ConfigScope.Managed] = "managed",
        };
        Assert.Equal("user", byScope[ConfigScope.User]);
        Assert.Equal(2, byScope.Count);
        Assert.False(byScope.ContainsKey(ConfigScope.Project));

        HashSet<ConfigScope> set = [ConfigScope.Local, ConfigScope.Local];
        MessageAssert.Equal(1, set.Count, "The same scope added twice must collapse to one entry.");
    }

    /// <summary>
    /// Sorting by ordinal is how merge precedence is applied. Asserted end-to-end rather
    /// than per-value so a reordering of <see cref="ConfigScope.All"/> cannot pass by
    /// agreeing with itself.
    /// </summary>
    [Fact]
    public void SortingByOrdinal_PutsHighestPriorityFirst()
    {
        ConfigScope[] shuffled =
            [ConfigScope.User, ConfigScope.Project, ConfigScope.Managed, ConfigScope.Local];

        Assert.Equal(
            new[] { ConfigScope.Managed, ConfigScope.Local, ConfigScope.Project, ConfigScope.User },
            shuffled.OrderBy(s => (int)s).ToArray());
    }

    /// <summary>
    /// <c>IsReadOnly</c> is what product-neutral code asks instead of comparing against
    /// <see cref="ConfigScope.Managed"/>. Claude has exactly one policy rung; the point of
    /// the property is that a second product need not have exactly one, so the assertion
    /// is written as "the read-only scopes are precisely these" rather than "Managed is
    /// read-only".
    /// </summary>
    [Fact]
    public void IsReadOnly_MarksPreciselyThePolicyScopes()
    {
        MessageAssert.SequenceEqual(
            new[] { ConfigScope.Managed },
            ConfigScope.All.Where(s => s.IsReadOnly).ToArray(),
            "Managed is Claude's only policy-controlled scope.");

        Assert.False(ConfigScope.User.IsReadOnly);
        Assert.False(ConfigScope.Project.IsReadOnly);
        Assert.False(ConfigScope.Local.IsReadOnly);
    }

    /// <summary>
    /// <see cref="ConfigScope.Id"/> is the stable machine key the scope-chiclet brush and
    /// tooltip converters are keyed by, and <c>ConfigScopeAdapter.Id</c> now takes it directly
    /// instead of lower-casing <see cref="ConfigScope.ToString"/> itself. Pinned to the
    /// literal strings rather than derived from <see cref="ConfigScope.DisplayName"/>, so a
    /// change to the casing rule cannot pass by agreeing with itself.
    /// </summary>
    [Fact]
    public void Id_IsTheLowerCasedMemberName_WhichAxamlLookupsKeyOn()
    {
        Assert.Equal(
            new[] { "managed", "local", "project", "user" },
            ConfigScope.All.Select(s => s.Id).ToArray());
    }

    /// <summary>
    /// Phase 4f added <see cref="ConfigScope.DisplayName"/> alongside
    /// <see cref="ConfigScope.ToString"/>. They must agree — <c>ToString</c> is documented as
    /// consumed-as-data, and two spellings of the same fact drifting apart is how the load
    /// order in step 1h came to be stated backwards in four places.
    /// </summary>
    [Fact]
    public void DisplayName_AgreesWithToString_AndIsNotUpperCased()
    {
        foreach (ConfigScope scope in ConfigScope.All)
        {
            Assert.Equal(scope.ToString(), scope.DisplayName);
        }

        MessageAssert.Equal("User", ConfigScope.User.DisplayName,
            "Not \"USER\". The chiclets render in caps, but that upper-casing belongs to "
            + "ConfigScopeAdapter — baking presentation into a Core model is what this separation "
            + "exists to avoid.");
    }

    /// <summary>
    /// <c>MergeResult.EffectiveScope</c> and <c>LayeredValue.EffectiveScope</c> are
    /// <c>ConfigScope?</c>, where null means "no scope defines this". Managed is ordinal
    /// zero, so a nullable wrapper that confused the two would silently attribute every
    /// undefined setting to enterprise policy.
    /// </summary>
    [Fact]
    public void Nullable_DistinguishesNoScopeFromManaged()
    {
        ConfigScope? none = null;
        ConfigScope? managed = ConfigScope.Managed;

        Assert.False(none.HasValue);
        Assert.True(managed.HasValue);
        Assert.NotEqual(none, managed);
        Assert.Equal(ConfigScope.Managed, managed!.Value);
    }
}
