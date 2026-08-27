using System.Text.Json;
using Bennewitz.Ninja.OpenCode.Sdk.Keybinds;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// Every shape claim the <c>keybinds</c> model documents, asserted against the bundled TUI schema.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>Phase 13 exists to refresh these schemas, and this editor hardcodes more schema knowledge
/// than any other in the plan</b> — four union arms, two object arms told apart by which key they
/// carry, two enum literals, and the fact that <c>true</c> is not admitted. A refresh that changed
/// any of it would leave the editor confidently writing last release's shape, and every one of those
/// failures is silent: the user's file is simply rejected elsewhere, with the editor insisting it is
/// fine.
/// </para>
/// <para>
/// ⭐ These are drift guards, not behaviour tests. <b>Six of the nine previous slices found the
/// plan's description of a shape wrong or incomplete</b>, which is the whole argument for pinning
/// claims to the file rather than to a comment. The claim these tests exist to protect hardest is
/// the one the plan elided behind an ellipsis: the event object and the array arm.
/// </para>
/// <para>
/// The schema is read from the source tree rather than through <c>BundledResource</c>, which is
/// internal to <c>AgentForge.Core</c>. Reading the file that is embedded is equivalent for drift
/// purposes, and the embedding itself is asserted separately.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeKeybindSchemaDriftTests
{
    /// <summary>What the model and the editor are built against.</summary>
    private const int DeclaredActionCount = 184;

    private static string SchemaPath()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(
                dir, "src", "AgentForge.Core", "Assets", "Schemas", "opencode-tui.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate the bundled OpenCode TUI schema from '{AppContext.BaseDirectory}'.");
    }

    private static JsonDocument Schema() => JsonDocument.Parse(File.ReadAllText(SchemaPath()));

    private static JsonElement Keybinds(JsonDocument doc) =>
        doc.RootElement.GetProperty("properties").GetProperty("keybinds");

    /// <summary>One action's value schema — <c>app_exit</c> stands for all 184.</summary>
    /// <remarks>
    /// Safe because <see cref="EveryAction_DeclaresTheIdenticalShape"/> proves they are the same
    /// shape. Without that test this helper would be sampling and calling it surveying.
    /// </remarks>
    private static JsonElement OneAction(JsonDocument doc) =>
        Keybinds(doc).GetProperty("properties").GetProperty("app_exit");

    private static IReadOnlyList<JsonElement> Arms(JsonElement value) =>
        [.. value.GetProperty("anyOf").EnumerateArray()];

    private static string[] PropertyNames(JsonElement obj) =>
        [.. obj.GetProperty("properties").EnumerateObject().Select(p => p.Name)];

    private static string[] StringArray(JsonElement array) =>
        [.. array.EnumerateArray().Select(e => e.GetString() ?? string.Empty)];

    // ── The container ──────────────────────────────────────────────────────

    [TestMethod]
    public void TheContainer_IsAClosedObjectOf184OptionalActions()
    {
        using JsonDocument doc = Schema();
        JsonElement keybinds = Keybinds(doc);

        Assert.AreEqual("object", keybinds.GetProperty("type").GetString());
        Assert.AreEqual(
            DeclaredActionCount,
            keybinds.GetProperty("properties").EnumerateObject().Count(),
            "The action count moved. The editor's row list and its tests are built against it.");

        Assert.IsTrue(
            keybinds.TryGetProperty("additionalProperties", out JsonElement extra)
                && extra.ValueKind == JsonValueKind.False,
            "`keybinds` no longer forbids unknown actions, so an unknown name is no longer a "
                + "violation and the editor's warning about one is now wrong.");

        Assert.IsFalse(
            keybinds.TryGetProperty("required", out _),
            "An action became required. Every row currently starts as 'not set' and writes nothing.");
    }

    /// <remarks>
    /// ⭐ <b>This is what makes one row template correct for all 184.</b> If the shapes ever diverge,
    /// a single row control would render some actions wrongly, and the divergence would be invisible
    /// because the common case still worked.
    /// </remarks>
    [TestMethod]
    public void EveryAction_DeclaresTheIdenticalShape()
    {
        using JsonDocument doc = Schema();

        string? canonical = null;
        string canonicalName = string.Empty;

        foreach (JsonProperty action in Keybinds(doc).GetProperty("properties").EnumerateObject())
        {
            // `description` is per-action by design — it is the row's label. Everything else must
            // match, so it is compared with the description stripped out.
            string shape = JsonSerializer.Serialize(
                action.Value.EnumerateObject()
                    .Where(p => p.Name != "description")
                    .ToDictionary(p => p.Name, p => p.Value));

            if (canonical is null)
            {
                canonical = shape;
                canonicalName = action.Name;
                continue;
            }

            Assert.AreEqual(
                canonical,
                shape,
                $"`{action.Name}` no longer has the same shape as `{canonicalName}`, so one row "
                    + "template can no longer serve every action.");
        }
    }

    /// <remarks>
    /// ⚠⚠ <b>Zero of the 184 actions declares a <c>default</c>.</b> Checked, not assumed — and it is
    /// the reason the editor shows an untouched action as "not set" rather than as whatever OpenCode
    /// would do on its own. Showing a default this file does not state would be inventing one; if a
    /// refresh ever adds them, this failing test is the invitation to surface them for real.
    /// </remarks>
    [TestMethod]
    public void NoAction_DeclaresADefault()
    {
        using JsonDocument doc = Schema();

        List<string> withDefaults = [];
        foreach (JsonProperty action in Keybinds(doc).GetProperty("properties").EnumerateObject())
        {
            if (action.Value.TryGetProperty("default", out _))
            {
                withDefaults.Add(action.Name);
            }
        }

        Assert.AreEqual(
            0,
            withDefaults.Count,
            $"The schema now states defaults ({string.Join(", ", withDefaults.Take(5))}…). The "
                + "editor claims none exist and shows unset rows as 'not set'.");
    }

    /// <remarks>
    /// Every action carries a description, which is the row's label and the text search matches on.
    /// A missing one would render a blank row that nothing could find.
    /// </remarks>
    [TestMethod]
    public void EveryAction_CarriesADescription()
    {
        using JsonDocument doc = Schema();

        foreach (JsonProperty action in Keybinds(doc).GetProperty("properties").EnumerateObject())
        {
            Assert.IsTrue(
                action.Value.TryGetProperty("description", out JsonElement text)
                    && !string.IsNullOrWhiteSpace(text.GetString()),
                $"`{action.Name}` has no description, so its row would have no label.");
        }
    }

    // ── The four top-level arms ────────────────────────────────────────────

    /// <remarks>
    /// ⛔ <b>The plan describes this union as
    /// <c>false | "none" | string | {name,ctrl,shift,meta,super,hyper} | …</c>, and the two shapes
    /// behind that ellipsis are the event object and the array — the two that decide the model.</b>
    /// This test pins all four arms so the elision cannot be inherited as fact.
    /// </remarks>
    [TestMethod]
    public void AnActionValue_HasExactlyFourArms_InTheDocumentedOrder()
    {
        using JsonDocument doc = Schema();
        IReadOnlyList<JsonElement> arms = Arms(OneAction(doc));

        Assert.AreEqual(4, arms.Count, "The arm count moved; the mode enum is built against it.");

        Assert.AreEqual("boolean", arms[0].GetProperty("type").GetString());
        Assert.AreEqual("string", arms[1].GetProperty("type").GetString());
        Assert.IsTrue(
            arms[2].TryGetProperty("anyOf", out _),
            "The third arm is no longer a nested union of binding forms.");
        Assert.AreEqual("array", arms[3].GetProperty("type").GetString());
    }

    /// <remarks>
    /// ⛔⛔ <b>The boolean arm is <c>enum: [false]</c>, so the literal <c>true</c> is NOT valid.</b>
    /// This is the claim most likely to look like a bug and be "fixed": reading <c>true</c> as
    /// "enabled" would invent an arm the schema does not have, and the editor would then offer a
    /// value OpenCode rejects.
    /// </remarks>
    [TestMethod]
    public void TheBooleanArm_AdmitsOnlyFalse()
    {
        using JsonDocument doc = Schema();
        JsonElement enumeration = Arms(OneAction(doc))[0].GetProperty("enum");

        Assert.AreEqual(1, enumeration.GetArrayLength());
        Assert.AreEqual(
            JsonValueKind.False,
            enumeration[0].ValueKind,
            "`true` is now admitted, so the editor needs an arm for it.");
    }

    /// <remarks>
    /// The literal the codec matches <see cref="StringComparison.Ordinal"/>. Pinned so a schema that
    /// renamed or widened it cannot leave the codec matching a string that no longer means anything.
    /// </remarks>
    [TestMethod]
    public void TheStringArm_AdmitsExactlyTheNoneLiteralTheCodecMatches()
    {
        using JsonDocument doc = Schema();
        string[] admitted = StringArray(Arms(OneAction(doc))[1].GetProperty("enum"));

        CollectionAssert.AreEqual(
            new[] { OpenCodeKeybindCodec.NoneLiteral },
            admitted,
            "The literal string arm changed; the codec matches `NoneLiteral` and nothing else.");
    }

    /// <remarks>
    /// ⭐ <b>The chord arm declares no <c>pattern</c>, which is the whole justification for the
    /// capture control writing the OBJECT form.</b> With no grammar in the schema, turning a captured
    /// keystroke into text would mean choosing between <c>"ctrl+x"</c>, <c>"C-x"</c> and
    /// <c>"ctrl-x"</c> on no evidence and writing the guess into the user's file. If a refresh ever
    /// adds a pattern, this test failing is the signal that a string chord can finally be validated —
    /// and generated.
    /// </remarks>
    [TestMethod]
    public void TheChordArm_IsAnUnconstrainedString()
    {
        using JsonDocument doc = Schema();
        JsonElement chord = Arms(Arms(OneAction(doc))[2])[0];

        Assert.AreEqual("string", chord.GetProperty("type").GetString());
        Assert.IsFalse(
            chord.TryGetProperty("pattern", out _),
            "The chord arm now constrains its text, so this SDK could validate one.");
        Assert.IsFalse(
            chord.TryGetProperty("enum", out _),
            "The chord arm is now a closed list, so the editor could offer a picker.");
    }

    /// <remarks>
    /// ⚠ The array arm carries no <c>minItems</c> / <c>maxItems</c>, deliberately unlike
    /// <c>plugin[]</c>'s tuple arm, which pins both to 2. So an empty array is legal, which is why
    /// the codec writes <c>[]</c> for an empty sequence instead of treating it as nothing.
    /// </remarks>
    [TestMethod]
    public void TheArrayArm_IsUnboundedAndHoldsTheSameInnerUnion()
    {
        using JsonDocument doc = Schema();
        JsonElement array = Arms(OneAction(doc))[3];

        Assert.IsFalse(array.TryGetProperty("minItems", out _), "The array arm gained a minimum.");
        Assert.IsFalse(array.TryGetProperty("maxItems", out _), "The array arm gained a maximum.");
        Assert.IsFalse(
            array.TryGetProperty("prefixItems", out _),
            "The array arm became a positional tuple, which changes what an element means.");

        // Its items must be the same three-arm union the single form uses, or the editor's one
        // binding control cannot serve both the single and sequence modes.
        Assert.AreEqual(
            JsonSerializer.Serialize(Arms(OneAction(doc))[2]),
            JsonSerializer.Serialize(array.GetProperty("items")),
            "An array element no longer has the same shape as a single binding.");
    }

    /// <remarks>
    /// The inner union's three arms in the order the <c>OpenCodeBindingForm</c> members mirror.
    /// </remarks>
    [TestMethod]
    public void TheInnerUnion_HasExactlyThreeBindingForms()
    {
        using JsonDocument doc = Schema();
        IReadOnlyList<JsonElement> forms = Arms(Arms(OneAction(doc))[2]);

        Assert.AreEqual(3, forms.Count, "The binding-form count moved.");
        Assert.AreEqual("string", forms[0].GetProperty("type").GetString());
        Assert.AreEqual("object", forms[1].GetProperty("type").GetString());
        Assert.AreEqual("object", forms[2].GetProperty("type").GetString());
    }

    // ── The two object arms, and how they are told apart ───────────────────

    [TestMethod]
    public void TheKeyObject_IsClosed_AndRequiresName()
    {
        using JsonDocument doc = Schema();
        JsonElement key = Arms(Arms(OneAction(doc))[2])[1];

        CollectionAssert.AreEqual(
            new[] { "name", "ctrl", "shift", "meta", "super", "hyper" },
            PropertyNames(key),
            "The key object's fields changed; the editor renders exactly these.");

        CollectionAssert.AreEqual(new[] { "name" }, StringArray(key.GetProperty("required")));

        Assert.IsTrue(
            key.TryGetProperty("additionalProperties", out JsonElement extra)
                && extra.ValueKind == JsonValueKind.False,
            "The key object now permits unknown fields, so preserving them is courtesy rather than "
                + "the correctness the model's remarks claim.");

        foreach (string modifier in new[] { "ctrl", "shift", "meta", "super", "hyper" })
        {
            Assert.AreEqual(
                "boolean",
                key.GetProperty("properties").GetProperty(modifier).GetProperty("type").GetString(),
                $"`{modifier}` is no longer a plain boolean.");
        }
    }

    /// <remarks>
    /// ⛔⛔ <b>The event object does NOT set <c>additionalProperties: false</c>, unlike the key object
    /// beside it.</b> This is the asymmetry the model's remarks turn on, and it is exactly the kind
    /// of detail that reads as an oversight and gets "corrected" into a bug — so it is asserted
    /// rather than described. It means an unknown field here is legal, making preservation
    /// correctness rather than courtesy.
    /// </remarks>
    [TestMethod]
    public void TheEventObject_IsOpen_AndRequiresKey()
    {
        using JsonDocument doc = Schema();
        JsonElement wrapper = Arms(Arms(OneAction(doc))[2])[2];

        CollectionAssert.AreEqual(
            new[] { "key", "event", "preventDefault", "fallthrough" },
            PropertyNames(wrapper),
            "The event object's fields changed; the editor renders exactly these.");

        CollectionAssert.AreEqual(new[] { "key" }, StringArray(wrapper.GetProperty("required")));

        Assert.IsFalse(
            wrapper.TryGetProperty("additionalProperties", out _),
            "The event object now declares additionalProperties. If it forbids them, an unknown "
                + "field there became a violation and the model's remarks are wrong about why it "
                + "is preserved.");
    }

    /// <remarks>
    /// ⭐ <b>The discriminator the codec uses, pinned in both directions.</b> <c>key</c> is required
    /// by the event arm and forbidden by the key arm; <c>name</c> is required by the key arm and not
    /// declared on the event arm. That is what makes telling them apart unambiguous rather than a
    /// guess about which fields dominate — and if a refresh ever adds <c>name</c> to the event
    /// object, the codec's discriminator silently starts misreading bindings.
    /// </remarks>
    [TestMethod]
    public void TheTwoObjectArms_AreToldApartByKeyAndName_InBothDirections()
    {
        using JsonDocument doc = Schema();
        IReadOnlyList<JsonElement> forms = Arms(Arms(OneAction(doc))[2]);
        string[] keyFields = PropertyNames(forms[1]);
        string[] eventFields = PropertyNames(forms[2]);

        Assert.IsTrue(keyFields.Contains("name"), "The key arm no longer declares `name`.");
        Assert.IsFalse(
            keyFields.Contains("key"),
            "The key arm now declares `key`, so the codec's discriminator is ambiguous.");

        Assert.IsTrue(eventFields.Contains("key"), "The event arm no longer declares `key`.");
        Assert.IsFalse(
            eventFields.Contains("name"),
            "The event arm now declares `name`, so the codec's discriminator is ambiguous.");
    }

    /// <remarks>
    /// The event wrapper's <c>key</c> is itself <c>string | key-object</c> — the same choice the
    /// outer union offers. That is why one key control serves both places.
    /// </remarks>
    [TestMethod]
    public void TheEventObjectsKey_IsTheSameTwoArmsAsTheOuterUnion()
    {
        using JsonDocument doc = Schema();
        IReadOnlyList<JsonElement> forms = Arms(Arms(OneAction(doc))[2]);
        IReadOnlyList<JsonElement> inner = Arms(forms[2].GetProperty("properties").GetProperty("key"));

        Assert.AreEqual(2, inner.Count);
        Assert.AreEqual(JsonSerializer.Serialize(forms[0]), JsonSerializer.Serialize(inner[0]));
        Assert.AreEqual(
            JsonSerializer.Serialize(forms[1]),
            JsonSerializer.Serialize(inner[1]),
            "The event wrapper's key object drifted from the standalone one, so one control can no "
                + "longer serve both.");
    }

    /// <remarks>
    /// The codec's <c>EventLiterals</c> is hardcoded, and the editor offers exactly those two as
    /// selectable values. Drift in either direction matters: a third literal would be unofferable,
    /// and a removed one would stay on offer.
    /// </remarks>
    [TestMethod]
    public void TheEventEnum_MatchesTheCodecsLiterals()
    {
        using JsonDocument doc = Schema();
        JsonElement wrapper = Arms(Arms(OneAction(doc))[2])[2];

        CollectionAssert.AreEqual(
            OpenCodeKeybindCodec.EventLiterals.ToArray(),
            StringArray(wrapper.GetProperty("properties").GetProperty("event").GetProperty("enum")),
            "The `event` enum drifted from OpenCodeKeybindCodec.EventLiterals.");
    }
}
