namespace Bennewitz.Ninja.LayeredEditors.Abstractions;

/// <summary>
/// Describes a single property in the settings tree — its name, type, constraints,
/// and any child properties. Adapters expose their native schema representation
/// (JSON Schema, protobuf descriptor, reflection on a C# record, …) through this
/// interface without leaking the underlying type.
/// </summary>
public interface IEditorSchema
{
    /// <summary>Stable, unique identifier for the property within the settings tree
    /// (e.g. a dot-separated path <c>"permissions.defaultMode"</c>). The editor
    /// factory and workspace use this as the lookup key.</summary>
    string Path { get; }

    /// <summary>Leaf name of this property (e.g. <c>"defaultMode"</c>).</summary>
    string Name { get; }

    /// <summary>Human-readable title, or <c>null</c> to fall back to <see cref="Name"/>.</summary>
    string? Title { get; }

    /// <summary>Tooltip / help description, or <c>null</c>.</summary>
    string? Description { get; }

    /// <summary>Discriminator that drives editor-view selection.</summary>
    EditorValueType ValueType { get; }

    /// <summary>
    /// Allowed values for <see cref="EditorValueType.Enum"/> properties, or <c>null</c>
    /// for other types.
    /// </summary>
    IReadOnlyList<string>? EnumValues { get; }

    /// <summary>
    /// Example values for this property — used as suggestions in AutoCompleteBox editors
    /// for fields that are promoted from examples (no strict enum constraint).
    /// Empty for properties with no examples or with a strict enum.
    /// </summary>
    IReadOnlyList<string> Examples { get; }

    /// <summary>Minimum bound for numeric properties, or <c>null</c> if unbounded.</summary>
    double? Minimum { get; }

    /// <summary>Maximum bound for numeric properties, or <c>null</c> if unbounded.</summary>
    double? Maximum { get; }

    /// <summary>Child properties for <see cref="EditorValueType.Object"/> types;
    /// empty for all other types.</summary>
    IReadOnlyList<IEditorSchema> Properties { get; }

    /// <summary>Item schema for <see cref="EditorValueType.StringArray"/> and
    /// <see cref="EditorValueType.Dictionary"/> types; <c>null</c> otherwise.</summary>
    IEditorSchema? ItemsSchema { get; }

    /// <summary>
    /// Default value for this property, already parsed into the narrow value
    /// currency (null / bool / string / long / double / list / dict).
    /// </summary>
    object? DefaultValue { get; }

    /// <summary>
    /// True when the property is declared read-only in the schema itself
    /// (distinct from <see cref="IEditorScope.IsReadOnly"/>, which is scope-wide).
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// True when this property was not present in the last persisted schema snapshot.
    /// The library-default PropertyEditorWrapper renders a "✨ NEW" chip for these
    /// so users can discover settings that appeared since their last session.
    /// Adapters without snapshot support should return <c>false</c>.
    /// </summary>
    bool IsNew { get; }

    /// <summary>
    /// True when the schema marks this property as deprecated. Host filtering
    /// typically hides deprecated properties unless a value is set at some
    /// scope — so users aren't prompted to adopt obsolete settings while still
    /// being able to unset legacy values. Adapters without a deprecation
    /// concept should return <c>false</c>.
    /// </summary>
    bool IsDeprecated { get; }

    /// <summary>
    /// True when the property is not covered by official documentation.
    /// The default <c>PropertyEditorWrapper</c> renders a detective 🕵 badge
    /// rather than a raw "UNDOCUMENTED:" text prefix.
    /// Adapters that do not track documentation status should return <c>false</c>.
    /// </summary>
    bool IsUndocumented => false;

    /// <summary>
    /// Optional per-value descriptions (value → text) for <see cref="EditorValueType.Enum"/>
    /// pickers, surfaced as per-item tooltips. Default-implemented as empty so adapters
    /// without the concept need not provide it.
    /// </summary>
    IReadOnlyDictionary<string, string> EnumValueDescriptions => new Dictionary<string, string>(0);

    /// <summary>
    /// Extensibility bag for source-specific metadata the editors may consult
    /// (well-known keys such as <c>"format"</c>, <c>"pattern"</c>, <c>"is-required"</c>).
    /// Keeps the core interface stable while allowing adapters to surface domain
    /// hints without patching the library.
    /// </summary>
    IReadOnlyDictionary<string, object?> Metadata { get; }
}