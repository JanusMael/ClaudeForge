using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.OpenCode.Sdk.Keybinds;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// The TUI's <c>keybinds</c> value: 184 actions, each a four-arm union nested three deep.
/// </summary>
/// <remarks>
/// <para>
/// The interesting behaviour is almost entirely about what this codec REFUSES to fold. Four pairs
/// look interchangeable and are not: <c>false</c> versus <c>"none"</c>, <c>"x"</c> versus
/// <c>["x"]</c>, absent versus <c>{}</c>, and a modifier that is <c>false</c> versus one that is
/// absent. Each pair has the same effect and a different text, and this phase's rule is that an
/// editor never rewrites one spelling into another.
/// </para>
/// <para>
/// ⭐ The schema-drift tests at the bottom are the ones that matter most over time: every shape
/// claim in the model's documentation is asserted against the bundled schema, in both directions
/// where that is meaningful. Six of the nine previous slices found the plan's description of a shape
/// wrong, so a claim that is only written in a comment is a claim nothing checks.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeKeybindCodecTests
{
    // ── The container ──────────────────────────────────────────────────────

    [TestMethod]
    public void AnAbsentKey_IsNotDefined_AndWritesNothing()
    {
        OpenCodeKeybindConfig config = OpenCodeKeybindCodec.Read(null, isDefined: false);

        Assert.IsFalse(config.IsDefined);
        Assert.AreEqual(0, config.Entries.Count);
        Assert.IsNull(OpenCodeKeybindCodec.Write(config));
    }

    /// <remarks>
    /// ⚠ An absent <c>keybinds</c> and an empty <c>keybinds: {}</c> change no binding either way, so
    /// folding them looks free. It is not: one is a key the user never wrote, the other is one they
    /// did, and rewriting either into the other turns opening a settings page into a diff.
    /// </remarks>
    [TestMethod]
    public void AnEmptyObject_IsNotTheSameAsAnAbsentKey()
    {
        OpenCodeKeybindConfig absent = OpenCodeKeybindCodec.Read(null, isDefined: false);
        OpenCodeKeybindConfig empty = OpenCodeKeybindCodec.Read(
            CurrencyText.Parse("{}"), isDefined: true);

        Assert.IsFalse(absent.IsDefined);
        Assert.IsTrue(empty.IsDefined);
        Assert.IsNull(OpenCodeKeybindCodec.Write(absent));
        Assert.AreEqual("{}", CurrencyText.Render(OpenCodeKeybindCodec.Write(empty)));
    }

    /// <remarks>
    /// ⚠ <c>keybinds</c> declares <c>additionalProperties: false</c>, so an unknown action name is a
    /// violation — which is why it is preserved. The ordinary way to meet one is a config written by
    /// a newer OpenCode that has since added the action.
    /// </remarks>
    [TestMethod]
    public void AnUnknownActionName_IsFlaggedAndKept()
    {
        HashSet<string> known = ["app_exit"];

        OpenCodeKeybindConfig config = OpenCodeKeybindCodec.Read(
            CurrencyText.Parse("""{"app_exit":"ctrl+q","future_action":"ctrl+z"}"""),
            isDefined: true,
            known);

        Assert.AreEqual(2, config.Entries.Count);
        Assert.IsTrue(config.Entries[0].IsKnown);
        Assert.IsFalse(config.Entries[1].IsKnown, "The unknown action was not flagged.");
        Assert.AreEqual(
            """{app_exit:ctrl+q,future_action:ctrl+z}""",
            CurrencyText.Render(OpenCodeKeybindCodec.Write(config)),
            "An action this build does not know about was dropped on save.");
    }

    /// <remarks>
    /// A whole value that is not an object at all — the outermost of the three opaque arms.
    /// </remarks>
    [TestMethod]
    public void AWholeValueThatIsNotAnObject_IsHeldVerbatim()
    {
        OpenCodeKeybindConfig config = OpenCodeKeybindCodec.Read("nonsense", isDefined: true);

        Assert.IsTrue(config.IsOpaque);
        Assert.AreEqual("nonsense", OpenCodeKeybindCodec.Write(config));
    }

    /// <remarks>
    /// ⚠⚠ <b>The single most important property of the container.</b> The editor shows 184 rows and
    /// the user will have touched a handful. If an untouched row wrote anything at all, opening the
    /// page and saving would produce a 184-key diff.
    /// </remarks>
    [TestMethod]
    public void OnlyActionsThatAreSet_AreWritten()
    {
        OpenCodeKeybindConfig config = new()
        {
            IsDefined = true,
            Entries =
            [
                new("app_exit", new OpenCodeKeybindValue { Mode = OpenCodeKeybindMode.NotSet }),
                new("help_show", Bound("ctrl+h")),
                new("app_debug", new OpenCodeKeybindValue { Mode = OpenCodeKeybindMode.NotSet }),
            ],
        };

        Assert.AreEqual(
            "{help_show:ctrl+h}",
            CurrencyText.Render(OpenCodeKeybindCodec.Write(config)));
    }

    /// <remarks>
    /// ⭐ Action order is the caller's, deliberately unlike the <c>mcp</c> and <c>formatter</c>
    /// codecs, which write an entry's keys in schema order. Six keys carry no meaning; 184 do —
    /// reordering them turns a one-line change into an unreviewable diff.
    /// </remarks>
    [TestMethod]
    public void ActionOrder_IsPreservedExactly()
    {
        const string Json = """{"help_show":"f1","app_exit":"ctrl+q","app_debug":"f12"}""";

        OpenCodeKeybindConfig config = OpenCodeKeybindCodec.Read(
            CurrencyText.Parse(Json), isDefined: true);

        Assert.AreEqual(
            "{help_show:f1,app_exit:ctrl+q,app_debug:f12}",
            CurrencyText.Render(OpenCodeKeybindCodec.Write(config)));
    }

    /// <remarks>
    /// The container's map type is asserted directly. A canary in 9a-2 proved that swapping a plain
    /// <c>Dictionary</c> in leaves every behavioural order test green, so no behavioural assertion
    /// can hold this guarantee — only a type assertion can.
    /// </remarks>
    [TestMethod]
    public void EveryLevel_EmitsAnOrderedMap()
    {
        object? written = OpenCodeKeybindCodec.Write(new OpenCodeKeybindConfig
        {
            IsDefined = true,
            Entries =
            [
                new("app_exit", new OpenCodeKeybindValue
                {
                    Mode = OpenCodeKeybindMode.Bound,
                    Bindings =
                    [
                        new OpenCodeKeyBinding
                        {
                            Form = OpenCodeBindingForm.Event,
                            Key = new OpenCodeKeySpec { Name = "q", Ctrl = true },
                            Event = "press",
                        },
                    ],
                }),
            ],
        });

        Assert.IsInstanceOfType<OrderedPropertyMap>(written, "The container is not ordered.");

        object? binding = ((IReadOnlyDictionary<string, object?>)written!)["app_exit"];
        Assert.IsInstanceOfType<OrderedPropertyMap>(binding, "The event object is not ordered.");

        object? key = ((IReadOnlyDictionary<string, object?>)binding!)["key"];
        Assert.IsInstanceOfType<OrderedPropertyMap>(key, "The key object is not ordered.");
    }

    // ── The four arms of one action's value ────────────────────────────────

    /// <remarks>
    /// ⛔ <b>The boolean arm is <c>enum: [false]</c> — the literal <c>true</c> is NOT admitted.</b>
    /// So there is no "enabled" mode to read <c>true</c> into, and it lands on the opaque arm rather
    /// than being read as the opposite of <c>false</c>.
    /// </remarks>
    [TestMethod]
    public void False_IsDisabled_ButTrue_IsNotAnArmAtAll()
    {
        OpenCodeKeybindValue off = OpenCodeKeybindCodec.ReadValue(false, isDefined: true);
        OpenCodeKeybindValue on = OpenCodeKeybindCodec.ReadValue(true, isDefined: true);

        Assert.AreEqual(OpenCodeKeybindMode.Disabled, off.Mode);
        Assert.AreEqual(false, OpenCodeKeybindCodec.WriteValue(off));

        Assert.AreEqual(
            OpenCodeKeybindMode.Unrecognised,
            on.Mode,
            "`true` is not in the schema's enum and must not be read as an arm.");
        Assert.AreEqual(true, OpenCodeKeybindCodec.WriteValue(on));
    }

    /// <remarks>
    /// ⚠ <c>false</c> and <c>"none"</c> plainly do the same thing — the action is unbound. They are
    /// still two different texts, and the rule is that an editor never rewrites one into the other.
    /// </remarks>
    [TestMethod]
    public void FalseAndNone_AreDifferentStates()
    {
        OpenCodeKeybindValue off = OpenCodeKeybindCodec.ReadValue(false, isDefined: true);
        OpenCodeKeybindValue none = OpenCodeKeybindCodec.ReadValue("none", isDefined: true);

        Assert.AreNotEqual(off.Mode, none.Mode);
        Assert.AreEqual(false, OpenCodeKeybindCodec.WriteValue(off));
        Assert.AreEqual("none", OpenCodeKeybindCodec.WriteValue(none));
    }

    /// <remarks>
    /// ⚠⚠ Matched <see cref="StringComparison.Ordinal"/>, so <c>"None"</c> is a chord named
    /// <c>"None"</c> and not the literal. That reading is what the schema says, and the alternative
    /// would silently rewrite the user's text on the next save. Same discipline as
    /// <c>autoupdate</c>'s <c>"Notify"</c>.
    /// </remarks>
    [TestMethod]
    public void TheNoneLiteral_IsCaseSensitive()
    {
        OpenCodeKeybindValue value = OpenCodeKeybindCodec.ReadValue("None", isDefined: true);

        Assert.AreEqual(OpenCodeKeybindMode.Bound, value.Mode);
        Assert.AreEqual("None", OpenCodeKeybindCodec.WriteValue(value));
    }

    /// <remarks>
    /// ⚠⚠⚠ <b>The trap this shape shares with <c>plugin[]</c>'s bare-versus-tuple arms — the fourth
    /// time this phase has met it.</b> A one-element array is the array arm, not the single arm.
    /// Unwrapping it looks like tidying and is a silent move to the other arm of a union.
    /// </remarks>
    [TestMethod]
    public void AOneElementArray_StaysAnArray()
    {
        OpenCodeKeybindValue single = OpenCodeKeybindCodec.ReadValue("ctrl+q", isDefined: true);
        OpenCodeKeybindValue wrapped = OpenCodeKeybindCodec.ReadValue(
            CurrencyText.Parse("""["ctrl+q"]"""), isDefined: true);

        Assert.AreEqual(OpenCodeKeybindMode.Bound, single.Mode);
        Assert.AreEqual(OpenCodeKeybindMode.Sequence, wrapped.Mode);

        Assert.AreEqual("ctrl+q", CurrencyText.Render(OpenCodeKeybindCodec.WriteValue(single)));
        Assert.AreEqual("[ctrl+q]", CurrencyText.Render(OpenCodeKeybindCodec.WriteValue(wrapped)));
    }

    /// <remarks>
    /// ⚠ The deliberate exception to this phase's "empty writes nothing" rule, and the same one the
    /// <c>formatter</c> codec makes for <c>{}</c>: the empty array is what choosing the list form
    /// <i>means</i>, so collapsing it to a removal would undo the only thing that choice did.
    /// </remarks>
    [TestMethod]
    public void AnEmptySequence_WritesAnEmptyArray_NotNothing()
    {
        OpenCodeKeybindValue value = OpenCodeKeybindCodec.ReadValue(
            CurrencyText.Parse("[]"), isDefined: true);

        Assert.AreEqual(OpenCodeKeybindMode.Sequence, value.Mode);
        Assert.AreEqual(0, value.Bindings.Count);
        Assert.AreEqual("[]", CurrencyText.Render(OpenCodeKeybindCodec.WriteValue(value)));
    }

    /// <remarks>
    /// The one path where the codec drops a value, stated rather than discovered: a
    /// <see cref="OpenCodeKeybindMode.Bound"/> value holding no binding cannot express itself in
    /// that arm. The editor cannot produce it — a row in that mode always owns exactly one binding —
    /// but the SDK contract should say what happens rather than leave it to be found.
    /// </remarks>
    [TestMethod]
    public void ABoundValueWithNoBinding_WritesNothing()
    {
        OpenCodeKeybindValue value = new() { Mode = OpenCodeKeybindMode.Bound, Bindings = [] };

        Assert.IsNull(OpenCodeKeybindCodec.WriteValue(value));
    }

    /// <remarks>
    /// Switching away from a bound value and back must not destroy it — the preserve-the-other-arm
    /// rule, which this phase has now needed for permission, MCP, plugin, formatter/lsp and here.
    /// </remarks>
    [TestMethod]
    public void SwitchingModeAway_KeepsTheBindingsForSwitchingBack()
    {
        OpenCodeKeybindValue bound = OpenCodeKeybindCodec.ReadValue("ctrl+q", isDefined: true);
        OpenCodeKeybindValue off = bound with { Mode = OpenCodeKeybindMode.Disabled };

        Assert.AreEqual(false, OpenCodeKeybindCodec.WriteValue(off));
        Assert.AreEqual(
            1,
            off.Bindings.Count,
            "The binding was destroyed by a mode switch, so switching back cannot restore it.");
        Assert.AreEqual(
            "ctrl+q",
            CurrencyText.Render(
                OpenCodeKeybindCodec.WriteValue(off with { Mode = OpenCodeKeybindMode.Bound })));
    }

    // ── The inner three-arm union ──────────────────────────────────────────

    /// <remarks>
    /// ⭐ The string arm declares <b>no <c>pattern</c></b>, so every string validates and the chord
    /// grammar lives entirely in OpenCode's parser. This SDK therefore has nothing to validate
    /// against and holds what it was given, however odd it looks.
    /// </remarks>
    [TestMethod]
    public void AChordString_IsHeldExactlyAsWritten()
    {
        string[] chords = ["ctrl+q", "<leader>e", "C-x C-s", "  spaced  ", "🙂"];

        foreach (string chord in chords)
        {
            OpenCodeKeybindValue value = OpenCodeKeybindCodec.ReadValue(chord, isDefined: true);

            Assert.AreEqual(OpenCodeBindingForm.Chord, value.Bindings[0].Form);
            Assert.AreEqual(
                chord,
                OpenCodeKeybindCodec.WriteValue(value),
                $"The chord {JsonSerializer.Serialize(chord)} was altered.");
        }
    }

    [TestMethod]
    public void AKeyObject_RoundTripsWithItsModifiers()
    {
        const string Json = """{"name":"q","ctrl":true,"shift":false}""";

        OpenCodeKeybindValue value = OpenCodeKeybindCodec.ReadValue(
            CurrencyText.Parse(Json), isDefined: true);

        Assert.AreEqual(OpenCodeBindingForm.Key, value.Bindings[0].Form);
        Assert.AreEqual("q", value.Bindings[0].Key!.Name);
        Assert.AreEqual(true, value.Bindings[0].Key!.Ctrl);
        Assert.AreEqual(false, value.Bindings[0].Key!.Shift);
        Assert.IsNull(value.Bindings[0].Key!.Meta);

        Assert.AreEqual(
            "{name:q,ctrl:true,shift:false}",
            CurrencyText.Render(OpenCodeKeybindCodec.WriteValue(value)));
    }

    /// <remarks>
    /// ⚠ A modifier stated as <c>false</c> and one left out are different files. Same
    /// absent-versus-<c>false</c> distinction that made <c>disabled</c> a three-state checkbox in
    /// the <c>lsp</c> editor, one level further in.
    /// </remarks>
    [TestMethod]
    public void AModifierThatIsFalse_IsNotTheSameAsAnAbsentOne()
    {
        OpenCodeKeybindValue stated = OpenCodeKeybindCodec.ReadValue(
            CurrencyText.Parse("""{"name":"q","ctrl":false}"""), isDefined: true);
        OpenCodeKeybindValue absent = OpenCodeKeybindCodec.ReadValue(
            CurrencyText.Parse("""{"name":"q"}"""), isDefined: true);

        Assert.AreEqual(false, stated.Bindings[0].Key!.Ctrl);
        Assert.IsNull(absent.Bindings[0].Key!.Ctrl);

        Assert.AreEqual(
            "{name:q,ctrl:false}",
            CurrencyText.Render(OpenCodeKeybindCodec.WriteValue(stated)));
        Assert.AreEqual(
            "{name:q}",
            CurrencyText.Render(OpenCodeKeybindCodec.WriteValue(absent)));
    }

    [TestMethod]
    public void AnEventObject_RoundTripsWithAllItsOptions()
    {
        const string Json =
            """{"key":{"name":"q","ctrl":true},"event":"release","preventDefault":true,"fallthrough":false}""";

        OpenCodeKeybindValue value = OpenCodeKeybindCodec.ReadValue(
            CurrencyText.Parse(Json), isDefined: true);

        OpenCodeKeyBinding binding = value.Bindings[0];
        Assert.AreEqual(OpenCodeBindingForm.Event, binding.Form);
        Assert.AreEqual("release", binding.Event);
        Assert.AreEqual(true, binding.PreventDefault);
        Assert.AreEqual(false, binding.Fallthrough);

        Assert.AreEqual(
            "{key:{name:q,ctrl:true},event:release,preventDefault:true,fallthrough:false}",
            CurrencyText.Render(OpenCodeKeybindCodec.WriteValue(value)));
    }

    /// <remarks>
    /// The event arm's <c>key</c> is itself <c>string | key-object</c>, so the string form has to
    /// survive there too rather than being promoted into an object.
    /// </remarks>
    [TestMethod]
    public void AnEventObject_KeepsAStringKeyAsAString()
    {
        OpenCodeKeybindValue value = OpenCodeKeybindCodec.ReadValue(
            CurrencyText.Parse("""{"key":"ctrl+q","event":"press"}"""), isDefined: true);

        Assert.AreEqual(OpenCodeBindingForm.Event, value.Bindings[0].Form);
        Assert.IsNull(value.Bindings[0].Key);
        Assert.AreEqual(
            "{key:ctrl+q,event:press}",
            CurrencyText.Render(OpenCodeKeybindCodec.WriteValue(value)));
    }

    /// <remarks>
    /// ⛔ <b>The event object does not set <c>additionalProperties: false</c>, unlike the key object
    /// beside it</b> — measured, not assumed. So an unknown field there is legal, which makes
    /// preserving it correctness rather than courtesy.
    /// </remarks>
    [TestMethod]
    public void UnknownFieldsSurvive_OnBothObjectArms()
    {
        OpenCodeKeybindValue onEvent = OpenCodeKeybindCodec.ReadValue(
            CurrencyText.Parse("""{"key":"q","future":42}"""), isDefined: true);
        OpenCodeKeybindValue onKey = OpenCodeKeybindCodec.ReadValue(
            CurrencyText.Parse("""{"name":"q","future":42}"""), isDefined: true);

        Assert.AreEqual(
            "{key:q,future:42}",
            CurrencyText.Render(OpenCodeKeybindCodec.WriteValue(onEvent)));
        Assert.AreEqual(
            "{name:q,future:42}",
            CurrencyText.Render(OpenCodeKeybindCodec.WriteValue(onKey)));
    }

    /// <remarks>
    /// The one uniform rule that beat four special cases in the agent codec: a surfaced key whose
    /// value is the wrong SHAPE goes to <c>Extras</c> rather than being read as absent. Without it
    /// a non-boolean <c>preventDefault</c> reads as null and then vanishes on save — and it is
    /// already schema-invalid, i.e. exactly when the user needs to see it.
    /// </remarks>
    [TestMethod]
    public void ASurfacedKeyWithTheWrongShape_GoesToExtras_RatherThanVanishing()
    {
        OpenCodeKeybindValue value = OpenCodeKeybindCodec.ReadValue(
            CurrencyText.Parse("""{"key":"q","preventDefault":"yes","event":7}"""),
            isDefined: true);

        Assert.IsNull(value.Bindings[0].PreventDefault);
        Assert.IsNull(value.Bindings[0].Event);
        Assert.AreEqual(
            "{key:q,preventDefault:yes,event:7}",
            CurrencyText.Render(OpenCodeKeybindCodec.WriteValue(value)));
    }

    /// <remarks>
    /// ⚠ The <c>event</c> value is kept as a raw string rather than an enum, so a value outside the
    /// schema's two literals survives instead of being folded onto whichever one an enum defaults
    /// to. Invalid either way; only one of the two outcomes tells the user.
    /// </remarks>
    [TestMethod]
    public void AnEventValueOutsideTheEnum_IsKept()
    {
        OpenCodeKeybindValue value = OpenCodeKeybindCodec.ReadValue(
            CurrencyText.Parse("""{"key":"q","event":"hover"}"""), isDefined: true);

        Assert.AreEqual("hover", value.Bindings[0].Event);
        Assert.AreEqual(
            "{key:q,event:hover}",
            CurrencyText.Render(OpenCodeKeybindCodec.WriteValue(value)));
    }

    /// <remarks>
    /// An object carrying neither <c>key</c> nor <c>name</c> matches neither object arm. As one
    /// action's whole value it is the middle opaque arm.
    /// </remarks>
    [TestMethod]
    public void AnObjectMatchingNeitherArm_IsHeldVerbatim()
    {
        OpenCodeKeybindValue value = OpenCodeKeybindCodec.ReadValue(
            CurrencyText.Parse("""{"gesture":"swipe"}"""), isDefined: true);

        Assert.AreEqual(OpenCodeKeybindMode.Unrecognised, value.Mode);
        Assert.AreEqual(
            "{gesture:swipe}",
            CurrencyText.Render(OpenCodeKeybindCodec.WriteValue(value)));
    }

    /// <remarks>
    /// ⭐⭐ <b>The innermost opaque arm, and the reason it exists at per-binding granularity.</b> One
    /// unreadable element of a sequence must not freeze the readable ones beside it — the same
    /// per-entry rule <c>mcp</c> established so that a single newer-OpenCode server does not make
    /// its twelve neighbours uneditable.
    /// </remarks>
    [TestMethod]
    public void OneUnreadableBindingInASequence_DoesNotFreezeItsNeighbours()
    {
        OpenCodeKeybindValue value = OpenCodeKeybindCodec.ReadValue(
            CurrencyText.Parse("""["ctrl+q", 42, {"name":"x"}]"""), isDefined: true);

        Assert.AreEqual(OpenCodeKeybindMode.Sequence, value.Mode);
        Assert.AreEqual(3, value.Bindings.Count);
        Assert.IsFalse(value.Bindings[0].IsOpaque);
        Assert.IsTrue(value.Bindings[1].IsOpaque, "The unreadable element was not held.");
        Assert.IsFalse(value.Bindings[2].IsOpaque);

        Assert.AreEqual(
            "[ctrl+q,42,{name:x}]",
            CurrencyText.Render(OpenCodeKeybindCodec.WriteValue(value)));
    }

    /// <remarks>
    /// Reachable by one obvious click — clearing a chord's text, or a structured key's name. Named
    /// so the editor can report it, and never repaired: inventing a name would claim a keystroke the
    /// user never made. Same rule the <c>lsp</c> editor follows for an entry matching neither arm.
    /// </remarks>
    [TestMethod]
    public void AKeylessBinding_IsIncomplete_AndStillWritesWhatWasTyped()
    {
        OpenCodeKeyBinding blankChord = new() { Form = OpenCodeBindingForm.Chord, Chord = "  " };
        OpenCodeKeyBinding namelessKey = new()
        {
            Form = OpenCodeBindingForm.Key,
            Key = new OpenCodeKeySpec { Name = string.Empty, Ctrl = true },
        };
        OpenCodeKeyBinding keylessEvent = new()
        {
            Form = OpenCodeBindingForm.Event,
            Event = "press",
        };

        Assert.IsTrue(blankChord.IsIncomplete);
        Assert.IsTrue(namelessKey.IsIncomplete);
        Assert.IsTrue(keylessEvent.IsIncomplete);

        // ⚠ The nameless key still writes `name`, unlike a command's missing template which is
        // omitted. Omitting it here would produce an object matching the OTHER arm of the union
        // rather than an invalid one — silently reclassifying the binding instead of reporting it.
        Assert.AreEqual(
            "{name:,ctrl:true}",
            CurrencyText.Render(OpenCodeKeybindCodec.WriteValue(new OpenCodeKeybindValue
            {
                Mode = OpenCodeKeybindMode.Bound,
                Bindings = [namelessKey],
            })));
    }

    /// <remarks>
    /// A complete binding must not be reported as incomplete — the negative half, without which the
    /// warning would cry wolf and train users to ignore it. Same reason the shell-command detector
    /// pins its negative cases.
    /// </remarks>
    [TestMethod]
    public void ACompleteBinding_IsNotFlagged()
    {
        OpenCodeKeyBinding[] fine =
        [
            new() { Form = OpenCodeBindingForm.Chord, Chord = "ctrl+q" },
            new()
            {
                Form = OpenCodeBindingForm.Key,
                Key = new OpenCodeKeySpec { Name = "q" },
            },
            new()
            {
                Form = OpenCodeBindingForm.Event,
                Key = new OpenCodeKeySpec { Name = "q" },
            },
            new() { Form = OpenCodeBindingForm.Event, Chord = "ctrl+q" },
            new() { Form = OpenCodeBindingForm.Opaque, IsOpaque = true, Raw = 42L },
        ];

        foreach (OpenCodeKeyBinding binding in fine)
        {
            Assert.IsFalse(
                binding.IsIncomplete,
                $"A {binding.Form} binding was wrongly flagged as incomplete.");
        }
    }

    private static OpenCodeKeybindValue Bound(string chord) => new()
    {
        Mode = OpenCodeKeybindMode.Bound,
        Bindings = [new OpenCodeKeyBinding { Form = OpenCodeBindingForm.Chord, Chord = chord }],
    };
}
