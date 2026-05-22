using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// locks the App-bridge <see cref="PropertyEditorViewModel"/>'s
/// new virtual default implementations of <c>ToJsonValue</c> and
/// <c>LoadFromLayered</c>.
/// </summary>
/// <remarks>
/// <para>
/// Step 2 changed both methods from abstract to virtual with library-delegating
/// defaults, so a future migrated leaf can override only the library's
/// <c>ToValue()</c> / <c>LoadFromValue()</c> and still satisfy App-side
/// callers via the bridge.  These tests verify:
/// </para>
/// <list type="number">
///   <item>A leaf overriding only library <c>ToValue</c> produces the right
///         JSON via the bridge's default <c>ToJsonValue</c>.</item>
///   <item>A leaf overriding only library <c>LoadFromValue</c> sees a
///         <see cref="ClaudeValueAdapter"/> when the App-side caller passes
///         a <see cref="LayeredValue"/>.</item>
///   <item>A leaf overriding NEITHER side throws a clear
///         <see cref="InvalidOperationException"/> from the recursion guard
///         instead of stack-overflowing.</item>
/// </list>
/// </remarks>
[TestClass]
public sealed class PropertyEditorBridgeDefaultsTests
{
    private static SchemaNode S(string name)
    {
        return new SchemaNode(name, name);
    }

    // ── ToJsonValue default ──────────────────────────────────────────

    [TestMethod]
    public void ToJsonValue_DefaultPath_ConvertsToValueResultViaJsonCurrency()
    {
        // FUTURE-style leaf: overrides library ToValue() only, leaves the
        // App-bridge ToJsonValue() at its new default.  The default must
        // produce the JsonNode equivalent of whatever ToValue returned.
        LibraryToValueOnlyLeaf leaf = new(S("x"), ConfigScope.User)
        {
            Value = 42L,
        };

        JsonNode? json = leaf.ToJsonValue();

        Assert.IsNotNull(json);
        Assert.AreEqual("42", json.ToJsonString(),
            "Default ToJsonValue must round-trip ToValue() through JsonCurrency.");
    }

    [TestMethod]
    public void ToJsonValue_DefaultPath_PreservesNullValue()
    {
        LibraryToValueOnlyLeaf leaf = new(S("x"), ConfigScope.User) { Value = null };

        Assert.IsNull(leaf.ToJsonValue(),
            "ToValue=null must round-trip to ToJsonValue=null via JsonCurrency.ToJsonNode.");
    }

    [TestMethod]
    public void ToJsonValue_LeafOverridesNeither_ThrowsWithDiagnostic()
    {
        BothDefaultsLeaf leaf = new(S("x"), ConfigScope.User);

        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() => leaf.ToJsonValue());

        StringAssert.Contains(ex.Message, "BothDefaultsLeaf",
            "Diagnostic must name the offending leaf type.");
        StringAssert.Contains(ex.Message, "ToJsonValue",
            "Diagnostic must mention the App-side override option.");
        StringAssert.Contains(ex.Message, "ToValue",
            "Diagnostic must mention the library-side override option.");
    }

    [TestMethod]
    public void ToJsonValue_AppSideOverride_ShortCircuitsBeforeDefault()
    {
        // EXISTING-style leaf: overrides ToJsonValue directly. The new
        // default never runs, so even though ToValue isn't overridden
        // there is no recursion.
        AppSideToJsonValueOnlyLeaf leaf = new(S("x"), ConfigScope.User)
        {
            Json = JsonValue.Create("hello"),
        };

        JsonNode? json = leaf.ToJsonValue();
        Assert.IsNotNull(json);
        Assert.AreEqual("\"hello\"", json.ToJsonString(),
            "Existing-style leaves' ToJsonValue overrides must take precedence over the new default.");
    }

    // ── LoadFromLayered default ──────────────────────────────────────

    [TestMethod]
    public void LoadFromLayered_DefaultPath_DelegatesToLoadFromValueWithClaudeValueAdapter()
    {
        // FUTURE-style leaf: overrides library LoadFromValue only.  When a
        // legacy caller hands it a LayeredValue, the bridge's default must
        // wrap it in ClaudeValueAdapter and forward.
        LibraryLoadFromValueOnlyLeaf leaf = new(S("x"), ConfigScope.User);

        List<ScopeEntry> entries =
        [
            new(ConfigScope.User, JsonValue.Create("user-val"), "/u.json"),
        ];
        LayeredValue layered = new("x", entries)
        {
            EffectiveValue = JsonValue.Create("user-val"),
            EffectiveScope = ConfigScope.User,
        };

        leaf.LoadFromLayered(layered, ConfigScope.User);

        Assert.IsNotNull(leaf.LastReceivedValue,
            "Default LoadFromLayered must invoke library LoadFromValue.");
        Assert.IsInstanceOfType<ClaudeValueAdapter>(leaf.LastReceivedValue,
            "Wrapped value must be a ClaudeValueAdapter so adapters that "
            + "type-test for the underlying LayeredValue can recover it.");
    }

    [TestMethod]
    public void LoadFromLayered_LeafOverridesNeither_ThrowsWithDiagnostic()
    {
        BothDefaultsLeaf leaf = new(S("x"), ConfigScope.User);
        LayeredValue layered = new("x", []);

        InvalidOperationException ex =
            Assert.ThrowsException<InvalidOperationException>(() => leaf.LoadFromLayered(layered, ConfigScope.User));

        StringAssert.Contains(ex.Message, "LoadFromLayered");
        StringAssert.Contains(ex.Message, "LoadFromValue");
    }

    // ── Legacy bridge aliases (Schema / JsonPath / IsManagedLocked) ──
    //
    // the App-bridge keeps three legacy
    // property aliases for backward-compat with existing AXAML bindings
    // (PropertyEditorWrapper.axaml) + Claude-specific consumers that
    // expect the original SchemaNode type rather than the library's
    // IEditorSchema.  These were uncovered until now.

    [TestMethod]
    public void Schema_LegacyAlias_ReturnsConstructorSchemaNode()
    {
        // The base library exposes Schema typed as IEditorSchema.  The
        // bridge uses `new` to shadow it with the concrete SchemaNode
        // the App-side ctor was given — App-side consumers can still
        // read schema-specific properties (DefaultValue, EnumValues, …)
        // without down-casting.
        SchemaNode schema = S("authToken");
        LibraryToValueOnlyLeaf leaf = new(schema, ConfigScope.User);

        Assert.AreSame(schema, leaf.Schema,
            "App-bridge Schema getter must return the original SchemaNode reference, not a re-wrapped IEditorSchema.");
    }

    [TestMethod]
    public void JsonPath_LegacyAlias_MatchesLibraryPath()
    {
        // JsonPath is the legacy name for the library's Path property.
        // Both must agree — they are the same dot-separated route.
        LibraryToValueOnlyLeaf leaf = new(S("auth.token"), ConfigScope.User);

        Assert.AreEqual(leaf.Path, leaf.JsonPath,
            "JsonPath must mirror the library's Path so legacy bindings keep working.");
        Assert.AreEqual("auth.token", leaf.JsonPath);
    }

    [TestMethod]
    public void IsManagedLocked_LegacyAlias_MatchesIsLocked()
    {
        // IsManagedLocked is the legacy name for IsLocked (the library
        // renamed it on the move out of the App-bridge).  Tests-of-record
        // for the lock state live elsewhere; this just locks the alias.
        LibraryToValueOnlyLeaf leaf = new(S("x"), ConfigScope.User);

        Assert.AreEqual(leaf.IsLocked, leaf.IsManagedLocked,
            "IsManagedLocked must mirror IsLocked so legacy bindings keep working.");
    }

    // ── BuildFallbackLayeredValue (non-ClaudeValueAdapter IEditorValue) ──
    //
    // the bridge's LoadFromValue branches on
    // whether the incoming IEditorValue is a ClaudeValueAdapter.  Normal
    // production calls always pass a ClaudeValueAdapter (the App-side
    // wraps the LayeredValue itself), so the fallback synthesis path was
    // never hit by integration tests — only by hypothetical test-fakes
    // that hand the bridge a raw IEditorValue.  These tests exercise that
    // path so coverage reflects the contract.

    [TestMethod]
    public void LoadFromValue_NonAdapterIEditorValue_SynthesizesLayeredViaFallback()
    {
        // Pass a non-ClaudeValueAdapter fake to a leaf that overrides ONLY
        // LoadFromLayered (legacy pattern).  Dispatch:
        //   leaf.LoadFromValue is NOT overridden → bridge's override runs.
        //   value is NOT ClaudeValueAdapter → BuildFallbackLayeredValue fires.
        //   bridge calls LoadFromLayered(synth, …) → leaf override captures.
        LegacyLoadFromLayeredOnlyLeaf leaf = new(S("model"), ConfigScope.User);
        ClaudeScope userScope = ClaudeScope.For(ConfigScope.User);
        MinimalFakeEditorValue fakeValue = new MinimalFakeEditorValue("model")
            .With(userScope, "sonnet");

        leaf.LoadFromValue(fakeValue, userScope);

        Assert.IsNotNull(leaf.LastLayered,
            "BuildFallbackLayeredValue must have synthesised a LayeredValue and dispatched it.");
        Assert.AreEqual(1, leaf.LastLayered!.Entries.Count,
            "Single defined scope → single synthesised entry.");
        Assert.AreEqual(ConfigScope.User, leaf.LastLayered.Entries[0].Scope);
    }

    [TestMethod]
    public void LoadFromValue_NonAdapter_EmptyValue_FallbackProducesEmptyEntries()
    {
        // No scope has defined this property → BuildFallbackLayeredValue
        // produces a LayeredValue with NO entries + null EffectiveScope.
        LegacyLoadFromLayeredOnlyLeaf leaf = new(S("empty"), ConfigScope.User);
        ClaudeScope userScope = ClaudeScope.For(ConfigScope.User);
        MinimalFakeEditorValue fakeValue = new("empty"); // no With() calls

        leaf.LoadFromValue(fakeValue, userScope);

        Assert.IsNotNull(leaf.LastLayered);
        Assert.AreEqual(0, leaf.LastLayered!.Entries.Count,
            "Empty fake → no entries in the synthesised LayeredValue.");
        Assert.IsNull(leaf.LastLayered.EffectiveScope,
            "Empty fake → no EffectiveScope.");
    }

    [TestMethod]
    public void LoadFromValue_NonAdapter_MultiScope_FallbackPreservesAllEntries()
    {
        // Two scopes defined → fallback synthesises both entries.  The
        // fake reports the higher-priority scope as effective.
        LegacyLoadFromLayeredOnlyLeaf leaf = new(S("model"), ConfigScope.User);
        ClaudeScope userScope = ClaudeScope.For(ConfigScope.User);
        ClaudeScope projectScope = ClaudeScope.For(ConfigScope.Project);
        MinimalFakeEditorValue fakeValue = new MinimalFakeEditorValue("model")
                                           .With(userScope, "sonnet")
                                           .With(projectScope, "opus");

        leaf.LoadFromValue(fakeValue, userScope);

        Assert.IsNotNull(leaf.LastLayered);
        Assert.AreEqual(2, leaf.LastLayered!.Entries.Count,
            "Both scope entries must round-trip through BuildFallbackLayeredValue.");
        Assert.IsNotNull(leaf.LastLayered.EffectiveScope,
            "Multi-scope fake → EffectiveScope must be populated.");
    }

    // ── Plumbing ─────────────────────────────────────────────────────

    /// <summary>Future-style leaf: overrides library <c>ToValue()</c> only.</summary>
    private sealed class LibraryToValueOnlyLeaf : PropertyEditorViewModel
    {
        public LibraryToValueOnlyLeaf(SchemaNode s, ConfigScope sc) : base(s, sc)
        {
        }

        public object? Value { get; set; }

        public override object? ToValue()
        {
            return Value;
        }

        public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
        {
        }
    }

    /// <summary>
    /// Existing-style leaf: overrides App-side <c>ToJsonValue()</c> only.
    /// All six current leaves (Boolean, String, Number, Enum, Path,
    /// StringArray) follow this pattern.
    /// </summary>
    private sealed class AppSideToJsonValueOnlyLeaf : PropertyEditorViewModel
    {
        public AppSideToJsonValueOnlyLeaf(SchemaNode s, ConfigScope sc) : base(s, sc)
        {
        }

        public JsonNode? Json { get; set; }

        public override JsonNode? ToJsonValue()
        {
            return Json?.DeepClone();
        }

        public override void LoadFromLayered(LayeredValue layered, ConfigScope editingScope)
        {
        }
    }

    /// <summary>Future-style leaf: overrides library <c>LoadFromValue</c> only.</summary>
    private sealed class LibraryLoadFromValueOnlyLeaf : PropertyEditorViewModel
    {
        public LibraryLoadFromValueOnlyLeaf(SchemaNode s, ConfigScope sc) : base(s, sc)
        {
        }

        public IEditorValue? LastReceivedValue { get; private set; }

        public override object? ToValue()
        {
            return null;
        }

        public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
        {
            LastReceivedValue = value;
        }
    }

    /// <summary>
    /// Legacy-style leaf: overrides only the App-side <c>LoadFromLayered</c>
    /// (the pattern still used by the 11 compound editors).  Used to
    /// exercise the bridge's <c>LoadFromValue</c> default path when a
    /// non-<see cref="ClaudeValueAdapter"/> <see cref="IEditorValue"/> is
    /// passed — which forces <c>BuildFallbackLayeredValue</c> to fire.
    /// </summary>
    private sealed class LegacyLoadFromLayeredOnlyLeaf : PropertyEditorViewModel
    {
        public LegacyLoadFromLayeredOnlyLeaf(SchemaNode s, ConfigScope sc) : base(s, sc)
        {
        }

        public LayeredValue? LastLayered { get; private set; }
        public ConfigScope? LastEditingScope { get; private set; }

        public override JsonNode? ToJsonValue()
        {
            return null;
        }

        public override void LoadFromLayered(LayeredValue layered, ConfigScope editingScope)
        {
            LastLayered = layered;
            LastEditingScope = editingScope;
        }
    }

    /// <summary>
    /// Minimal <see cref="IEditorValue"/> fake — NOT a
    /// <see cref="ClaudeValueAdapter"/> — so the bridge's
    /// <c>LoadFromValue</c> takes the <c>BuildFallbackLayeredValue</c>
    /// branch rather than the unwrap-the-adapter branch.  Mirrors the
    /// shape of <c>tests/LayeredEditors.Avalonia.Tests/Fakes/FakeEditorValue.cs</c>
    /// but inlined here to avoid a cross-project test-fake dependency.
    /// </summary>
    private sealed class MinimalFakeEditorValue : IEditorValue
    {
        private readonly List<(IEditorScope Scope, object? Value)> _entries = [];

        public MinimalFakeEditorValue(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public MinimalFakeEditorValue With(IEditorScope scope, object? value)
        {
            _entries.RemoveAll(e => e.Scope.Id == scope.Id);
            _entries.Add((scope, value));
            return this;
        }

        public IEditorScope? EffectiveScope =>
            _entries.OrderByDescending(e => e.Scope.Priority).Select(e => e.Scope).FirstOrDefault();

        public object? EffectiveValue =>
            EffectiveScope is { } s ? GetValueAt(s) : null;

        public bool IsOverridden => _entries.Count > 1;

        public object? GetValueAt(IEditorScope scope)
        {
            return _entries.FirstOrDefault(e => e.Scope.Id == scope.Id).Value;
        }

        public bool IsDefinedAt(IEditorScope scope)
        {
            return _entries.Any(e => e.Scope.Id == scope.Id);
        }

        public IEnumerable<IEditorScope> EnumerateDefinedScopes()
        {
            return _entries.Select(e => e.Scope);
        }
    }

    /// <summary>
    /// Misconfigured leaf: overrides NEITHER side.  Hits the recursion guard
    /// in both ToJsonValue and LoadFromLayered.
    /// </summary>
    private sealed class BothDefaultsLeaf : PropertyEditorViewModel
    {
        public BothDefaultsLeaf(SchemaNode s, ConfigScope sc) : base(s, sc)
        {
        }

        // No ToJsonValue, no ToValue, no LoadFromLayered, no LoadFromValue overrides.
        // Library's ToValue is abstract — must override SOMETHING. Override
        // ToValue to satisfy the library contract and let our bridge default
        // for ToJsonValue's call to ToValue() not bottom out at "abstract".
        // The cycle now exists between bridge.ToValue (=> Normalise(ToJsonValue))
        // and bridge.ToJsonValue (=> ToJsonNode(ToValue)).
        public override object? ToValue()
        {
            // Calling ToJsonValue here triggers the cycle the guard catches.
            // We only call it because the library would otherwise fail at the
            // abstract-method level; the guard's diagnostic is the real test.
            return ClaudeValueAdapter.Normalise(ToJsonValue());
        }

        public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
        {
            // Trigger the cycle the LoadFromLayered guard catches.
            LayeredValue lv = (value as ClaudeValueAdapter)?.Inner ?? new LayeredValue("x", []);
            LoadFromLayered(lv, ClaudeScope.ToConfigScope(editingScope));
        }
    }
}