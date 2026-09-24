using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.ClaudeForge.Adapters;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Adapters;

/// <summary>
/// coverage for <see cref="LayeredValueAdapter"/>.
/// </summary>
/// <remarks>
/// Companion to the existing <see cref="LayeredValueAdapterTests"/> file —
/// the latter covers behaviour, this file fills coverage gaps in the
/// pure-function helpers.
/// </remarks>
public sealed class LayeredValueAdapterCoverageTests
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

    [Fact]
    public void Normalise_Int_WidensToLong()
    {
        // The currency contract is `long` for whole numbers; `int`-typed
        // JsonValue must widen rather than escape as `int`.
        JsonValue node = JsonValue.Create(42);
        LayeredValueAdapter adapter = new(Layered("p", ConfigScope.User, node));
        Assert.Equal((long)42, adapter.EffectiveValue);
    }

    [Fact]
    public void Normalise_Short_WidensToLong()
    {
        JsonValue node = JsonValue.Create((short)7);
        LayeredValueAdapter adapter = new(Layered("p", ConfigScope.User, node));
        Assert.Equal((long)7, adapter.EffectiveValue);
    }

    [Fact]
    public void Normalise_Byte_WidensToLong()
    {
        JsonValue node = JsonValue.Create((byte)255);
        LayeredValueAdapter adapter = new(Layered("p", ConfigScope.User, node));
        Assert.Equal((long)255, adapter.EffectiveValue);
    }

    [Fact]
    public void Normalise_Float_WidensToDouble()
    {
        // Currency is `double` for floating point; `float` must widen.
        JsonValue node = JsonValue.Create(2.5f);
        LayeredValueAdapter adapter = new(Layered("p", ConfigScope.User, node));
        Assert.IsAssignableFrom<double>(adapter.EffectiveValue);
        Assert.Equal(2.5, (double)adapter.EffectiveValue!, 0.0001);
    }

    [Fact]
    public void Normalise_JsonElement_FallbackPath_HandlesParsedJson()
    {
        // Parsing JSON via JsonNode.Parse yields a JsonValue whose
        // backing storage is a JsonElement; that path requires the
        // fallback branch in NormaliseScalar.
        JsonObject parsed = JsonNode.Parse("""{"k":42,"f":3.14,"s":"hi","b":true}""")!.AsObject();

        LayeredValueAdapter kAdapter = new(Layered("k", ConfigScope.User, parsed["k"]));
        Assert.Equal((long)42, kAdapter.EffectiveValue);

        LayeredValueAdapter fAdapter = new(Layered("f", ConfigScope.User, parsed["f"]));
        Assert.IsAssignableFrom<double>(fAdapter.EffectiveValue);

        LayeredValueAdapter sAdapter = new(Layered("s", ConfigScope.User, parsed["s"]));
        Assert.Equal("hi", sAdapter.EffectiveValue);

        LayeredValueAdapter bAdapter = new(Layered("b", ConfigScope.User, parsed["b"]));
        Assert.True((bool?)bAdapter.EffectiveValue);
    }

    [Fact]
    public void Normalise_NestedObjectInsideArray_RecursesCorrectly()
    {
        // Validates that NormaliseArray recurses through NormaliseObject:
        // [ { "name": "alice" } ] -> IReadOnlyList<object?> with a
        // IReadOnlyDictionary inside.
        JsonNode? node = JsonNode.Parse("""[{"name":"alice","age":30}]""");
        LayeredValueAdapter adapter = new(Layered("users", ConfigScope.User, node));
        IReadOnlyList<object?>? list = adapter.EffectiveValue as IReadOnlyList<object?>;
        Assert.NotNull(list);
        Assert.Single(list!);
        IReadOnlyDictionary<string, object?>? dict = list[0] as IReadOnlyDictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("alice", dict!["name"]);
        Assert.Equal((long)30, dict["age"]);
    }

    [Fact]
    public void Normalise_NullJsonNode_ReturnsNull()
    {
        LayeredValueAdapter adapter = new(Layered("p", ConfigScope.User, null));
        Assert.Null(adapter.EffectiveValue);
    }

    // ── EffectiveScope / IsOverridden ──────────────────────────────────────

    [Fact]
    public void EffectiveScope_NullWhenLayeredHasNoScope()
    {
        LayeredValue raw = new("p", []);
        LayeredValueAdapter adapter = new(raw);
        Assert.Null(adapter.EffectiveScope);
    }

    [Fact]
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
        LayeredValueAdapter adapter = new(raw);
        // Two distinct values across scopes => overridden.
        Assert.True(adapter.IsOverridden);
    }

    [Fact]
    public void GetValueAt_ReturnsPerScopeValue()
    {
        LayeredValue raw = new("p",
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user-val"), "/u"),
            new ScopeEntry(ConfigScope.Project, JsonValue.Create("project-val"), "/p"),
        ]);
        LayeredValueAdapter adapter = new(raw);
        Assert.Equal("user-val", adapter.GetValueAt(ConfigScopeAdapter.For(ConfigScope.User)));
        Assert.Equal("project-val", adapter.GetValueAt(ConfigScopeAdapter.For(ConfigScope.Project)));
    }

    [Fact]
    public void IsDefinedAt_TrueWhereDefined_FalseWhereNot()
    {
        LayeredValue raw = new("p",
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("v"), "/u"),
        ]);
        LayeredValueAdapter adapter = new(raw);
        Assert.True(adapter.IsDefinedAt(ConfigScopeAdapter.For(ConfigScope.User)));
        Assert.False(adapter.IsDefinedAt(ConfigScopeAdapter.For(ConfigScope.Local)));
        Assert.False(adapter.IsDefinedAt(ConfigScopeAdapter.For(ConfigScope.Project)));
    }

    [Fact]
    public void Path_ExposesLayeredJsonPath()
    {
        LayeredValue raw = new("permissions.allow", []);
        LayeredValueAdapter adapter = new(raw);
        Assert.Equal("permissions.allow", adapter.Path);
    }
}