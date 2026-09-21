using System.Collections;

namespace Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

/// <summary>
/// A string-keyed map that enumerates in insertion order.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b><see cref="Dictionary{TKey,TValue}"/> does not promise an enumeration order.</b> In
/// practice it yields insertion order when nothing has been removed, which is why code that
/// round-trips JSON objects through one usually appears to preserve key order — but that is an
/// implementation detail the BCL documents as unspecified, not a contract.
/// </para>
/// <para>
/// That is a hazard rather than a curiosity, because for some configuration key order is
/// <i>meaning</i>. OpenCode resolves a tool's permission rules by taking the <b>last</b> match,
/// so reordering the keys of a permission map silently rewrites the policy: a narrow
/// <c>"npm *": "deny"</c> that moves below a broad <c>"*": "ask"</c> stops applying, with no
/// error and no visible change other than the order itself. An editor that carefully preserved
/// order would still be at the mercy of whatever the currency layer beneath it happened to do.
/// </para>
/// <para>
/// This type makes the order explicit so the guarantee stops depending on luck. It is
/// deliberately minimal: a list for order, a dictionary for lookup.
/// </para>
/// <para>
/// ⚠ <b>It lives in the abstractions layer, not next to the JSON conversion that first needed
/// it.</b> Both an SDK assembling a value and a UI adapter converting one need the same
/// guarantee, and an SDK cannot reference a UI assembly — so leaving it beside
/// <c>JsonCurrency</c> would have forced a second copy in <c>OpenCode.Sdk</c> for the <c>mcp</c>
/// codec, splitting a format's read and write halves across two types that could then disagree.
/// A string-keyed map with a defined order is a neutral primitive; nothing about it is Avalonia's.
/// </para>
/// </remarks>
public sealed class OrderedPropertyMap : IReadOnlyDictionary<string, object?>
{
    private readonly List<string> _order = [];
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    /// <summary>An empty map.</summary>
    public OrderedPropertyMap()
    {
    }

    /// <summary>A map holding <paramref name="entries"/> in the order given.</summary>
    public OrderedPropertyMap(IEnumerable<KeyValuePair<string, object?>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach ((string key, object? value) in entries)
        {
            Set(key, value);
        }
    }

    /// <summary>
    /// Add or replace <paramref name="key"/>.
    /// </summary>
    /// <remarks>
    /// Replacing an existing key keeps its original position. Anything else would make editing a
    /// value change the policy — the one thing this type exists to prevent.
    /// </remarks>
    public void Set(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_values.ContainsKey(key))
        {
            _order.Add(key);
        }

        _values[key] = value;
    }

    /// <inheritdoc />
    public object? this[string key] => _values[key];

    /// <inheritdoc />
    public IEnumerable<string> Keys => _order;

    /// <inheritdoc />
    public IEnumerable<object?> Values => _order.Select(k => _values[k]);

    /// <inheritdoc />
    public int Count => _order.Count;

    /// <inheritdoc />
    public bool ContainsKey(string key) => _values.ContainsKey(key);

    /// <inheritdoc />
    public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        foreach (string key in _order)
        {
            yield return new KeyValuePair<string, object?>(key, _values[key]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
