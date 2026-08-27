using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.OpenCode.Sdk.Keybinds;

/// <summary>
/// Reads and writes the TUI's <c>keybinds</c> value in the editor library's value currency.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both halves live in this one file</b>, for the reason every codec in this phase does: a reader
/// that accepts a spelling the writer never produces is how a config quietly changes on save, and
/// keeping the halves adjacent is what has stopped that six times.
/// </para>
/// <para>
/// ⚠ <b>Nesting is what makes this the largest shape in the plan.</b> The container is an object of
/// 184 declared actions; each action's value is a four-arm union; the third arm is itself a three-arm
/// union; the fourth is an array of that same inner union; and the event arm's <c>key</c> is a
/// two-arm union again. So there is an opaque arm at <b>three</b> levels — the whole value, one
/// action's value, and one binding inside a sequence — because a single unreadable binding must not
/// freeze the other 183 actions. Same per-entry granularity as <c>mcp</c>, one level deeper.
/// </para>
/// <para>
/// ⚠ <b>Action order is the caller's, not the schema's.</b> Unlike the <c>mcp</c> and
/// <c>formatter</c> codecs — which write an entry's handful of keys in schema order because six keys
/// carry no meaning — a <c>keybinds</c> object can name 184 keys, and reordering them turns a
/// one-line change into an unreviewable diff. This codec therefore emits
/// <see cref="OpenCodeKeybindConfig.Entries"/> in the order it is given, and the editor is
/// responsible for keeping file order for actions that were already present.
/// </para>
/// </remarks>
public static class OpenCodeKeybindCodec
{
    /// <summary>The one string the second arm admits.</summary>
    public const string NoneLiteral = "none";

    /// <summary>The <c>event</c> value for a key press.</summary>
    public const string PressEvent = "press";

    /// <summary>The <c>event</c> value for a key release.</summary>
    public const string ReleaseEvent = "release";

    /// <summary>The two <c>event</c> values the schema's enum admits, in declared order.</summary>
    public static IReadOnlyList<string> EventLiterals { get; } = [PressEvent, ReleaseEvent];

    /// <summary>Parse a whole <c>keybinds</c> value.</summary>
    /// <param name="value">The value at the editing scope, in the library's currency.</param>
    /// <param name="isDefined">
    /// Whether the editing scope defines the key at all. Required because
    /// <c>IEditorValue.GetValueAt</c> returns <see langword="null"/> for both "absent" and
    /// "explicitly null", and those are different states.
    /// </param>
    /// <param name="knownActions">
    /// The action names the schema declares, used only to flag entries the schema does not know.
    /// Pass an empty set to treat every entry as known.
    /// </param>
    public static OpenCodeKeybindConfig Read(
        object? value,
        bool isDefined,
        IReadOnlySet<string>? knownActions = null)
    {
        if (!isDefined)
        {
            return new OpenCodeKeybindConfig { IsDefined = false };
        }

        if (value is not IReadOnlyDictionary<string, object?> map)
        {
            return new OpenCodeKeybindConfig { IsDefined = true, IsOpaque = true, Raw = value };
        }

        List<OpenCodeKeybindEntry> entries = [];
        foreach ((string action, object? item) in map)
        {
            bool isKnown = knownActions is null
                || knownActions.Count == 0
                || knownActions.Contains(action);

            entries.Add(new OpenCodeKeybindEntry(action, ReadValue(item, isDefined: true), isKnown));
        }

        return new OpenCodeKeybindConfig { IsDefined = true, Entries = entries };
    }

    /// <summary>
    /// Serialise a whole <c>keybinds</c> value, or <see langword="null"/> when the key should be
    /// removed.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>An action whose mode is <see cref="OpenCodeKeybindMode.NotSet"/> is skipped, not written
    /// as <c>null</c>.</b> With 184 rows on screen and most of them untouched, the alternative would
    /// turn opening the page into a 184-key diff. So "not set" is the one mode that produces no key,
    /// and it is what every row starts as.
    /// </remarks>
    public static object? Write(OpenCodeKeybindConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!config.IsDefined)
        {
            return null;
        }

        if (config.IsOpaque)
        {
            return config.Raw;
        }

        OrderedPropertyMap map = new();
        foreach (OpenCodeKeybindEntry entry in config.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Action)
                || entry.Value.Mode == OpenCodeKeybindMode.NotSet)
            {
                continue;
            }

            map.Set(entry.Action, WriteValue(entry.Value));
        }

        return map;
    }

    /// <summary>Parse a single action's value.</summary>
    /// <param name="value">The value stated for one action.</param>
    /// <param name="isDefined">Whether the action appears in the file at all.</param>
    /// <remarks>
    /// ⚠⚠ <b>The array arm is matched before anything is unwrapped, and a one-element array stays
    /// <see cref="OpenCodeKeybindMode.Sequence"/>.</b> <c>"x"</c> and <c>["x"]</c> are different
    /// files, and folding the second into the first looks like tidying while silently moving the
    /// value to the other arm of the union. Fourth time this phase has met that trap.
    /// </remarks>
    public static OpenCodeKeybindValue ReadValue(object? value, bool isDefined)
    {
        if (!isDefined)
        {
            return new OpenCodeKeybindValue { Mode = OpenCodeKeybindMode.NotSet };
        }

        switch (value)
        {
            // The boolean arm admits ONLY `false`. A literal `true` is not in the enum, so it is
            // held rather than read as "enabled" — there is no such arm to read it into.
            case false:
                return new OpenCodeKeybindValue { Mode = OpenCodeKeybindMode.Disabled };

            case string text when string.Equals(text, NoneLiteral, StringComparison.Ordinal):
                return new OpenCodeKeybindValue { Mode = OpenCodeKeybindMode.None };

            case IReadOnlyList<object?> list:
            {
                List<OpenCodeKeyBinding> bindings = [];
                foreach (object? item in list)
                {
                    bindings.Add(ReadBinding(item));
                }

                return new OpenCodeKeybindValue
                {
                    Mode = OpenCodeKeybindMode.Sequence,
                    Bindings = bindings,
                };
            }

            case string or IReadOnlyDictionary<string, object?>:
            {
                OpenCodeKeyBinding binding = ReadBinding(value);
                return binding.IsOpaque
                    ? new OpenCodeKeybindValue
                    {
                        Mode = OpenCodeKeybindMode.Unrecognised,
                        Raw = value,
                    }
                    : new OpenCodeKeybindValue
                    {
                        Mode = OpenCodeKeybindMode.Bound,
                        Bindings = [binding],
                    };
            }

            default:
                return new OpenCodeKeybindValue
                {
                    Mode = OpenCodeKeybindMode.Unrecognised,
                    Raw = value,
                };
        }
    }

    /// <summary>
    /// Serialise a single action's value, or <see langword="null"/> when the action should be
    /// omitted.
    /// </summary>
    /// <remarks>
    /// ⚠ <b><see cref="OpenCodeKeybindMode.Sequence"/> with no bindings writes <c>[]</c>, not
    /// <see langword="null"/>.</b> The empty array is what the mode <i>means</i> — the user chose the
    /// list form and has not added to it yet — and collapsing it to a removal would undo the only
    /// thing their choice did. Same deliberate exception the <c>formatter</c> codec makes for an
    /// empty <c>{}</c>.
    /// </remarks>
    public static object? WriteValue(OpenCodeKeybindValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        switch (value.Mode)
        {
            case OpenCodeKeybindMode.Disabled:
                return false;

            case OpenCodeKeybindMode.None:
                return NoneLiteral;

            case OpenCodeKeybindMode.Unrecognised:
                return value.Raw;

            case OpenCodeKeybindMode.Bound:
                // A `Bound` value carrying no binding cannot express itself in this arm; the mode
                // exists only to hold exactly one. Writing nothing is the honest outcome, and the
                // editor cannot reach it — a row leaving `Bound` empty is reported, not written.
                return value.Bindings.Count == 0 ? null : WriteBinding(value.Bindings[0]);

            case OpenCodeKeybindMode.Sequence:
            {
                List<object?> items = [];
                foreach (OpenCodeKeyBinding binding in value.Bindings)
                {
                    items.Add(WriteBinding(binding));
                }

                return items;
            }

            // NotSet.
            default:
                return null;
        }
    }

    /// <summary>Parse one binding out of the inner three-arm union.</summary>
    private static OpenCodeKeyBinding ReadBinding(object? value)
    {
        if (value is string text)
        {
            return new OpenCodeKeyBinding
            {
                Form = OpenCodeBindingForm.Chord,
                Chord = text,
            };
        }

        if (value is not IReadOnlyDictionary<string, object?> map)
        {
            return new OpenCodeKeyBinding
            {
                Form = OpenCodeBindingForm.Opaque,
                IsOpaque = true,
                Raw = value,
            };
        }

        // The two object arms are told apart by which key they carry. `key` is required by the event
        // arm and forbidden by the key arm (additionalProperties: false), and `name` is required by
        // the key arm and not declared on the event arm — so the discriminator is unambiguous in
        // both directions rather than a guess about which fields dominate.
        if (map.ContainsKey("key"))
        {
            return ReadEventBinding(map);
        }

        if (map.ContainsKey("name"))
        {
            return new OpenCodeKeyBinding
            {
                Form = OpenCodeBindingForm.Key,
                Key = ReadKeySpec(map),
            };
        }

        return new OpenCodeKeyBinding
        {
            Form = OpenCodeBindingForm.Opaque,
            IsOpaque = true,
            Raw = value,
        };
    }

    private static OpenCodeKeyBinding ReadEventBinding(IReadOnlyDictionary<string, object?> map)
    {
        string chord = string.Empty;
        OpenCodeKeySpec? key = null;
        string? eventName = null;
        bool? preventDefault = null;
        bool? fallthrough = null;
        List<KeyValuePair<string, object?>> extras = [];

        foreach ((string name, object? item) in map)
        {
            // A surfaced key whose value is the WRONG SHAPE goes to Extras rather than being read as
            // absent. Without this rule an unreadable `event` or a non-boolean `fallthrough` reads
            // as null and then vanishes on save — and each is already schema-invalid, i.e. exactly
            // when the user needs to see it. One uniform rule, learned in 9a-5.
            switch (name)
            {
                case "key" when item is string keyText:
                    chord = keyText;
                    break;
                case "key" when item is IReadOnlyDictionary<string, object?> keyMap:
                    key = ReadKeySpec(keyMap);
                    break;
                case "event" when item is string eventText:
                    eventName = eventText;
                    break;
                case "preventDefault" when item is bool prevent:
                    preventDefault = prevent;
                    break;
                case "fallthrough" when item is bool fall:
                    fallthrough = fall;
                    break;
                default:
                    extras.Add(new KeyValuePair<string, object?>(name, item));
                    break;
            }
        }

        return new OpenCodeKeyBinding
        {
            Form = OpenCodeBindingForm.Event,
            Chord = chord,
            Key = key,
            Event = eventName,
            PreventDefault = preventDefault,
            Fallthrough = fallthrough,
            Extras = extras,
        };
    }

    private static OpenCodeKeySpec ReadKeySpec(IReadOnlyDictionary<string, object?> map)
    {
        string name = string.Empty;
        bool? ctrl = null;
        bool? shift = null;
        bool? meta = null;
        bool? super = null;
        bool? hyper = null;
        List<KeyValuePair<string, object?>> extras = [];

        foreach ((string key, object? item) in map)
        {
            switch (key)
            {
                case "name" when item is string text:
                    name = text;
                    break;
                case "ctrl" when item is bool flag:
                    ctrl = flag;
                    break;
                case "shift" when item is bool flag:
                    shift = flag;
                    break;
                case "meta" when item is bool flag:
                    meta = flag;
                    break;
                case "super" when item is bool flag:
                    super = flag;
                    break;
                case "hyper" when item is bool flag:
                    hyper = flag;
                    break;
                default:
                    extras.Add(new KeyValuePair<string, object?>(key, item));
                    break;
            }
        }

        return new OpenCodeKeySpec
        {
            Name = name,
            Ctrl = ctrl,
            Shift = shift,
            Meta = meta,
            Super = super,
            Hyper = hyper,
            Extras = extras,
        };
    }

    private static object? WriteBinding(OpenCodeKeyBinding binding)
    {
        if (binding.IsOpaque)
        {
            return binding.Raw;
        }

        if (binding.Form == OpenCodeBindingForm.Chord)
        {
            return binding.Chord;
        }

        if (binding.Form == OpenCodeBindingForm.Key)
        {
            return binding.Key is null ? null : WriteKeySpec(binding.Key);
        }

        // The event arm, in the schema's declared order.
        OrderedPropertyMap map = new();

        // `key` is required, so it is written whichever of its two forms the binding holds. A
        // binding with neither is reported through IsIncomplete and still written without the key,
        // because inventing one would claim a keystroke the user never made.
        if (binding.Key is not null)
        {
            map.Set("key", WriteKeySpec(binding.Key));
        }
        else if (!string.IsNullOrEmpty(binding.Chord))
        {
            map.Set("key", binding.Chord);
        }

        if (binding.Event is { } eventName)
        {
            map.Set("event", eventName);
        }

        if (binding.PreventDefault is bool preventDefault)
        {
            map.Set("preventDefault", preventDefault);
        }

        if (binding.Fallthrough is bool fallthrough)
        {
            map.Set("fallthrough", fallthrough);
        }

        foreach ((string key, object? value) in binding.Extras)
        {
            map.Set(key, value);
        }

        return map;
    }

    /// <remarks>
    /// An <see cref="OrderedPropertyMap"/> at this innermost level too. A canary in 9a-2 proved that
    /// swapping only the inner map leaves every behavioural order test green, so the guarantee has to
    /// be made at every level to hold anywhere.
    /// </remarks>
    private static IReadOnlyDictionary<string, object?> WriteKeySpec(OpenCodeKeySpec spec)
    {
        OrderedPropertyMap map = new();

        // `name` is written even when blank is the only thing there is: unlike a command's template,
        // omitting it produces an object that matches the OTHER arm of the union rather than an
        // invalid one, which would silently reclassify the binding.
        map.Set("name", spec.Name);

        if (spec.Ctrl is bool ctrl)
        {
            map.Set("ctrl", ctrl);
        }

        if (spec.Shift is bool shift)
        {
            map.Set("shift", shift);
        }

        if (spec.Meta is bool meta)
        {
            map.Set("meta", meta);
        }

        if (spec.Super is bool super)
        {
            map.Set("super", super);
        }

        if (spec.Hyper is bool hyper)
        {
            map.Set("hyper", hyper);
        }

        foreach ((string key, object? value) in spec.Extras)
        {
            map.Set(key, value);
        }

        return map;
    }
}
