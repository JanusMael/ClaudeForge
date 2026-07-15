namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IEditorSchema"/>.
/// </summary>
public sealed class FakeEditorSchema : IEditorSchema
{
    public FakeEditorSchema(
        string path,
        EditorValueType valueType = EditorValueType.String,
        string? name = null)
    {
        Path = path;
        Name = name ?? path.Split('.')[^1];
        ValueType = valueType;
    }

    public string Path { get; init; }
    public string Name { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public EditorValueType ValueType { get; init; }

    public IReadOnlyList<string>? EnumValues { get; init; }
    public IReadOnlyList<string> Examples { get; init; } = [];
    public IReadOnlyDictionary<string, string> EnumValueDescriptions { get; init; } = new Dictionary<string, string>();
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public IReadOnlyList<IEditorSchema> Properties { get; init; } = [];
    public IEditorSchema? ItemsSchema { get; init; }
    public object? DefaultValue { get; init; }
    public bool IsReadOnly { get; init; }
    public bool IsNew { get; init; }
    public bool IsDeprecated { get; init; }
    public bool IsUndocumented { get; init; }

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        new Dictionary<string, object?>();
}