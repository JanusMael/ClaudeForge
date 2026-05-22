namespace Bennewitz.Ninja.LayeredEditors.Abstractions;

/// <summary>
/// All scope-level contributions for a single property path, together with the
/// merged effective value.
/// </summary>
/// <remarks>
/// <para><b>Value currency contract.</b> Every value exposed through
/// <see cref="EffectiveValue"/> and <see cref="GetValueAt"/> MUST be one of:</para>
/// <list type="bullet">
///   <item><c>null</c></item>
///   <item><see cref="bool"/></item>
///   <item><see cref="string"/></item>
///   <item><see cref="long"/></item>
///   <item><see cref="double"/></item>
///   <item><c>IReadOnlyList&lt;object?&gt;</c> (items recursively obey this contract)</item>
///   <item><c>IReadOnlyDictionary&lt;string, object?&gt;</c> (values recursively obey this contract)</item>
/// </list>
/// <para>Adapters must normalise their native types (JsonNode, YamlScalar, etc.)
/// into this shape. Raw serialization types must never leak out.</para>
/// <para><b>Explicit null vs absent.</b> <see cref="IsDefinedAt"/> is the only
/// reliable way to distinguish "the key is present and explicitly null" from
/// "the key is absent at this scope". <see cref="GetValueAt"/> can legitimately
/// return <c>null</c> in the first case.</para>
/// </remarks>
public interface IEditorValue
{
    /// <summary>The settings-tree path this value belongs to (matches
    /// <see cref="IEditorSchema.Path"/> of the corresponding schema).</summary>
    string Path { get; }

    /// <summary>The scope whose value is currently winning the merge, or
    /// <c>null</c> when no scope has defined this property.</summary>
    IEditorScope? EffectiveScope { get; }

    /// <summary>The merged/winning value, obeying the value-currency contract
    /// above. <c>null</c> when no scope has defined this property, or when the
    /// winning scope explicitly stores <c>null</c>.</summary>
    object? EffectiveValue { get; }

    /// <summary>True when more than one scope has defined this property.</summary>
    bool IsOverridden { get; }

    /// <summary>Returns the value defined explicitly at <paramref name="scope"/>,
    /// or <c>null</c> if <see cref="IsDefinedAt"/> is <c>false</c> OR if the scope
    /// explicitly stores <c>null</c>. Use <see cref="IsDefinedAt"/> to
    /// disambiguate.</summary>
    object? GetValueAt(IEditorScope scope);

    /// <summary>True when <paramref name="scope"/> has explicitly defined this
    /// property (including defining it as <c>null</c>).</summary>
    bool IsDefinedAt(IEditorScope scope);

    /// <summary>
    /// Enumerates every scope that has explicitly defined this property.
    /// Order is implementation-defined; callers that want a particular order
    /// (e.g. highest-priority first) should sort the result themselves.
    /// </summary>
    /// <remarks>
    /// added so editor view-models can populate per-row
    /// "Defined in scopes:" / "Currently effective from {scope}" affordances
    /// without forcing each implementation to probe every known scope via
    /// <see cref="IsDefinedAt"/>.  The contract:
    /// <list type="bullet">
    ///   <item>Excludes scopes where the property is undefined.</item>
    ///   <item>INCLUDES scopes that explicitly stored <c>null</c> — definition
    ///         semantics match <see cref="IsDefinedAt"/>.</item>
    ///   <item>Distinct: each scope appears at most once.</item>
    /// </list>
    /// </remarks>
    IEnumerable<IEditorScope> EnumerateDefinedScopes();
}