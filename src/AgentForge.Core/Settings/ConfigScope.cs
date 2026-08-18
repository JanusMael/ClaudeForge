namespace Bennewitz.Ninja.AgentForge.Core.Settings;

/// <summary>
/// The scope level at which a setting is defined.
/// Lower <see cref="Ordinal"/> = higher priority (the top rung overrides everything).
/// On the default ladder that order is:
///   Managed (0) &gt; Local (1) &gt; Project (2) &gt; User (3)
/// More-specific scopes win: a project-local personal override beats a shared
/// project default, which in turn beats the user-global baseline.
/// </summary>
/// <remarks>
/// <para>
/// <b>This was an <c>enum</c> until Phase 3, and gained its name / read-only shape in
/// Phase 4f.</b> The four values, their ordinals, their ordering, their
/// <see cref="ToString"/> text, and <c>default(ConfigScope)</c> all still behave exactly as
/// the enum did. What changed is where the *ladder* comes from: a
/// <see cref="ScopeLadder"/> the product supplies, rather than two arrays hardcoded here.
/// </para>
/// <para>
/// <b>Why a single <c>int</c> backing field plus an optional ladder, rather than a record of
/// <c>(Id, Priority, DisplayName, IsReadOnly)</c>:</b> a struct with reference-type members
/// has an all-zero <c>default</c> whose <c>Id</c> is <see langword="null"/>. The codebase has
/// a dozen uninitialised <c>private ConfigScope _lastScope;</c> fields that today start life
/// as <see cref="Managed"/>, plus <c>ConfigScope?</c> properties whose semantics depend on
/// the non-null case being a real scope. Phase 3 built the record shape and measured it:
/// <b>2,791 of 2,792 tests still passed</b> while <c>default(ConfigScope)</c> quietly stopped
/// being <c>Managed</c>. So the ordinal remains the identity, the richer members are
/// *derived*, and <see cref="ScopeLadder.Default"/> is encoded as a <see langword="null"/>
/// field so plain struct equality still gives
/// <c>default(ConfigScope) == ConfigScope.Managed</c>.
/// </para>
/// <para>
/// <b><see cref="Id"/> and <see cref="ToString"/> are load-bearing, not cosmetic.</b>
/// <c>ClaudeScope</c> takes its <c>Id</c> and <c>DisplayName</c> from them, and those flow
/// into AXAML brush and tooltip lookups keyed by name. They must keep returning exactly the
/// old enum member names for the default ladder.
/// </para>
/// </remarks>
public readonly record struct ConfigScope
{
    /// <summary>
    /// The ordinal. Named to mirror the former enum's underlying value: lower wins.
    /// </summary>
    private readonly int _value;

    /// <summary>
    /// The ladder this scope belongs to, or <see langword="null"/> for
    /// <see cref="ScopeLadder.Default"/>.
    /// <para>
    /// <b>The null encoding is the invariant, not an optimisation.</b> It is what keeps
    /// <c>default(ConfigScope)</c> equal to <see cref="Managed"/>, and what keeps the four
    /// statics below equal to the scopes a Claude client hands out — otherwise every one of
    /// the ~1,100 test sites naming <c>ConfigScope.User</c> would compare unequal to the
    /// client's own <c>User</c> and the change would look like a thousand unrelated failures.
    /// <see cref="ScopeLadder.ScopeAt"/> normalises, so no other code has to know.
    /// </para>
    /// </summary>
    private readonly ScopeLadder? _ladder;

    internal ConfigScope(int value, ScopeLadder? ladder)
    {
        _value = value;
        _ladder = ladder;
    }

    /// <summary>The ladder this scope came from.</summary>
    public ScopeLadder Ladder => _ladder ?? ScopeLadder.Default;

    /// <summary>Enterprise/MDM policy. Read-only; cannot be overridden by any other scope.</summary>
    public static ConfigScope Managed => ScopeLadder.Default.ScopeAt(0);

    /// <summary>
    /// Local per-project settings (.claude/settings.local.json).
    /// Gitignored and personal — highest priority among user-editable scopes.
    /// Overrides both Project and User settings for this working tree.
    /// </summary>
    public static ConfigScope Local => ScopeLadder.Default.ScopeAt(1);

    /// <summary>
    /// Project settings (.claude/settings.json).
    /// Committed to git and shared with the team.
    /// Overrides User settings; overridden by Local.
    /// </summary>
    public static ConfigScope Project => ScopeLadder.Default.ScopeAt(2);

    /// <summary>
    /// User-global settings (~/.claude/settings.json).
    /// Applies to all projects; lowest-priority user-editable scope.
    /// Overridden by Project and Local when a project is open.
    /// </summary>
    public static ConfigScope User => ScopeLadder.Default.ScopeAt(3);

    /// <summary>
    /// Every scope on the <see cref="ScopeLadder.Default"/> ladder, in priority order
    /// (highest-priority first), matching the order the former enum's members were declared in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replaces <c>Enum.GetValues&lt;ConfigScope&gt;()</c>, which returned members in
    /// declaration order. Call sites that enumerated scopes depend on that order — the
    /// scope-chiclet legend and the property editor's per-scope rows both render in it.
    /// </para>
    /// <para>
    /// ⚠ <b>This is the default ladder's set, not "every scope in the process".</b> Code that
    /// belongs to one product must enumerate <i>that product's</i>
    /// <see cref="ScopeLadder.All"/> instead, or it silently renders Claude's four rungs for a
    /// product with a different ladder.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ConfigScope> All => ScopeLadder.Default.All;

    /// <summary>
    /// The ordinal, exposed so ordering code need not cast. Lower wins, as before.
    /// </summary>
    public int Ordinal => _value;

    /// <summary>
    /// Stable lower-case machine key — <c>"managed"</c>, <c>"user"</c>. What AXAML brush and
    /// tooltip lookups are keyed by, so it is data rather than presentation.
    /// </summary>
    public string Id => Ladder.RungAt(_value).Name.ToLowerInvariant();

    /// <summary>
    /// Human-readable name — <c>"Managed"</c>, <c>"User"</c>. Identical to
    /// <see cref="ToString"/>; a separate member because callers reading a *name* should not
    /// have to rely on a <c>ToString</c> override staying meaningful.
    /// </summary>
    /// <remarks>
    /// Deliberately <i>not</i> upper-cased. The scope chiclets render in caps, but that is a
    /// presentation choice and it stays in the view adapter (<c>ClaudeScope</c>) rather than
    /// being baked into a Core model.
    /// </remarks>
    public string DisplayName => Ladder.RungAt(_value).Name;

    /// <summary>
    /// <see langword="true"/> when this scope is set by policy and cannot be edited —
    /// <see cref="Managed"/>, on the default ladder.
    /// </summary>
    /// <remarks>
    /// Exists so product-neutral code can express "locked by policy" without naming a
    /// specific scope. <c>LayeredValue.IsManagedLocked</c> and
    /// <c>AgentConfigClientCore.EditableScopes</c> both used to compare against
    /// <see cref="Managed"/> directly, which silently assumed a ladder with exactly one
    /// read-only rung at the top. OpenCode has two.
    /// </remarks>
    public bool IsReadOnly => Ladder.RungAt(_value).IsReadOnly;

    /// <summary>
    /// Preserves <c>(int)scope</c> at the handful of sites that sort by scope
    /// (<c>LayeredValue</c>, <c>SettingsWorkspace</c>, <c>PermissionResolver</c>) and in
    /// <c>ClaudeScope.ToLibraryPriority</c>'s inversion. Explicit rather than implicit so a
    /// scope cannot silently participate in arithmetic.
    /// </summary>
    public static explicit operator int(ConfigScope scope)
    {
        return scope._value;
    }

    /// <summary>
    /// The former enum member name — <c>"Managed"</c>, <c>"Local"</c>, <c>"Project"</c>,
    /// <c>"User"</c> on the default ladder. See the class remarks: this is consumed as data,
    /// not just shown.
    /// </summary>
    public override string ToString()
    {
        return DisplayName;
    }
}
