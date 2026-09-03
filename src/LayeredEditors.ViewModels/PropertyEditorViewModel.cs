namespace Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

/// <summary>
/// Abstract base for all property editor ViewModels in the reusable library.
/// Works exclusively with the <see cref="LayeredEditors.Abstractions"/> interfaces;
/// no JSON or domain types leak in here.
/// </summary>
public abstract partial class PropertyEditorViewModel : ObservableObject, IDangerAnnotatedEditor
{
    protected PropertyEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
    {
        Schema = schema;
        _editingScope = editingScope;
    }

    /// <summary>The schema definition for this property.</summary>
    public IEditorSchema Schema { get; }

    /// <summary>Display name (title if available, otherwise property name).</summary>
    public string DisplayName => Schema.Title ?? Schema.Name;

    /// <summary>Description text for tooltips / help areas.</summary>
    public string? Description => Schema.Description;

    /// <summary>The settings-tree path for this property (equivalent to the old JsonPath).</summary>
    public string Path => Schema.Path;

    /// <summary>
    /// The host product's danger policy, or <see langword="null"/> for a product that declares
    /// none. Supplied by the product's factory via
    /// <see cref="AttachDangerClassifier(IDangerClassifier?)"/>, deliberately NOT through
    /// <see cref="EditorContext"/> — see that type's remarks for why.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>init</c>-only rather than a constructor parameter on purpose. This base has a long tail
    /// of leaf and specialised subclasses, each with its own <c>base(schema, editingScope)</c>
    /// call; a third parameter would have to be threaded through every one of them. An
    /// <c>init</c> property is set by whoever news the editor up — the factory — so the
    /// subclasses stay untouched.
    /// </para>
    /// <para>
    /// ⚠ <b>The failure mode of that choice is silence.</b> A factory branch that forgets to set
    /// it produces an editor whose rows simply show no severity, which looks identical to "this
    /// product has nothing dangerous". That is the same shape as the
    /// factory-branch-without-a-DataTemplate trap, and it is why
    /// <c>OpenCodeEditorDangerWiringTests</c> drives every top-level schema node through the real
    /// factory and fails on any editor that came back without a classifier.
    /// </para>
    /// </remarks>
    public IDangerClassifier? DangerClassifier { get; private set; }

    /// <summary>
    /// Attach the host's danger policy, once, and return this editor for chaining.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>Exists so a factory has ONE assignment site instead of one per branch.</b>
    /// <c>OpenCodeEditorFactory</c> has a dozen <c>return new …EditorViewModel(schema, scope)</c>
    /// arms; setting the classifier in each object initializer means a new arm silently ships
    /// with no severity, which is indistinguishable from "this product declares nothing
    /// dangerous". Chaining this off a single choke point removes that class of mistake rather
    /// than guarding against it.
    /// </para>
    /// <para>
    /// Named <c>Attach…</c> and not <c>With…</c> deliberately: it MUTATES and returns the same
    /// instance. A <c>With</c> prefix reads as a record-style copy, which this is not.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A second call. Set-once keeps this from becoming general mutable state — an editor's
    /// product cannot change under it, so a second attach means two owners disagree about who
    /// built this editor.
    /// </exception>
    public PropertyEditorViewModel AttachDangerClassifier(IDangerClassifier? classifier)
    {
        if (DangerClassifier is not null)
        {
            throw new InvalidOperationException(
                $"A danger classifier is already attached to the editor for '{Path}'. "
                + "Attach it once, at construction, from the factory that owns this product.");
        }

        DangerClassifier = classifier;
        RecomputeDanger();
        return this;
    }

    /// <summary>
    /// How much attention this property deserves, and whether the value it holds right now is
    /// the unsafe one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recomputed on read rather than cached, and re-raised by <see cref="RecomputeDanger"/>
    /// whenever the value or the editing scope changes — the assessment is a function of both, so
    /// a cached copy goes stale the moment somebody types.
    /// </para>
    /// <para>
    /// ⚠ Passes <see cref="ToValue"/>, which is the editor's CURRENT (possibly unsaved) state.
    /// That is the intent: the point of a danger indicator is to fire while the change is still
    /// being made, not after it is written.
    /// </para>
    /// </remarks>
    public DangerAssessment Danger =>
        DangerClassifier?.Classify(Path, EditingScope, ToValue()) ?? DangerAssessment.Unremarkable;

    /// <summary>
    /// True when this property is worth drawing attention to at all — i.e. the product said
    /// something about it. Bound by the wrapper to decide whether to render a severity dot.
    /// </summary>
    public bool HasDangerSeverity => Danger.Explanation is not null;

    /// <summary>
    /// True when the value held right now is the unsafe one, so the wrapper should raise a
    /// standing banner rather than just tint a dot.
    /// </summary>
    public bool IsDangerNow => Danger.IsDangerNow;

    /// <summary>
    /// What a screen reader announces for the severity indicator.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>The indicator is a coloured geometric glyph, which conveys nothing to a screen
    /// reader.</b> Colour and shape are the two visual codes; this is the third, and without it
    /// the whole danger surface is sighted-only. Naming the tier as well as the consequence
    /// matters because the consequence sentence alone does not say how much it matters.
    /// <para>
    /// Composed here rather than in AXAML so it is unit-testable and so the two apps cannot
    /// format it differently.
    /// </para>
    /// <para>
    /// ⚠ <b>The severity word is an unlocalised English literal, and there is currently nowhere
    /// better to put it.</b> This assembly has no string surface at all: the library's
    /// resolver-based <c>WrapperStrings</c> lives in <c>LayeredEditors.Avalonia</c>, which sits
    /// ABOVE this one, so referencing it would invert the dependency. The consequence sentence
    /// itself comes from the product's danger table and is localisable there.
    /// </para>
    /// </remarks>
    public string DangerAccessibleText =>
        Danger.Explanation is null ? string.Empty : $"{Danger.Severity}: {Danger.Explanation}";

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Returns this editor's own <see cref="Danger"/> for its own <see cref="Path"/>, then
    /// descends into <see cref="IChildEditorHost.Children"/> — so a nested path like
    /// <c>attachment.image.maxWidth</c> resolves to the child editor that actually renders it,
    /// carrying that child's value rather than its parent's.
    /// </para>
    /// <para>
    /// ⛔ <b>The descent tests <see cref="IChildEditorHost"/>, never a concrete object-editor
    /// type.</b> There are two <c>ObjectPropertyEditorViewModel</c> classes — one here and one in
    /// the app — and the app's does not derive from this library's, so a type test would silently
    /// cover only half the object editors in play. That is the same trap
    /// <see cref="IChildEditorHost"/> was created for, and the symptom here would be a nested
    /// dangerous key reporting no severity at all.
    /// </para>
    /// <para>
    /// Ordinal comparison, matching the classifier's own table lookup: these are JSON paths, not
    /// display text, and a case-insensitive match would let <c>Permission</c> answer for
    /// <c>permission</c>.
    /// </para>
    /// </remarks>
    public DangerAssessment? AssessDanger(string jsonPath)
    {
        if (string.Equals(jsonPath, Path, StringComparison.Ordinal))
        {
            return Danger;
        }

        if (this is IChildEditorHost host)
        {
            foreach (PropertyEditorViewModel child in host.Children)
            {
                DangerAssessment? nested = child.AssessDanger(jsonPath);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Re-raise <see cref="Danger"/> and its companions. Call after anything that changes the
    /// value or the scope.
    /// </summary>
    protected void RecomputeDanger()
    {
        OnPropertyChanged(nameof(Danger));
        OnPropertyChanged(nameof(HasDangerSeverity));
        OnPropertyChanged(nameof(IsDangerNow));
        OnPropertyChanged(nameof(DangerAccessibleText));
    }

    /// <summary>
    /// True when the schema adapter reports this property was not present in the
    /// last persisted snapshot. The default <c>PropertyEditorWrapper</c> renders
    /// a "✨ NEW" chip when set, giving users a visual hint that a setting
    /// appeared since their previous session.
    /// </summary>
    public bool IsNew => Schema.IsNew;

    /// <summary>
    /// True when the schema marks this property as deprecated. Hosts typically
    /// hide deprecated properties from the editor list unless they are already
    /// set at some scope (see <see cref="IsSetAnywhere"/>) so users can still
    /// unset legacy values.
    /// </summary>
    public bool IsDeprecated => Schema.IsDeprecated;

    /// <summary>
    /// True when the property is not covered by official documentation.
    /// The default PropertyEditorWrapper renders a detective 🕵 badge instead
    /// of the raw "UNDOCUMENTED:" prefix text in the description.
    /// </summary>
    public bool IsUndocumented => Schema.IsUndocumented;

    /// <summary>
    /// Non-null when this editor was produced as the generic raw-JSON fallback
    /// because no structured editor matched the schema's shape. The default
    /// <c>PropertyEditorWrapper</c> renders a warning badge whose tooltip is this
    /// text; the property stays fully editable. Set once at construction by the
    /// editor factory (or the library's own fallback); <see langword="null"/> for
    /// every structured editor.
    /// </summary>
    [ObservableProperty] private string? _unsupportedShapeNotice;

    /// <summary>
    /// Optional section-level action rendered as a hyperlink immediately to the
    /// right of the property-name heading by the default <c>PropertyEditorWrapper</c>.
    /// Hidden when null/empty. Lets a specialized editor surface a heading-level
    /// affordance — e.g. the Hooks editor's "View flow diagram" jump to its Flow
    /// tab — without the generic wrapper knowing anything editor-specific; both this
    /// and <see cref="HeaderActionCommand"/> are compiled-bound against this base type,
    /// so every editor resolves them (null ⇒ the link collapses).
    /// </summary>
    [ObservableProperty] private string? _headerActionText;

    /// <summary>Command invoked by the header-action hyperlink. See <see cref="HeaderActionText"/>.</summary>
    [ObservableProperty] private System.Windows.Input.ICommand? _headerActionCommand;

    /// <summary>
    /// True when any scope has an explicit value for this property — derived from
    /// <see cref="EffectiveScope"/> being non-null. Schema defaults and pure
    /// inheritance without an explicit value do NOT count. Change notifications
    /// are routed through the <c>EffectiveScope</c> partial handler so filtered
    /// views react when a value is added or removed.
    /// </summary>
    public bool IsSetAnywhere => EffectiveScope != null;

    // ── Scope state ────────────────────────────────────────────────────────────

    /// <summary>The scope the user is currently editing.</summary>
    [ObservableProperty] private IEditorScope _editingScope;

    /// <summary>The scope that provides the effective (winning) value.</summary>
    [ObservableProperty] private IEditorScope? _effectiveScope;

    /// <summary>True when more than one scope defines this property.</summary>
    [ObservableProperty] private bool _isOverridden;

    /// <summary>
    /// Scopes — other than the currently-editing scope — that also have an
    /// explicit value for this property.  Hosts use this to render
    /// scope-indicator chiclets next to the property name in the editor
    /// header so the user sees at a glance which other layers contribute.
    /// </summary>
    /// <remarks>
    /// Each entry is an <see cref="IEditorScope"/>; the
    /// host's scope-aware brushes / tooltips / display-name converters
    /// resolve the per-scope styling.  Default is empty.  Concrete leaves
    /// populate it from <see cref="IEditorValue.IsDefinedAt"/> over the
    /// scopes returned by the host's scope catalog.
    /// </remarks>
    [ObservableProperty] private IReadOnlyList<IEditorScope> _otherScopesWithData = [];

    /// <summary>
    /// True when this property must not be edited — either because the schema marks it
    /// read-only, or because the effective scope itself is read-only (e.g. Managed).
    /// Replaces the old <c>IsManagedLocked</c> name.
    /// </summary>
    public bool IsLocked => Schema.IsReadOnly || EffectiveScope?.IsReadOnly == true;

    // ── Modification state ─────────────────────────────────────────────────────

    /// <summary>True when the editing scope has an explicitly set value.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReset))]
    [NotifyCanExecuteChangedFor(nameof(ResetToInheritedCommand))]
    private bool _isModified;

    /// <summary>True when the user can reset (clear) this property at the current scope.</summary>
    public bool CanReset => IsModified && !EditingScope.IsReadOnly;

    // ── Inherited-value display ────────────────────────────────────────────────

    /// <summary>
    /// Short text of the value this property will inherit when not explicitly set
    /// at the editing scope — either from a higher-priority scope or the schema default.
    /// <c>null</c> when the editing scope already has an explicit value, or when there
    /// is nothing to inherit.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Watermark))]
    [NotifyPropertyChangedFor(nameof(HasInheritedFromOtherScope))]
    private string? _inheritedDisplay;

    /// <summary>
    /// companion to <see cref="InheritedDisplay"/>: the scope
    /// that the inherited value came from, or <c>null</c> when the
    /// inherited value is the schema default (no scope owns it) OR when
    /// the editing scope itself has the value.  Bound by
    /// <c>PropertyEditorWrapper</c> to render a coloured chiclet next to
    /// the "Currently effective from {scope}" line below the editor, so
    /// the user can tell at a glance not just WHAT value will be used
    /// when this scope is empty but WHICH scope provides it.
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasInheritedFromOtherScope))]
    private IEditorScope? _inheritedFromScope;

    /// <summary>
    /// True when the property's effective value comes from a scope OTHER
    /// than the editing scope AND a non-null display text exists.  Drives
    /// the visibility of the App-side wrapper's "[scope] {value}" row
    /// below the editor.  False on rows where the editor's own value is
    /// the source of truth (no inheritance to display) or where there is
    /// no value anywhere (nothing to show).
    /// </summary>
    public bool HasInheritedFromOtherScope =>
        InheritedDisplay is not null && InheritedFromScope is not null;

    /// <summary>
    /// Watermark text for the editor input — "(not set)" when nothing is inherited,
    /// or "(inherits: {value})" when a lower scope or the schema default provides a value.
    /// </summary>
    public string Watermark =>
        InheritedDisplay is null ? "(not set)" : $"(inherits: {InheritedDisplay})";

    /// <summary>
    /// Computes and sets <see cref="InheritedDisplay"/> +
    /// <see cref="InheritedFromScope"/> from the layered value and editing
    /// scope.  Call this at the end of every <c>LoadFromValue</c>
    /// override.
    /// </summary>
    /// <remarks>
    /// also populates <see cref="InheritedFromScope"/> so the
    /// View can render the source scope as a coloured chiclet alongside
    /// the inherited value.  The semantics mirror
    /// <see cref="InheritedDisplay"/>: scope is non-null only when the
    /// inherited value comes from an actual scope (not the schema default).
    /// </remarks>
    protected void UpdateInheritedDisplay(IEditorValue value, IEditorScope editingScope)
    {
        if (value.IsDefinedAt(editingScope))
        {
            InheritedDisplay = null;
            InheritedFromScope = null;
            return;
        }

        // Empty-string-effective-value guard.  An empty string is
        // functionally equivalent to "not set" for display purposes (the SDK's
        // own value-setter coerces empty to unset on save), but
        // FormatForDisplay returns "" unchanged, so the early-return below
        // used to assign InheritedDisplay="" and InheritedFromScope=non-null.
        // That rendered as "(inherits: )" in the watermark (empty value after
        // the colon) AND fired HasInheritedFromOtherScope=true so the chiclet
        // row appeared below the editor with no value text alongside it.
        // Both shapes are user-visible bugs.  Fall through to the schema-
        // default branch when the formatted display is empty so the user
        // sees a meaningful "(inherits: sonnet)" or "(not set)" instead.
        if (value.EffectiveValue is not null)
        {
            string? formatted = TruncateDisplay(FormatForDisplay(value.EffectiveValue));
            if (!string.IsNullOrEmpty(formatted))
            {
                InheritedDisplay = formatted;
                InheritedFromScope = value.EffectiveScope;
                return;
            }
            // Empty formatted — fall through to schema default.
        }

        if (Schema.DefaultValue is not null)
        {
            string? formatted = TruncateDisplay(FormatForDisplay(Schema.DefaultValue));
            if (!string.IsNullOrEmpty(formatted))
            {
                InheritedDisplay = formatted;
                // Schema default has no owning scope — the chiclet stays
                // hidden in this case and only the "(inherits: …)" watermark
                // text communicates the fallback.
                InheritedFromScope = null;
                return;
            }
        }

        InheritedDisplay = null;
        InheritedFromScope = null;
    }

    /// <summary>
    /// populate <see cref="OtherScopesWithData"/> from
    /// <paramref name="value"/>'s <see cref="IEditorValue.EnumerateDefinedScopes"/>,
    /// excluding the current editing scope.  Used by the simple leaf
    /// editors (Boolean / String / Number / Enum / Path / StringArray) so
    /// they render the "Defined in scopes:" affordance the same way
    /// compound editors (Hooks / MCP / Permissions / Marketplaces / …)
    /// already do.  Call this from every <c>LoadFromValue</c> override
    /// alongside <see cref="UpdateInheritedDisplay"/>.
    /// </summary>
    /// <remarks>
    /// Pre-fix, only compound editors populated this via the App-side
    /// <c>SetScopeState</c>; leaves shipped with the default empty list,
    /// so the wrapper's "Defined in scopes:" label never fired for them.
    /// The matching unit tests live in each leaf's
    /// <c>*PropertyEditorViewModelTests</c>.
    /// </remarks>
    protected void UpdateOtherScopesWithData(IEditorValue value, IEditorScope editingScope)
    {
        OtherScopesWithData = value
                              .EnumerateDefinedScopes()
                              .Where(s => s.Id != editingScope.Id)
                              .ToList();
    }

    private static string? FormatForDisplay(object? v)
    {
        return v switch
        {
            string s => s,
            bool b => b ? "true" : "false",
            // Compound objects (permissions, hooks, mcpServers …) normalise to
            // IReadOnlyDictionary<string,object?>.  Their .ToString() produces a
            // raw collection representation — unhelpful and visually broken in
            // the "inherited from <scope>:" row.  Return null to suppress the
            // row for these types; the "Defined in scopes:" header chiclets
            // already communicate that other scopes have data.
            IReadOnlyDictionary<string, object?> => null,
            // String-array properties (sandbox.network.allowedDomains, etc.)
            // normalise to IReadOnlyList<object?>.  A compact count is more
            // readable than the List<T>.ToString() fallback.
            IReadOnlyList<object?> list => list.Count > 0 ? $"[{list.Count} item(s)]" : null,
            var _ => v?.ToString(),
        };
    }

    private static string? TruncateDisplay(string? s)
    {
        return s is null ? null : s.Length > 50 ? s[..50] + "…" : s;
    }

    // ── Abstract interface ─────────────────────────────────────────────────────

    /// <summary>
    /// Return the current editor state as a value obeying the currency contract:
    /// <c>null | bool | string | long | double |
    /// IReadOnlyList&lt;object?&gt; | IReadOnlyDictionary&lt;string, object?&gt;</c>.
    /// Returns <c>null</c> when the property should be removed from the current scope.
    /// </summary>
    public abstract object? ToValue();

    /// <summary>
    /// Load the editor state from the layered value at the given editing scope.
    /// </summary>
    public abstract void LoadFromValue(IEditorValue value, IEditorScope editingScope);

    // ── Value-set tracking helper ──────────────────────────────────────────────

    /// <summary>
    /// Synchronises <see cref="IsModified"/> with the "is the editor's value set?" predicate
    /// and re-raises <see cref="ObservableObject.PropertyChanged"/> when needed so that
    /// downstream consumers (e.g. <c>SettingsGroupEditorViewModel</c>) see every value
    /// change — not just null↔non-null transitions.
    /// </summary>
    /// <param name="isSet">
    /// <see langword="true"/> when the editor's underlying value is now non-null /
    /// non-empty (i.e. the property is explicitly set at the editing scope);
    /// <see langword="false"/> when it has been cleared.
    /// </param>
    /// <remarks>
    /// <para>
    /// Replaces six near-identical hand-written force-fire blocks across the leaf
    /// editors (Boolean, String, Number, Enum, Path, StringArray).
    /// </para>
    /// <para>
    /// Without the explicit re-raise on the <c>wasModified &amp;&amp; isSet</c> path,
    /// CommunityToolkit.Mvvm's <c>[ObservableProperty]</c>-generated setter elides
    /// equal assignments — cycling between two non-null values (e.g. <c>true → false</c>
    /// for Boolean, or one URL → another for String) would leave <c>IsModified</c> at
    /// <see langword="true"/> but never raise <c>PropertyChanged(IsModified)</c>, so
    /// the workspace would not see the new value and the Save button would stay
    /// disabled.
    /// </para>
    /// </remarks>
    protected void TrackValueSet(bool isSet)
    {
        bool wasModified = IsModified;
        IsModified = isSet;
        if (wasModified && isSet)
        {
            OnPropertyChanged(nameof(IsModified));
        }

        // ⚠ UNCONDITIONAL, unlike the IsModified re-raise above. The danger assessment depends on
        // the VALUE, not on whether the modified flag moved — switching one unsafe value for
        // another (or an unsafe one for a safe one) leaves IsModified untouched while the
        // assessment flips. Guarding this with `wasModified` would leave a stale "unsafe" banner
        // sitting over a value the user had already fixed.
        RecomputeDanger();
    }

    // ── Reset command ──────────────────────────────────────────────────────────

    /// <summary>Remove the explicit value at the current editing scope (revert to inherited).</summary>
    [RelayCommand(CanExecute = nameof(CanReset))]
    protected virtual void ResetToInherited()
    {
        IsModified = false;
        OnResetToInherited();

        // Reverting to the inherited value changes what this scope holds, so the assessment
        // changes with it — a row that was red because the user set `allow` here must stop being
        // red once that override is gone.
        RecomputeDanger();
    }

    /// <summary>Called from <see cref="ResetToInherited"/> after flag is cleared.
    /// Concrete implementations clear their value fields here.</summary>
    protected virtual void OnResetToInherited()
    {
    }

    // ── Change notifications ───────────────────────────────────────────────────

    partial void OnEditingScopeChanged(IEditorScope value)
    {
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(CanReset));

        // Severity escalates with scope: the same API key is a local secret at a user-global
        // scope and a git-committed one at project scope, so switching the scope selector must
        // repaint the row even though the value never moved.
        RecomputeDanger();
    }

    partial void OnEffectiveScopeChanged(IEditorScope? value)
    {
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(IsSetAnywhere));
    }
}