namespace Bennewitz.Ninja.AgentForge.Core.Settings;

/// <summary>
/// The scope level at which a setting is defined.
/// Lower numeric value = higher priority (Managed overrides everything).
/// Priority order matches Claude Code's documented behaviour:
///   Managed (0) &gt; Local (1) &gt; Project (2) &gt; User (3)
/// More-specific scopes win: a project-local personal override beats a shared
/// project default, which in turn beats the user-global baseline.
/// </summary>
/// <remarks>
/// <para>
/// <b>This was an <c>enum</c> until Phase 3 of the OpenCodeForge plan.</b> It is now a
/// struct so a second product can eventually declare a different, longer scope ladder
/// (OpenCode's is global → custom → project → inline → managed → MDM). This commit is
/// deliberately a <i>shape</i> change only: the four values, their ordinals, their
/// ordering, their <see cref="ToString"/> text, and <c>default(ConfigScope)</c> all
/// behave exactly as the enum did. Threading a per-product scope set through
/// <c>SettingsWorkspace</c> / <c>MergeEngine</c> / <c>LayeredValue</c> is the next commit,
/// and that is where behaviour is allowed to change.
/// </para>
/// <para>
/// <b>Why a single <c>int</c> backing field rather than a record of
/// <c>(Id, Priority, DisplayName, IsReadOnly)</c>:</b> a struct with reference-type
/// members has an all-zero <c>default</c> whose <c>Id</c> is <see langword="null"/>. The
/// codebase has a dozen uninitialised <c>private ConfigScope _lastScope;</c> fields that
/// today start life as <see cref="Managed"/>, plus <c>ConfigScope?</c> properties whose
/// semantics depend on the non-null case being a real scope. Backing the struct with the
/// ordinal keeps <c>default(ConfigScope) == Managed</c>, so none of those sites change
/// meaning silently. Equality is therefore plain integer equality, which also keeps the
/// type safe as a dictionary key and a <c>HashSet</c> member.
/// </para>
/// <para>
/// <b><see cref="ToString"/> is load-bearing, not cosmetic.</b>
/// <c>ClaudeScope</c> derives its <c>Id</c> and <c>DisplayName</c> from it
/// (<c>ToLowerInvariant</c> / <c>ToUpperInvariant</c>), and those flow into AXAML brush
/// and tooltip lookups. It must keep returning exactly the old enum member names.
/// </para>
/// </remarks>
public readonly record struct ConfigScope
{
    /// <summary>
    /// Display names indexed by ordinal. Also the source of <see cref="ToString"/>, so
    /// the strings must stay identical to the former enum member names.
    /// </summary>
    private static readonly string[] _names = ["Managed", "Local", "Project", "User"];

    /// <summary>
    /// The ordinal. Named to mirror the former enum's underlying value: lower wins.
    /// Private so the set stays closed — only the four statics below can exist, which is
    /// what makes record equality (over this one field) equivalent to identity.
    /// </summary>
    private readonly int _value;

    private ConfigScope(int value)
    {
        _value = value;
    }

    /// <summary>Enterprise/MDM policy. Read-only; cannot be overridden by any other scope.</summary>
    public static ConfigScope Managed => new(0);

    /// <summary>
    /// Local per-project settings (.claude/settings.local.json).
    /// Gitignored and personal — highest priority among user-editable scopes.
    /// Overrides both Project and User settings for this working tree.
    /// </summary>
    public static ConfigScope Local => new(1);

    /// <summary>
    /// Project settings (.claude/settings.json).
    /// Committed to git and shared with the team.
    /// Overrides User settings; overridden by Local.
    /// </summary>
    public static ConfigScope Project => new(2);

    /// <summary>
    /// User-global settings (~/.claude/settings.json).
    /// Applies to all projects; lowest-priority user-editable scope.
    /// Overridden by Project and Local when a project is open.
    /// </summary>
    public static ConfigScope User => new(3);

    /// <summary>
    /// Every scope, in priority order (highest-priority first), matching the order the
    /// former enum's members were declared in.
    /// </summary>
    /// <remarks>
    /// Replaces <c>Enum.GetValues&lt;ConfigScope&gt;()</c>, which returned members in
    /// declaration order. Call sites that enumerated scopes depend on that order — the
    /// scope-chiclet legend and the property editor's per-scope rows both render in it.
    /// </remarks>
    public static IReadOnlyList<ConfigScope> All { get; } = [Managed, Local, Project, User];

    /// <summary>
    /// The ordinal, exposed so ordering code need not cast. Lower wins, as before.
    /// </summary>
    public int Ordinal => _value;

    /// <summary>
    /// Preserves <c>(int)scope</c> at the handful of sites that sort by scope
    /// (<c>LayeredValue</c>, <c>SettingsWorkspace</c>, <c>PermissionResolver</c>) and in
    /// <c>ClaudeScope.ToLibraryPriority</c>'s <c>3 - (int)scope</c> inversion. Explicit
    /// rather than implicit so a scope cannot silently participate in arithmetic.
    /// </summary>
    public static explicit operator int(ConfigScope scope)
    {
        return scope._value;
    }

    /// <summary>
    /// The former enum member name — <c>"Managed"</c>, <c>"Local"</c>, <c>"Project"</c>,
    /// <c>"User"</c>. See the class remarks: this is consumed as data, not just shown.
    /// </summary>
    public override string ToString()
    {
        return (uint)_value < (uint)_names.Length ? _names[_value] : _value.ToString();
    }
}
