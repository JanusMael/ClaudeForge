namespace Bennewitz.Ninja.ClaudeForge.Core.Schema;

/// <summary>
/// The resolved value type of a JSON Schema node, used to select the appropriate property editor.
/// </summary>
public enum SchemaValueType
{
    /// <summary>Unknown or unresolved type.</summary>
    Unknown,

    /// <summary>JSON boolean.</summary>
    Boolean,

    /// <summary>JSON string (general).</summary>
    String,

    /// <summary>String with a path-like format (file or directory).</summary>
    Path,

    /// <summary>String with an enumerated set of allowed values.</summary>
    Enum,

    /// <summary>JSON integer number.</summary>
    Integer,

    /// <summary>JSON floating-point number.</summary>
    Number,

    /// <summary>JSON array.</summary>
    Array,

    /// <summary>JSON object with known properties.</summary>
    Object,

    /// <summary>
    /// Complex type requiring a specialized editor (e.g. MCP servers, hooks, permissions).
    /// </summary>
    Complex,
}