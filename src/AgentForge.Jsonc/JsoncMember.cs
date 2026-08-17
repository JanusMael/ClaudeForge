namespace Bennewitz.Ninja.AgentForge.Jsonc;

/// <summary>
/// One <c>"key": value</c> pair inside an object.
/// </summary>
/// <remarks>
/// Carries three spans because removal and replacement need different extents:
/// replacing a value touches only <see cref="JsoncValue.Start"/>..<see cref="JsoncValue.End"/>
/// of <see cref="Value"/>, while removing the member has to take the key, the colon,
/// and the value together — that is <see cref="Start"/>..<see cref="End"/>.
/// </remarks>
public sealed class JsoncMember
{
    internal JsoncMember(string name, int keyStart, int keyEnd, JsoncValue value)
    {
        Name = name;
        KeyStart = keyStart;
        KeyEnd = keyEnd;
        Value = value;
    }

    /// <summary>The unescaped key.</summary>
    public string Name { get; }

    /// <summary>Index of the key's opening quote.</summary>
    public int KeyStart { get; }

    /// <summary>Index one past the key's closing quote.</summary>
    public int KeyEnd { get; }

    /// <summary>The member's value.</summary>
    public JsoncValue Value { get; }

    /// <summary>Index of the member's first character — the key's opening quote.</summary>
    public int Start => KeyStart;

    /// <summary>Index one past the member's last character — the value's end.</summary>
    public int End => Value.End;
}
