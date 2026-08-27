using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Agents;

/// <summary>
/// An <see cref="IEditorValue"/> over a single value at a single scope, for hosting a compound
/// editor as a child of another one.
/// </summary>
/// <remarks>
/// <para>
/// The editor library's compound editors load from an <see cref="IEditorValue"/> — a whole layered
/// stack with an effective value and per-scope contributions. A <i>nested</i> value has no such
/// stack: an agent's <c>permission</c> override is one value inside one scope's object, and the
/// layering that produced the object already happened one level up.
/// </para>
/// <para>
/// ⚠ <b><see cref="IsOverridden"/> is therefore <see langword="false"/> and
/// <see cref="EnumerateDefinedScopes"/> yields at most the one scope — deliberately.</b> A child
/// editor rendering scope chiclets here would be claiming to know which scopes contribute to a
/// nested key, which this type cannot know and must not guess. The parent editor is the thing that
/// carries the scope story.
/// </para>
/// </remarks>
public sealed class NestedEditorValue : IEditorValue
{
    private readonly IEditorScope _scope;
    private readonly object? _value;
    private readonly bool _defined;

    /// <summary>Wraps <paramref name="value"/> as the value at <paramref name="scope"/>.</summary>
    /// <param name="path">The settings-tree path, for display and diagnostics.</param>
    /// <param name="scope">The scope being edited.</param>
    /// <param name="value">The nested value, or <see langword="null"/> when the key is absent.</param>
    /// <param name="defined">
    /// Whether the key was present. Distinct from <paramref name="value"/> being
    /// <see langword="null"/>, which is a legal stored value — the same distinction
    /// <see cref="IEditorValue.IsDefinedAt"/> exists to express.
    /// </param>
    public NestedEditorValue(string path, IEditorScope scope, object? value, bool defined)
    {
        Path = path;
        _scope = scope;
        _value = value;
        _defined = defined;
    }

    /// <inheritdoc />
    public string Path { get; }

    /// <inheritdoc />
    public IEditorScope? EffectiveScope => _defined ? _scope : null;

    /// <inheritdoc />
    public object? EffectiveValue => _defined ? _value : null;

    /// <inheritdoc />
    public bool IsOverridden => false;

    /// <inheritdoc />
    public object? GetValueAt(IEditorScope scope) =>
        scope.Id == _scope.Id ? _value : null;

    /// <inheritdoc />
    public bool IsDefinedAt(IEditorScope scope) => _defined && scope.Id == _scope.Id;

    /// <inheritdoc />
    public IEnumerable<IEditorScope> EnumerateDefinedScopes() =>
        _defined ? [_scope] : [];
}

/// <summary>
/// A minimal <see cref="IEditorSchema"/> for a nested compound value.
/// </summary>
/// <remarks>
/// The child editor reads only naming and display metadata off its schema, so a full
/// <c>SchemaNode</c> would be more plumbing than payoff — the shell's tree builder collapses
/// combinator branches, and the nested node is not one of the top-level nodes it produces. What
/// this deliberately does not fake is any constraint: no enum values, no bounds, nothing that
/// would make the child validate against something invented here.
/// </remarks>
public sealed class NestedEditorSchema : IEditorSchema
{
    /// <summary>Creates a schema stub for a nested property.</summary>
    /// <param name="name">The property name — what the editor factory dispatches on.</param>
    /// <param name="path">The settings-tree path.</param>
    /// <param name="title">Display title.</param>
    /// <param name="description">Help text.</param>
    public NestedEditorSchema(string name, string path, string? title = null, string? description = null)
    {
        Name = name;
        Path = path;
        Title = title;
        Description = description;
    }

    /// <inheritdoc />
    public string Path { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string? Title { get; }

    /// <inheritdoc />
    public string? Description { get; }

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
    public IReadOnlyList<IEditorSchema> Properties => [];

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
