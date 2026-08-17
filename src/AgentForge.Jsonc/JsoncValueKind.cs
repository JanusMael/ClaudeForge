namespace Bennewitz.Ninja.AgentForge.Jsonc;

/// <summary>Structural category of a parsed JSONC value.</summary>
public enum JsoncValueKind
{
    Object,
    Array,
    String,
    Number,
    True,
    False,
    Null,
}
