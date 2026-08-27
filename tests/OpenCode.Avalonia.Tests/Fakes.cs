using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tests;

/// <summary>
/// Minimal <see cref="IEditorScope"/> double.
/// </summary>
/// <remarks>
/// Deliberately local rather than borrowed from the editor library's own test project. A test
/// project referencing another test project's doubles couples two suites that should be free to
/// diverge — and these three interfaces are small enough that a local double is cheaper than the
/// coupling. Same call the shared SDK test projects made with their local merge-policy doubles.
/// </remarks>
public sealed class TestScope(string id, int priority, bool isReadOnly = false) : IEditorScope
{
    /// <summary>The scope the tests edit at.</summary>
    public static TestScope Project { get; } = new("project", 1);

    /// <summary>A higher-priority scope, for the two-scope cases.</summary>
    public static TestScope User { get; } = new("user", 2);

    /// <inheritdoc />
    public int Priority { get; } = priority;

    /// <inheritdoc />
    public string Id { get; } = id;

    /// <inheritdoc />
    public string DisplayName { get; } = id.ToUpperInvariant();

    /// <inheritdoc />
    public bool IsReadOnly { get; } = isReadOnly;
}

/// <summary>
/// Minimal <see cref="IEditorSchema"/> double: the permission node.
/// </summary>
/// <remarks>
/// <see cref="Properties"/> is settable because the keybinds editor builds its 184 rows FROM the
/// schema rather than from a value — it is the only editor here whose construction depends on child
/// declarations, so it is the only one that needs them in a double.
/// </remarks>
public sealed class TestSchema(string name = "permission") : IEditorSchema
{
    /// <inheritdoc />
    public string Path { get; } = name;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string? Title { get; init; }

    /// <inheritdoc />
    public string? Description { get; init; }

    /// <summary>Child property declarations, for editors that read the schema's shape.</summary>
    public IReadOnlyList<IEditorSchema> Children { get; init; } = [];

    /// <summary>Build a double for one child property.</summary>
    public static TestSchema Child(string name, string description) =>
        new(name) { Description = description };

    /// <inheritdoc />
    public EditorValueType ValueType => EditorValueType.Complex;

    /// <inheritdoc />
    public IReadOnlyList<string>? EnumValues => null;

    /// <inheritdoc />
    public IReadOnlyList<string> Examples => [];

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> EnumValueDescriptions { get; } =
        new Dictionary<string, string>();

    /// <inheritdoc />
    public double? Minimum => null;

    /// <inheritdoc />
    public double? Maximum => null;

    /// <inheritdoc />
    public IReadOnlyList<IEditorSchema> Properties => Children;

    /// <inheritdoc />
    public IEditorSchema? ItemsSchema => null;

    /// <inheritdoc />
    public object? DefaultValue => null;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public bool IsNew => false;

    /// <inheritdoc />
    public bool IsDeprecated => false;

    /// <inheritdoc />
    public bool IsUndocumented => false;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Metadata { get; } =
        new Dictionary<string, object?>();
}

/// <summary>
/// Minimal <see cref="IEditorValue"/> double holding one value per scope.
/// </summary>
public sealed class TestValue(string path = "permission") : IEditorValue
{
    private readonly List<(IEditorScope Scope, object? Value)> _entries = [];

    /// <inheritdoc />
    public string Path { get; } = path;

    /// <summary>Define an explicit value at <paramref name="scope"/>.</summary>
    public TestValue With(IEditorScope scope, object? value)
    {
        _entries.RemoveAll(e => e.Scope.Id == scope.Id);
        _entries.Add((scope, value));
        return this;
    }

    /// <inheritdoc />
    public IEditorScope? EffectiveScope =>
        _entries.OrderByDescending(e => e.Scope.Priority).Select(e => e.Scope).FirstOrDefault();

    /// <inheritdoc />
    public object? EffectiveValue => EffectiveScope is { } s ? GetValueAt(s) : null;

    /// <inheritdoc />
    public bool IsOverridden => _entries.Count > 1;

    /// <inheritdoc />
    public object? GetValueAt(IEditorScope scope) =>
        _entries.FirstOrDefault(e => e.Scope.Id == scope.Id).Value;

    /// <inheritdoc />
    public bool IsDefinedAt(IEditorScope scope) => _entries.Any(e => e.Scope.Id == scope.Id);

    /// <inheritdoc />
    public IEnumerable<IEditorScope> EnumerateDefinedScopes() => _entries.Select(e => e.Scope);
}
