namespace Bennewitz.Ninja.LayeredEditors.Abstractions;

/// <summary>
/// Discriminator for the shape of a property's value, used by the editor factory
/// to select an appropriate editor view-model. Independent of any specific data
/// source — JSON, YAML, registry, database rows all collapse into these categories.
/// </summary>
public enum EditorValueType
{
    /// <summary>Type could not be determined from the schema.</summary>
    Unknown = 0,

    /// <summary>True/false/unset tri-state.</summary>
    Boolean,

    /// <summary>Free-form text.</summary>
    String,

    /// <summary>Filesystem path — string with a browse-button affordance.</summary>
    Path,

    /// <summary>String with a fixed set of allowed values (<see cref="IEditorSchema.EnumValues"/>).</summary>
    Enum,

    /// <summary>Whole-number numeric value.</summary>
    Integer,

    /// <summary>Fractional numeric value.</summary>
    Number,

    /// <summary>Homogeneous list of strings.</summary>
    StringArray,

    /// <summary>Object with a fixed set of known child properties (composed via <see cref="IEditorSchema.Properties"/>).</summary>
    Object,

    /// <summary>Object with arbitrary keys — a string→value map (keys not known up-front).</summary>
    Dictionary,

    /// <summary>
    /// Domain-specific shape that does not fit any of the above. The consuming app registers
    /// a specialized `IPropertyEditorFactory` that matches on schema name or
    /// metadata to produce the appropriate editor.
    /// </summary>
    Complex,
}