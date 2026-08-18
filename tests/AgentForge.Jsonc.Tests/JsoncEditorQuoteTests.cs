using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Jsonc;

namespace Bennewitz.Ninja.AgentForge.Jsonc.Tests;

/// <summary>
/// Pins <c>JsoncEditor.Quote</c> against <see cref="JsonSerializer"/> as an oracle.
/// <para>
/// <b>Why this exists.</b> <c>Quote</c> used to be <c>JsonSerializer.Serialize(name)</c> — the
/// reflection-based overload, which carries <c>RequiresUnreferencedCode</c> and fails the
/// Release publish with <c>IL2026</c> → <c>NETSDK1144</c> under <c>PublishTrimmed=true</c>.
/// It shipped on this branch from Phase 2 and no Debug build could see it; only the CI trim
/// check can, and CI had not run. It is now a <see cref="Utf8JsonWriter"/> — the same code
/// path the serializer uses internally for a string.
/// </para>
/// <para>
/// <b>Why the oracle is legitimate here.</b> These assertions deliberately call the
/// reflection-based overload the production code is forbidden from using. Test assemblies are
/// not trimmed, so the comparison is safe, and it is the only way to assert "identical to what
/// the serializer would have produced" rather than "looks about right".
/// </para>
/// </summary>
[TestClass]
public sealed class JsoncEditorQuoteTests
{
    /// <summary>
    /// Member names that exercise every escaping rule that differs between a naive
    /// <c>"\"" + name + "\""</c> and real JSON string encoding.
    /// </summary>
    private static IEnumerable<string[]> NastyNames()
    {
        yield return ["model"];                       // the ordinary case
        yield return [string.Empty];
        yield return ["with space"];
        yield return ["has\"quote"];
        yield return ["has\\backslash"];
        yield return ["has/slash"];                   // STJ escapes this, naive quoting does not
        yield return ["tab\there"];
        yield return ["newline\nhere"];
        yield return ["carriage\rreturn"];
        yield return ["null\0char"];                  // control character
        yield return ["bell"];
        yield return [""];                      // last C0 control
        yield return ["unicode-é-ü-ß"];
        yield return ["emoji-\U0001F600"];            // surrogate pair
        yield return ["cjk-日本語"];
        yield return ["rtl-‮override"];           // STJ escapes bidi controls
        yield return ["<script>"];                    // STJ escapes < and > by default
        yield return ["amp&ersand"];
        yield return ["single'quote"];
        yield return ["plus+equals="];
        yield return ["a".PadRight(200, 'x')];        // past the writer's initial buffer
    }

    [TestMethod]
    [DynamicData(nameof(NastyNames))]
    public void Quote_MatchesTheSerializerExactly(string name)
    {
        // The oracle: what the forbidden reflection-based overload would have written.
#pragma warning disable IL2026 // Test assemblies are not trimmed — see the class remarks.
        string expected = JsonSerializer.Serialize(name);
#pragma warning restore IL2026

        Assert.AreEqual(expected, JsoncEditor.Quote(name),
            $"Quote must escape exactly as the serializer does. Input: {name.Length} char(s).");
    }

    /// <summary>
    /// A lone surrogate is not valid UTF-16 and is the one input where a hand-rolled encoder
    /// is most likely to diverge — by throwing, or by emitting an invalid sequence, where the
    /// serializer substitutes U+FFFD. Asserted separately because it is a *behaviour* claim,
    /// not just another escaping case.
    /// </summary>
    [TestMethod]
    public void Quote_HandlesALoneSurrogate_TheSameWayTheSerializerDoes()
    {
        string loneHighSurrogate = "before\uD800after";

#pragma warning disable IL2026
        string expected = JsonSerializer.Serialize(loneHighSurrogate);
#pragma warning restore IL2026

        Assert.AreEqual(expected, JsoncEditor.Quote(loneHighSurrogate));
    }

    /// <summary>
    /// The result must always be a complete quoted JSON string, because callers interpolate it
    /// straight into <c>"{Quote(key)}: {rendered}"</c>. A bare escaped value with no surrounding
    /// quotes would produce syntactically invalid JSONC that only shows up on reload.
    /// </summary>
    [TestMethod]
    public void Quote_AlwaysReturnsAQuotedToken()
    {
        foreach (string[] row in NastyNames())
        {
            string quoted = JsoncEditor.Quote(row[0]);

            Assert.IsTrue(quoted.Length >= 2, $"'{row[0]}' produced '{quoted}'.");
            Assert.IsTrue(quoted.StartsWith('"'), $"'{row[0]}' produced '{quoted}'.");
            Assert.IsTrue(quoted.EndsWith('"'), $"'{row[0]}' produced '{quoted}'.");
        }
    }
}
