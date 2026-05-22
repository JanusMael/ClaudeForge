namespace Bennewitz.Ninja.LayeredEditors.Abstractions;

/// <summary>
/// One level in a layered/stacked settings hierarchy (e.g. "managed policy",
/// "user profile", "project override", "local override"). Scopes define the
/// merge precedence: when a property is defined at several scopes, the scope
/// with the highest <see cref="Priority"/> wins.
/// </summary>
/// <remarks>
/// Priority follows a <b>higher-wins</b> convention. If an adapter's underlying
/// type uses a different convention (e.g. lower-wins), it must invert at the
/// adapter boundary. Implementations should be cheap to equality-compare — use
/// <see cref="Id"/> or implement <see cref="Object.Equals(Object)"/> deterministically.
/// </remarks>
public interface IEditorScope
{
    /// <summary>Merge precedence. Higher value wins over lower.</summary>
    int Priority { get; }

    /// <summary>Stable identifier used for equality and lookup (e.g. <c>"managed"</c>, <c>"user"</c>).</summary>
    string Id { get; }

    /// <summary>User-facing short label (e.g. <c>"MANAGED"</c>, <c>"USER"</c>).</summary>
    string DisplayName { get; }

    /// <summary>
    /// True when values at this scope cannot be edited from the UI
    /// (e.g. policy-imposed managed settings, remote-only overrides).
    /// </summary>
    bool IsReadOnly { get; }
}