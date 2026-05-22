using System.Collections;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Internal;

/// <summary>
/// Lazy snapshot list returned by every Sdk accessor.
/// </summary>
/// <typeparam name="T">Element type.</typeparam>
/// <remarks>
/// <para>
/// Constructed cheaply with a materialization factory; the underlying state is
/// not enumerated until the consumer calls <see cref="this[int]"/>,
/// <see cref="Count"/>, or <see cref="GetEnumerator"/> for the first time.
/// On first access, the factory is invoked exactly once and the resulting
/// snapshot is cached for the rest of the list's lifetime.
/// </para>
/// <para>
/// Thread-safety contract — the factory is expected to acquire the workspace
/// read lock, project the underlying state to records, and release the lock
/// before returning. Once materialized, the snapshot is immutable from the
/// consumer's point of view; concurrent mutations to the underlying state by
/// other threads do not affect this list.
/// </para>
/// <para>
/// <see langword="internal"/> by design — the SDK uses this internally; it
/// is not part of the public contract. Consumers see only
/// <see cref="IReadOnlyList{T}"/>.
/// </para>
/// </remarks>
internal sealed class LazyReadOnlyList<T> : IReadOnlyList<T>
{
    private readonly Func<IReadOnlyList<T>> _materialize;
    private IReadOnlyList<T>? _snapshot;

    public LazyReadOnlyList(Func<IReadOnlyList<T>> materialize)
    {
        _materialize = materialize;
    }

    private IReadOnlyList<T> Snapshot => _snapshot ??= _materialize();

    public T this[int index] => Snapshot[index];
    public int Count => Snapshot.Count;

    public IEnumerator<T> GetEnumerator()
    {
        return Snapshot.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}