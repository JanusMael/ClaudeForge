using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Adapters;

/// <summary>
/// Key order through the value-currency layer, which for some configuration is meaning rather
/// than formatting.
/// </summary>
public sealed class OrderedPropertyMapTests
{
    /// <summary>
    /// The fixture deliberately declares keys in an order that is neither alphabetical nor
    /// reverse-alphabetical, so a re-sort in either direction is detectable.
    /// </summary>
    private const string PermissionJson =
        """{ "npm *": "deny", "*": "ask", "git *": "allow", "aaa *": "deny" }""";

    private static readonly string[] DeclaredOrder = ["npm *", "*", "git *", "aaa *"];

    [Fact]
    public void DeclaredOrder_IsNotSorted_SoThisFixtureCanDetectAReSort()
    {
        MessageAssert.SequenceNotEqual(
            DeclaredOrder.Order(StringComparer.Ordinal).ToArray(), DeclaredOrder,
            "Precondition: an alphabetical fixture cannot detect an alphabetical re-sort.");
        MessageAssert.SequenceNotEqual(
            DeclaredOrder.OrderDescending(StringComparer.Ordinal).ToArray(), DeclaredOrder,
            "Precondition: nor can a reverse-alphabetical one detect a reverse sort.");
    }

    [Fact]
    public void ReadingAJsonObject_PreservesKeyOrder()
    {
        object? value = JsonCurrency.FromJsonNode(JsonNode.Parse(PermissionJson));

        IReadOnlyDictionary<string, object?> map = (IReadOnlyDictionary<string, object?>)value!;
        MessageAssert.SequenceEqual(DeclaredOrder, map.Keys.ToArray(),
            "Reading a permission map must preserve key order — the LAST matching rule wins, so "
            + "reordering the keys rewrites the policy.");
    }

    [Fact]
    public void RoundTrippingThroughTheCurrencyLayer_PreservesKeyOrder()
    {
        object? value = JsonCurrency.FromJsonNode(JsonNode.Parse(PermissionJson));
        JsonNode? back = JsonCurrency.ToJsonNode(value);

        MessageAssert.SequenceEqual(
            DeclaredOrder,
            ((JsonObject)back!).Select(p => p.Key).ToArray(),
            "A full read/write round trip must return the keys in the order they arrived.");
    }

    /// <summary>
    /// Editing a value must not move its key. Otherwise changing one rule's action would
    /// re-order the policy around it.
    /// </summary>
    [Fact]
    public void ReplacingAValue_KeepsTheKeyInPlace()
    {
        OrderedPropertyMap map = new();
        map.Set("a", 1);
        map.Set("b", 2);
        map.Set("c", 3);

        map.Set("b", 99);

        MessageAssert.SequenceEqual(new[] { "a", "b", "c" }, map.Keys.ToArray(),
            "Replacing b's value moved its key. Editing a rule's action would then change which "
            + "rule wins.");
        Assert.Equal(99, map["b"]);
    }

    [Fact]
    public void NestedObjects_PreserveOrderToo()
    {
        object? value = JsonCurrency.FromJsonNode(
            JsonNode.Parse($$"""{ "permission": { "bash": {{PermissionJson}} } }"""));

        var outer = (IReadOnlyDictionary<string, object?>)value!;
        var permission = (IReadOnlyDictionary<string, object?>)outer["permission"]!;
        var bash = (IReadOnlyDictionary<string, object?>)permission["bash"]!;

        MessageAssert.SequenceEqual(DeclaredOrder, bash.Keys.ToArray(),
            "The real permission map is nested two levels down; order must survive the recursion.");
    }

    [Fact]
    public void LookupIsCaseSensitive_MatchingTheRestOfTheConfigHandling()
    {
        OrderedPropertyMap map = new();
        map.Set("Bash", 1);

        Assert.True(map.ContainsKey("Bash"));
        Assert.False(map.ContainsKey("bash"),
            "Ordinal comparison: 'bash' and 'Bash' are different tools, and case-folding would "
            + "silently merge two rule sets.");
    }

    /// <summary>
    /// The map a JSON object reads into must be one that GUARANTEES order, not one that happens
    /// to preserve it.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>This asserts a type, which is normally the wrong thing to test — and here it is the
    /// only thing that can be tested.</b> Reverting <c>FromObject</c> to a plain
    /// <see cref="Dictionary{TKey,TValue}"/> leaves every behavioural test in this class green,
    /// because a dictionary with no removals does yield insertion order at runtime today. That
    /// was verified by canary, not assumed.
    /// <para>
    /// So the difference between the two implementations is not observable by enumerating them;
    /// it is the difference between a documented guarantee and an unspecified implementation
    /// detail that is free to change with a removal, a capacity change, or a runtime update. The
    /// only way to hold the guarantee is to assert that the guaranteeing type is the one in use.
    /// </para>
    /// </remarks>
    [Fact]
    public void ReadingAJsonObject_YieldsAMapThatGuaranteesOrder()
    {
        object? value = JsonCurrency.FromJsonNode(JsonNode.Parse(PermissionJson));

        MessageAssert.IsAssignableFrom<OrderedPropertyMap>(value,
            "A JSON object must read into OrderedPropertyMap. A plain Dictionary passes every "
            + "other test in this class while promising nothing about order — and for a "
            + "permission map, order is the policy.");
    }
}
