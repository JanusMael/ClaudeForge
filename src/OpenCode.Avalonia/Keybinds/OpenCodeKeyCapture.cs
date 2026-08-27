using Avalonia.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Keybinds;

/// <summary>
/// Translates a keystroke the capture control observed into the schema's structured key form.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>This produces the OBJECT form, never a chord string, and the schema is the reason.</b> The
/// chord arm is a bare <c>string</c> with no <c>pattern</c>, so its grammar lives entirely in
/// OpenCode's parser — turning <c>Ctrl</c>+<c>X</c> into text would mean choosing between
/// <c>"ctrl+x"</c>, <c>"C-x"</c> and <c>"ctrl-x"</c> on no evidence and writing that guess into a
/// user's file. The object form is specified exactly, so it is what capture writes.
/// </para>
/// <para>
/// ⚠⚠ <b>What the schema does NOT specify is the key NAME vocabulary</b>, and this is the one place
/// this editor makes a best-effort guess rather than refusing to. <c>name</c> is typed as a plain
/// string with no <c>enum</c> and no <c>pattern</c>, so every spelling validates and only OpenCode's
/// runtime knows which ones it answers to. The derivation below is lowercase and conventional; the
/// name stays an ordinary editable text box precisely so a wrong guess costs the user one correction
/// rather than being unfixable. It is stated here rather than presented as authority.
/// </para>
/// <para>
/// A modifier pressed on its own is not a binding, so <see cref="TryTranslate"/> declines it. Without
/// that, merely reaching for <c>Ctrl</c>+<c>K</c> would record <c>Ctrl</c> as the binding before the
/// second key arrived.
/// </para>
/// </remarks>
public static class OpenCodeKeyCapture
{
    /// <summary>Keys that are modifiers, and so cannot be a binding on their own.</summary>
    private static readonly HashSet<Key> ModifierKeys =
    [
        Key.LeftCtrl, Key.RightCtrl,
        Key.LeftShift, Key.RightShift,
        Key.LeftAlt, Key.RightAlt,
        Key.LWin, Key.RWin,
    ];

    /// <summary>
    /// Names for keys whose <see cref="Key"/> member does not lowercase into a sensible spelling.
    /// </summary>
    /// <remarks>
    /// Only the cases where the enum member name is actively misleading are listed. Everything else
    /// falls through to a lowercase of the member name, which is right for letters, function keys and
    /// most navigation keys — listing them all would be a table to keep in sync for no gain.
    /// </remarks>
    private static readonly Dictionary<Key, string> SpecialNames = new()
    {
        [Key.Return] = "enter",
        [Key.Escape] = "escape",
        [Key.Back] = "backspace",
        [Key.Space] = "space",
        [Key.Next] = "pagedown",
        [Key.Prior] = "pageup",
        [Key.OemPlus] = "plus",
        [Key.OemMinus] = "minus",
        [Key.OemComma] = "comma",
        [Key.OemPeriod] = "period",
        [Key.OemQuestion] = "slash",
        [Key.OemPipe] = "backslash",
        [Key.OemOpenBrackets] = "bracketleft",
        [Key.OemCloseBrackets] = "bracketright",
        [Key.OemSemicolon] = "semicolon",
        [Key.OemQuotes] = "quote",
        [Key.OemTilde] = "backtick",
    };

    /// <summary>Translate a keystroke, or decline when it is not a binding on its own.</summary>
    /// <param name="key">The key that was pressed.</param>
    /// <param name="modifiers">The modifiers held with it.</param>
    /// <param name="capture">The structured key, when this returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="false"/> for a modifier pressed alone or an unrecognised key, in which case
    /// nothing is written.
    /// </returns>
    public static bool TryTranslate(Key key, KeyModifiers modifiers, out CapturedKey capture)
    {
        capture = default;

        if (key == Key.None || ModifierKeys.Contains(key))
        {
            return false;
        }

        string? name = NameOf(key);
        if (name is null)
        {
            return false;
        }

        capture = new CapturedKey(
            name,
            modifiers.HasFlag(KeyModifiers.Control),
            modifiers.HasFlag(KeyModifiers.Shift),
            // ⚠ Avalonia's `Alt` maps to the schema's `meta`, and its `Meta` — the Windows or Command
            // key — maps to the schema's `super`. The names cross over, so a one-to-one mapping by
            // name would silently swap two modifiers. This is the mapping OpenCode's own vocabulary
            // implies (`meta` beside `super` in a list that has no `alt`), and it is the second guess
            // in this file rather than a fact the schema states.
            modifiers.HasFlag(KeyModifiers.Alt),
            modifiers.HasFlag(KeyModifiers.Meta));

        return true;
    }

    /// <summary>The conventional lowercase name for <paramref name="key"/>.</summary>
    private static string? NameOf(Key key)
    {
        if (SpecialNames.TryGetValue(key, out string? special))
        {
            return special;
        }

        // The digit rows: `D1`..`D0` and `NumPad1`..`NumPad0` both name a digit, and the leading
        // letter is an artefact of C# identifiers not starting with one.
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((char)('0' + (key - Key.D0))).ToString();
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return ((char)('0' + (key - Key.NumPad0))).ToString();
        }

        string member = key.ToString();

        // An unnamed member comes back as its numeric value. There is no sensible name for that, and
        // writing the number would put a key nothing recognises into the file.
        return member.Length > 0 && !char.IsAsciiDigit(member[0])
            ? member.ToLowerInvariant()
            : null;
    }
}

/// <summary>
/// One keystroke, as the schema's structured key form describes it.
/// </summary>
/// <param name="Name">The key's name.</param>
/// <param name="Ctrl">Whether Control was held.</param>
/// <param name="Shift">Whether Shift was held.</param>
/// <param name="Meta">Whether the schema's <c>meta</c> — Alt — was held.</param>
/// <param name="Super">Whether the schema's <c>super</c> — Windows or Command — was held.</param>
public readonly record struct CapturedKey(
    string Name,
    bool Ctrl,
    bool Shift,
    bool Meta,
    bool Super);
