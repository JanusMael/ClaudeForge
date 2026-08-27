using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Permissions;
using Bennewitz.Ninja.OpenCode.Sdk.Permissions;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// The second reader of the permission format — the one the editor uses — and the writer that
/// feeds it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>Two readers of one format is the pair that drifts.</b>
/// <see cref="OpenCodePermissionModel.Parse(JsonNode?)"/> reads what the config loader sees;
/// <see cref="OpenCodePermissionModel.FromValue"/> reads what the editor sees. They share the
/// action vocabulary and the action-only rejection, but not the traversal, so the tests that
/// matter here drive one corpus through both and compare — rather than testing each against its
/// own expectations, which is how two implementations stay individually green while disagreeing.
/// </para>
/// <para>
/// Deliberately no <c>JsonNode</c> in <see cref="OpenCodePermissionModel.FromValue"/>'s signature:
/// the value-currency conversion has no case for one, so a <c>JsonNode</c> returned to the editor
/// library is stringified and the whole permission block lands in the file as a quoted string.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodePermissionCurrencyTests
{
    /// <summary>
    /// Every shape worth cross-checking, as JSON. Includes both arms, a mixed object, and an
    /// action-only tool in its legal form.
    /// </summary>
    private static IEnumerable<string> Corpus()
    {
        yield return "\"ask\"";
        yield return "\"deny\"";
        yield return "{}";
        yield return "{\"bash\":\"allow\"}";
        yield return "{\"bash\":{\"*\":\"ask\",\"git *\":\"allow\",\"rm -rf *\":\"deny\"}}";
        yield return "{\"bash\":{\"npm *\":\"deny\",\"*\":\"ask\"},\"edit\":\"allow\","
                     + "\"websearch\":\"deny\"}";
        yield return "{\"edit\":{\"src/**\":\"allow\"},\"bash\":\"ask\"}";
    }

    /// <summary>Convert JSON to the value-currency shape the editor library speaks.</summary>
    /// <remarks>
    /// Hand-rolled rather than reached through the shell's <c>JsonCurrency</c>: this SDK must not
    /// reference an Avalonia assembly, and a five-line local conversion is a smaller price than
    /// the layering edge. The ordered-list backing is what makes the order assertions meaningful.
    /// </remarks>
    private static object? ToCurrency(JsonNode? node)
    {
        return node switch
        {
            null => null,
            JsonObject obj => new OrderedMap(obj.Select(
                p => new KeyValuePair<string, object?>(p.Key, ToCurrency(p.Value)))),
            JsonValue value when value.TryGetValue(out string? s) => s,
            var other => other.ToString(),
        };
    }

    [TestMethod]
    public void ParseAndFromValue_ProduceIdenticalModels()
    {
        foreach (string json in Corpus())
        {
            JsonNode node = JsonNode.Parse(json)!;

            OpenCodePermissionModel fromJson = OpenCodePermissionModel.Parse(node);
            OpenCodePermissionModel fromCurrency = OpenCodePermissionModel.FromValue(ToCurrency(node));

            Assert.AreEqual(
                fromJson.GlobalAction,
                fromCurrency.GlobalAction,
                $"Global arm disagreed for {json}.");

            CollectionAssert.AreEqual(
                fromJson.Tools.Select(t => t.Key).ToArray(),
                fromCurrency.Tools.Select(t => t.Key).ToArray(),
                $"Tool keys or their ORDER disagreed for {json}. Order is the policy here.");

            for (int i = 0; i < fromJson.Tools.Count; i++)
            {
                OpenCodeToolPermission a = fromJson.Tools[i].Value;
                OpenCodeToolPermission b = fromCurrency.Tools[i].Value;

                Assert.AreEqual(a.SingleAction, b.SingleAction, $"Single action disagreed for {json}.");
                CollectionAssert.AreEqual(
                    a.Rules.Select(r => $"{r.Pattern}={r.Action}").ToArray(),
                    b.Rules.Select(r => $"{r.Pattern}={r.Action}").ToArray(),
                    $"Rules or their ORDER disagreed for {json}.");
            }
        }
    }

    [TestMethod]
    public void FromValue_RejectsTheSameShapesParseRejects()
    {
        // A pattern object on an action-only tool.
        Assert.ThrowsExactly<FormatException>(() => OpenCodePermissionModel.FromValue(
            new OrderedMap([new KeyValuePair<string, object?>(
                "websearch",
                new OrderedMap([new KeyValuePair<string, object?>("*", "allow")]))])));

        // An action that is not one of the three.
        Assert.ThrowsExactly<FormatException>(() => OpenCodePermissionModel.FromValue(
            new OrderedMap([new KeyValuePair<string, object?>("bash", "maybe")])));

        // A value that is neither a string nor a map.
        Assert.ThrowsExactly<FormatException>(() => OpenCodePermissionModel.FromValue(42L));

        // A rule whose action is not even a string.
        Assert.ThrowsExactly<FormatException>(() => OpenCodePermissionModel.FromValue(
            new OrderedMap([new KeyValuePair<string, object?>(
                "bash",
                new OrderedMap([new KeyValuePair<string, object?>("*", true)]))])));
    }

    [TestMethod]
    public void FromValue_TreatsNullAsNoPermissionKey()
    {
        OpenCodePermissionModel model = OpenCodePermissionModel.FromValue(null);

        Assert.IsNull(model.GlobalAction);
        Assert.AreEqual(0, model.Tools.Count);
    }

    /// <remarks>
    /// Pinned against the literal strings rather than against <c>Parse</c>'s table, which is the
    /// same table. A round trip through both would stay green if the vocabulary were renamed on
    /// both sides at once — self-consistent and wrong, which is how a persisted format silently
    /// stops matching what is on users' disks.
    /// </remarks>
    [TestMethod]
    public void ToWireString_WritesTheStringsOpenCodeReads()
    {
        Assert.AreEqual("allow", OpenCodePermissionModel.ToWireString(PermissionOutcome.Allow));
        Assert.AreEqual("ask", OpenCodePermissionModel.ToWireString(PermissionOutcome.Ask));
        Assert.AreEqual("deny", OpenCodePermissionModel.ToWireString(PermissionOutcome.Deny));
    }

    [TestMethod]
    public void ToWireString_RefusesDefault()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => OpenCodePermissionModel.ToWireString(PermissionOutcome.Default),
            "Default means no rule matched. Writing a rule for it would invent a policy the user "
            + "never asked for — and it is the enum's zero value, so it is the one that arrives by "
            + "accident.");
    }

    [TestMethod]
    public void ShadowedRules_ReportThePositionsOfBothRules()
    {
        OpenCodePermissionModel model = OpenCodePermissionModel.Parse(
            JsonNode.Parse("{\"bash\":{\"npm *\":\"deny\",\"yarn *\":\"deny\",\"*\":\"ask\"}}"));

        IReadOnlyList<OpenCodeShadowedRule> shadowed = model.FindShadowedRules();

        Assert.AreEqual(2, shadowed.Count);
        Assert.AreEqual(0, shadowed[0].RuleIndex);
        Assert.AreEqual(2, shadowed[0].ShadowedByIndex);
        Assert.AreEqual(1, shadowed[1].RuleIndex);
        Assert.AreEqual(2, shadowed[1].ShadowedByIndex);
    }

    /// <remarks>
    /// Why the indices exist at all: nothing stops a permission object from repeating a pattern,
    /// and a caller mapping this report onto its own rows by pattern alone would flag the wrong
    /// one. The last duplicate is the rule that actually decides.
    /// </remarks>
    [TestMethod]
    public void ShadowedRules_DistinguishDuplicatePatternsByPosition()
    {
        OpenCodePermissionModel model = OpenCodePermissionModel.FromValue(
            new OrderedMap([new KeyValuePair<string, object?>("bash", new OrderedMap([
                new KeyValuePair<string, object?>("git *", "allow"),
            ]))]));

        // The parsed form cannot carry a duplicate key, so the duplicate case is constructed the
        // only way it can occur in practice: an editor holding two rows with the same pattern.
        Assert.AreEqual(1, model.Tools.Count);

        OpenCodePermissionModel duplicated = OpenCodePermissionModel.FromValue(
            new OrderedMap([new KeyValuePair<string, object?>("bash", new DuplicateKeyMap(
                [("git *", "allow"), ("git *", "deny")]))]));

        IReadOnlyList<OpenCodeShadowedRule> shadowed = duplicated.FindShadowedRules();

        Assert.AreEqual(1, shadowed.Count);
        Assert.AreEqual(0, shadowed[0].RuleIndex, "The first occurrence is the inert one.");
        Assert.AreEqual(1, shadowed[0].ShadowedByIndex);
    }

    /// <summary>An insertion-ordered map, standing in for the shell's currency map.</summary>
    private sealed class OrderedMap : IReadOnlyDictionary<string, object?>
    {
        private readonly List<KeyValuePair<string, object?>> _entries;

        public OrderedMap(IEnumerable<KeyValuePair<string, object?>> entries)
        {
            _entries = entries.ToList();
        }

        public object? this[string key] => _entries.First(e => e.Key == key).Value;

        public IEnumerable<string> Keys => _entries.Select(e => e.Key);

        public IEnumerable<object?> Values => _entries.Select(e => e.Value);

        public int Count => _entries.Count;

        public bool ContainsKey(string key) => _entries.Any(e => e.Key == key);

        public bool TryGetValue(string key, out object? value)
        {
            int index = _entries.FindIndex(e => e.Key == key);
            value = index < 0 ? null : _entries[index].Value;
            return index >= 0;
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _entries.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    /// <summary>
    /// A map that enumerates the same key twice — the state an editor is legitimately in when the
    /// user has two rows with the same pattern, and one JSON cannot express.
    /// </summary>
    private sealed class DuplicateKeyMap(IReadOnlyList<(string Key, object? Value)> entries)
        : IReadOnlyDictionary<string, object?>
    {
        public object? this[string key] => entries.First(e => e.Key == key).Value;

        public IEnumerable<string> Keys => entries.Select(e => e.Key);

        public IEnumerable<object?> Values => entries.Select(e => e.Value);

        public int Count => entries.Count;

        public bool ContainsKey(string key) => entries.Any(e => e.Key == key);

        public bool TryGetValue(string key, out object? value)
        {
            foreach ((string k, object? v) in entries)
            {
                if (k == key)
                {
                    value = v;
                    return true;
                }
            }

            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            entries.Select(e => new KeyValuePair<string, object?>(e.Key, e.Value)).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
