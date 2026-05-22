namespace Bennewitz.Ninja.LayeredEditors.Abstractions;

/// <summary>
/// The editors' gateway to the underlying settings store. Aggregates all scopes
/// for a given settings surface (e.g. all Claude Code settings files, from
/// managed through local). Adapters wrap their native storage model (documents,
/// registry hives, cloud records) behind this interface.
/// </summary>
/// <remarks>
/// <para>All writes are in-memory — the adapter decides when to flush to disk
/// (typically coordinated by the host app's save command).</para>
/// <para>Implementations MUST raise <see cref="ValueChanged"/> after every
/// successful <see cref="SetValue"/> or <see cref="RemoveValue"/> so that sibling
/// editors listening by path prefix can refresh their state without polling.</para>
/// </remarks>
public interface IEditorWorkspace
{
    /// <summary>All scopes this workspace recognises, ordered by
    /// <see cref="IEditorScope.Priority"/> (highest first — "wins" order).</summary>
    IReadOnlyList<IEditorScope> AvailableScopes { get; }

    /// <summary>Retrieve the layered value for <paramref name="path"/>. Returns
    /// an empty <see cref="IEditorValue"/> (no scopes defined) if the path is
    /// currently unset everywhere.</summary>
    IEditorValue GetValue(string path);

    /// <summary>Write <paramref name="value"/> at <paramref name="scope"/>. The
    /// value must obey the narrow currency contract on <see cref="IEditorValue"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="scope"/>'s <see cref="IEditorScope.IsReadOnly"/> is true.
    /// </exception>
    void SetValue(string path, object? value, IEditorScope scope);

    /// <summary>Remove any explicit value at <paramref name="scope"/>, letting
    /// lower-priority scopes provide the effective value.</summary>
    void RemoveValue(string path, IEditorScope scope);

    /// <summary>Raised after every successful mutation.</summary>
    event EventHandler<ValueChangedEventArgs>? ValueChanged;
}