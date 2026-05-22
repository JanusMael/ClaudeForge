using Bennewitz.Ninja.ClaudeForge.Adapters;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Adapters;

/// <summary>
/// coverage for <see cref="ClaudeValueAdapter"/>.
/// </summary>
/// <remarks>
/// Companion to the existing <see cref="ClaudeValueAdapterTests"/> file —
/// the latter covers behaviour, this file fills coverage gaps in the
/// pure-function helpers.
/// </remarks>
[TestClass]
public sealed class ClaudeValueAdapterCoverageTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static LayeredValue Layered(string key, ConfigScope scope, JsonNode? node)
    {
        return new LayeredValue(key, [new ScopeEntry(scope, node, "/fake")])
        {
            EffectiveValue = node,
            EffectiveScope = scope,
        };
    }

    // ── Normalise — scalar widening branches ───────────────────────────────

    [TestMethod]
    public void Normalise_Int_WidensToLong()
    {
        // The currency contract is `long` for whole numbers; `int`-typed
        // JsonValue must widen rather than escape as `int`.
        JsonValue node = JsonValue.Create(42);
        ClaudeValueAdapter adapter = new(Layered("p", ConfigScope.User, node));
        Assert.AreEqual((long)42, adapter.EffectiveValue);
    }

    [TestMethod]
    public void Normalise_Short_WidensToLong()
    {
        JsonValue node = JsonValue.Create((short)7);
        ClaudeValueAdapter adapter = new(Layered("p", ConfigScope.User, node));
        Assert.AreEqual((long)7, adapter.EffectiveValue);
    }

    [TestMethod]
    public void Normalise_Byte_WidensToLong()
    {
        JsonValue node = JsonValue.Create((byte)255);
        ClaudeValueAdapter adapter = new(Layered("p", ConfigScope.User, node));
        Assert.AreEqual((long)255, adapter.EffectiveValue);
    }

    [TestMethod]
    public void Normalise_Float_WidensToDouble()
    {
        // Currency is `double` for floating point; `float` must widen.
        JsonValue node = JsonValue.Create(2.5f);
        ClaudeValueAdapter adapter = new(Layered("p", ConfigScope.User, node));
        Assert.IsInstanceOfType(adapter.EffectiveValue, typeof(double));
        Assert.AreEqual(2.5, (double)adapter.EffectiveValue!, 0.0001);
    }

    [TestMethod]
    public void Normalise_JsonElement_FallbackPath_HandlesParsedJson()
    {
        // Parsing JSON via JsonNode.Parse yields a JsonValue whose
        // backing storage is a JsonElement; that path requires the
        // fallback branch in NormaliseScalar.
        JsonObject parsed = JsonNode.Parse("""{"k":42,"f":3.14,"s":"hi","b":true}""")!.AsObject();

        ClaudeValueAdapter kAdapter = new(Layered("k", ConfigScope.User, parsed["k"]));
        Assert.AreEqual((long)42, kAdapter.EffectiveValue);

        ClaudeValueAdapter fAdapter = new(Layered("f", ConfigScope.User, parsed["f"]));
        Assert.IsInstanceOfType(fAdapter.EffectiveValue, typeof(double));

        ClaudeValueAdapter sAdapter = new(Layered("s", ConfigScope.User, parsed["s"]));
        Assert.AreEqual("hi", sAdapter.EffectiveValue);

        ClaudeValueAdapter bAdapter = new(Layered("b", ConfigScope.User, parsed["b"]));
        Assert.IsTrue((bool?)bAdapter.EffectiveValue);
    }

    [TestMethod]
    public void Normalise_NestedObjectInsideArray_RecursesCorrectly()
    {
        // Validates that NormaliseArray recurses through NormaliseObject:
        // [ { "name": "alice" } ] -> IReadOnlyList<object?> with a
        // IReadOnlyDictionary inside.
        JsonNode? node = JsonNode.Parse("""[{"name":"alice","age":30}]""");
        ClaudeValueAdapter adapter = new(Layered("users", ConfigScope.User, node));
        IReadOnlyList<object?>? list = adapter.EffectiveValue as IReadOnlyList<object?>;
        Assert.IsNotNull(list);
        Assert.AreEqual(1, list!.Count);
        IReadOnlyDictionary<string, object?>? dict = list[0] as IReadOnlyDictionary<string, object?>;
        Assert.IsNotNull(dict);
        Assert.AreEqual("alice", dict!["name"]);
        Assert.AreEqual((long)30, dict["age"]);
    }

    [TestMethod]
    public void Normalise_NullJsonNode_ReturnsNull()
    {
        ClaudeValueAdapter adapter = new(Layered("p", ConfigScope.User, null));
        Assert.IsNull(adapter.EffectiveValue);
    }

    // ── EffectiveScope / IsOverridden ──────────────────────────────────────

    [TestMethod]
    public void EffectiveScope_NullWhenLayeredHasNoScope()
    {
        LayeredValue raw = new("p", []);
        ClaudeValueAdapter adapter = new(raw);
        Assert.IsNull(adapter.EffectiveScope);
    }

    [TestMethod]
    public void IsOverridden_PropagatesFromLayered()
    {
        LayeredValue raw = new("p",
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("a"), "/u"),
            new ScopeEntry(ConfigScope.Project, JsonValue.Create("b"), "/p"),
        ])
        {
            EffectiveValue = JsonValue.Create("b"),
            EffectiveScope = ConfigScope.Project,
            // IsOverridden derived inside LayeredValue
        };
        ClaudeValueAdapter adapter = new(raw);
        // Two distinct values across scopes => overridden.
        Assert.IsTrue(adapter.IsOverridden);
    }

    [TestMethod]
    public void GetValueAt_ReturnsPerScopeValue()
    {
        LayeredValue raw = new("p",
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user-val"), "/u"),
            new ScopeEntry(ConfigScope.Project, JsonValue.Create("project-val"), "/p"),
        ]);
        ClaudeValueAdapter adapter = new(raw);
        Assert.AreEqual("user-val", adapter.GetValueAt(ClaudeScope.For(ConfigScope.User)));
        Assert.AreEqual("project-val", adapter.GetValueAt(ClaudeScope.For(ConfigScope.Project)));
    }

    [TestMethod]
    public void IsDefinedAt_TrueWhereDefined_FalseWhereNot()
    {
        LayeredValue raw = new("p",
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("v"), "/u"),
        ]);
        ClaudeValueAdapter adapter = new(raw);
        Assert.IsTrue(adapter.IsDefinedAt(ClaudeScope.For(ConfigScope.User)));
        Assert.IsFalse(adapter.IsDefinedAt(ClaudeScope.For(ConfigScope.Local)));
        Assert.IsFalse(adapter.IsDefinedAt(ClaudeScope.For(ConfigScope.Project)));
    }

    [TestMethod]
    public void Path_ExposesLayeredJsonPath()
    {
        LayeredValue raw = new("permissions.allow", []);
        ClaudeValueAdapter adapter = new(raw);
        Assert.AreEqual("permissions.allow", adapter.Path);
    }
}