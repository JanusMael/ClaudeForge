namespace Bennewitz.Ninja.AgentForge.Core.Settings;

/// <summary>
/// One rung of a <see cref="ScopeLadder"/>: what a scope is called and whether it is set by
/// policy. Declared separately from <see cref="ConfigScope"/> because a rung is *data a
/// product states*, while a <see cref="ConfigScope"/> is a *value passed around* — the rung
/// is what a product writes down, the scope is what code compares and stores.
/// </summary>
/// <param name="Name">
/// Canonical human-readable name — <c>"Managed"</c>, <c>"Project"</c>. Returned verbatim by
/// <see cref="ConfigScope.ToString"/> and lower-cased for <see cref="ConfigScope.Id"/>, so
/// it is <b>consumed as data</b>, not merely displayed. See <see cref="ConfigScope"/>.
/// </param>
/// <param name="IsReadOnly">
/// Whether this rung is policy-controlled and cannot be edited. A ladder may have more than
/// one — OpenCode has two (managed and macOS MDM), which is the whole reason this is a
/// per-rung fact rather than "the top rung wins".
/// </param>
public readonly record struct ScopeRung(string Name, bool IsReadOnly);

/// <summary>
/// A product's scope ladder: the ordered set of layers its configuration can be defined at,
/// highest-priority first.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists.</b> Until Phase 4f the ladder was two private arrays inside
/// <see cref="ConfigScope"/> — <c>["Managed", "Local", "Project", "User"]</c> and
/// <c>[true, false, false, false]</c> — which meant product-neutral
/// <c>AgentForge.Core</c> hardcoded <i>Claude's</i> ladder. Handing that type a longer
/// ladder produced two silent wrong answers, not an error: rungs past the fourth reported
/// <see cref="ConfigScope.IsReadOnly"/> as <see langword="false"/>, so
/// <b>policy-locked settings became editable</b>, and their name came back as the bare
/// ordinal, breaking the scope-chiclet brush and tooltip lookups that key on
/// <see cref="ConfigScope.Id"/>.
/// </para>
/// <para>
/// <b><see cref="Default"/> is Claude's ladder, and that is deliberate.</b> A
/// <see cref="ConfigScope"/> built from it stores <see langword="null"/> for its ladder, so
/// <c>default(ConfigScope)</c> still equals <see cref="ConfigScope.Managed"/> under plain
/// struct equality and the four <c>ConfigScope</c> statics keep comparing equal to the
/// scopes Claude's clients hand out. Phase 3 measured what happens when that invariant
/// slips: a shape whose <c>default</c> stopped being <c>Managed</c> passed 2,791 of 2,792
/// tests while silently changing the meaning of a dozen uninitialised
/// <c>private ConfigScope _lastScope;</c> fields.
/// </para>
/// <para>
/// <b>Ordering is load-bearing.</b> Rungs are supplied highest-priority first and their
/// index becomes <see cref="ConfigScope.Ordinal"/>, which the merge engine sorts by and
/// <c>ClaudeScope.ToLibraryPriority</c> inverts. A ladder that lists its rungs the other way
/// round inverts precedence everywhere with no other symptom.
/// </para>
/// </remarks>
public sealed class ScopeLadder
{
    /// <summary>
    /// Claude's ladder: <c>Managed &gt; Local &gt; Project &gt; User</c>, one read-only rung.
    /// </summary>
    /// <remarks>
    /// Named <c>Default</c> rather than <c>Claude</c> because <c>AgentForge.Core</c> must not
    /// grow a Claude vocabulary — but it <i>is</i> Claude's ladder, and a second product must
    /// supply its own rather than inherit this one. See the class remarks for why it is also
    /// the value <see cref="ConfigScope"/> encodes as <see langword="null"/>.
    /// </remarks>
    public static ScopeLadder Default { get; } = new(
        "claude",
        isDefault: true,
        new ScopeRung("Managed", IsReadOnly: true),
        new ScopeRung("Local", IsReadOnly: false),
        new ScopeRung("Project", IsReadOnly: false),
        new ScopeRung("User", IsReadOnly: false));

    private readonly ScopeRung[] _rungs;

    /// <summary>
    /// Whether this instance is <see cref="Default"/>, recorded as a field rather than tested
    /// with <c>ReferenceEquals(this, Default)</c>.
    /// <para>
    /// ⚠ <b>Not a micro-optimisation — a correctness fix for static initialisation order.</b>
    /// <see cref="Default"/>'s own constructor builds <see cref="All"/>, and at that moment the
    /// <see cref="Default"/> property is still <see langword="null"/>. A
    /// <c>ReferenceEquals</c> test would therefore be <see langword="false"/> during exactly
    /// the one construction it needs to be true for, so the scopes inside
    /// <c>ConfigScope.All</c> would carry a non-null ladder while <c>ConfigScope.Managed</c>
    /// — built later, once the property is assigned — carried null. They would compare
    /// <b>unequal</b>, with nothing to indicate why.
    /// </para>
    /// </summary>
    private readonly bool _isDefault;

    /// <param name="id">
    /// Identifies the ladder in diagnostics. Not part of scope equality — two scopes are the
    /// same when they share a ladder instance and an ordinal.
    /// </param>
    /// <param name="rungs">The rungs, <b>highest-priority first</b>. At least one.</param>
    public ScopeLadder(string id, params ScopeRung[] rungs)
        : this(id, isDefault: false, rungs)
    {
    }

    private ScopeLadder(string id, bool isDefault, params ScopeRung[] rungs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(rungs);
        if (rungs.Length == 0)
        {
            throw new ArgumentException("A scope ladder needs at least one rung.", nameof(rungs));
        }

        Id = id;
        _isDefault = isDefault;
        _rungs = [.. rungs];
        All = [.. Enumerable.Range(0, _rungs.Length).Select(ScopeAt)];
    }

    /// <inheritdoc cref="ScopeLadder(string, ScopeRung[])"/>
    public string Id { get; }

    /// <summary>Every scope on this ladder, highest-priority first.</summary>
    public IReadOnlyList<ConfigScope> All { get; }

    /// <summary>How many rungs this ladder has.</summary>
    public int Count => _rungs.Length;

    /// <summary>
    /// The scope at <paramref name="ordinal"/> — 0 is the highest-priority rung.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ordinal"/> is not a rung on this ladder. Loud on purpose: a ladder
    /// silently answering for a rung it does not have is how the two hardcoded arrays this
    /// type replaced produced wrong read-only flags instead of failing.
    /// </exception>
    public ConfigScope ScopeAt(int ordinal)
    {
        if ((uint)ordinal >= (uint)_rungs.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordinal), ordinal,
                $"Scope ladder '{Id}' has {_rungs.Length} rung(s).");
        }

        // A scope on the default ladder stores null, so plain struct equality keeps
        // default(ConfigScope) == Managed. See the class remarks and _isDefault.
        return new ConfigScope(ordinal, _isDefault ? null : this);
    }

    internal ScopeRung RungAt(int ordinal)
    {
        return (uint)ordinal < (uint)_rungs.Length
            ? _rungs[ordinal]
            // Not an exception: ToString() and Id are called from logging and from AXAML
            // converters, where throwing turns a cosmetic mismatch into a crash. The
            // ordinal is a visible, searchable symptom instead.
            : new ScopeRung(ordinal.ToString(), IsReadOnly: false);
    }

    /// <summary>
    /// The lowest-priority rung that is editable — what a product offers when no
    /// configuration file has been discovered yet and the UI still needs a scope to target.
    /// </summary>
    /// <remarks>
    /// Replaces <c>AgentConfigClientCore.EditableScopes</c>' hardcoded
    /// <c>[ConfigScope.User]</c> fallback, which named Claude's lowest rung from
    /// product-neutral code. Falls back to the last rung when every rung is read-only —
    /// a ladder with nothing editable is a product's own statement, not this type's to
    /// second-guess.
    /// </remarks>
    public ConfigScope DefaultEditableScope
    {
        get
        {
            for (int i = _rungs.Length - 1; i >= 0; i--)
            {
                if (!_rungs[i].IsReadOnly)
                {
                    return ScopeAt(i);
                }
            }

            return ScopeAt(_rungs.Length - 1);
        }
    }

    public override string ToString()
    {
        return $"{Id}[{string.Join(" > ", _rungs.Select(r => r.Name))}]";
    }
}
