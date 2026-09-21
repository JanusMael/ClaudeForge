using System.Collections.Concurrent;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;

/// <summary>
/// Wraps <see cref="ConfigScope"/> as an <see cref="IEditorScope"/>, flipping the
/// priority convention so that higher <see cref="Priority"/> wins (library contract)
/// rather than lower enum value (Core's legacy convention).
/// </summary>
/// <remarks>
/// Cached singletons — identity equality works correctly with <c>AreSame</c>.
/// Priority inverts the scope ladder: the highest-priority scope (lowest ordinal)
/// gets the highest <see cref="Priority"/>, so for Claude's four scopes
/// Managed=0 → 3, Local=1 → 2, Project=2 → 1, User=3 → 0.
/// <para>
/// The cache used to be an <b>array indexed by the numeric value of</b>
/// <see cref="ConfigScope"/>, which made "entries must match the enum's declaration
/// order" a hard invariant documented in <c>AGENTS.md</c> — get it wrong and
/// <c>For(ConfigScope.User)</c> silently returned a different scope's wrapper. It is now
/// a dictionary built from <see cref="ConfigScope.All"/>, so the ordering coupling is
/// gone: reordering or extending the ladder cannot mis-map a scope, and the invariant no
/// longer exists to be violated.
/// </para>
/// </remarks>
public sealed class ConfigScopeAdapter : IEditorScope
{
    /// <summary>
    /// Wrappers, created on demand and kept forever.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>This used to be pre-built from <see cref="ConfigScope.All"/>, which is the DEFAULT
    /// ladder — i.e. one product's four scopes.</b> Any scope from another product's ladder threw
    /// <see cref="ArgumentOutOfRangeException"/> from <see cref="For"/>, so the second app could
    /// not render a single settings page. Populating on demand is what makes this type actually
    /// neutral rather than merely neutrally named.
    /// <para>
    /// Concurrent because editors are built off the UI thread during startup, and
    /// <c>GetOrAdd</c> keeps the singleton guarantee the library's <c>AreSame</c> relies on:
    /// two calls for the same scope must return the same instance.
    /// </para>
    /// </remarks>
    private static readonly ConcurrentDictionary<ConfigScope, ConfigScopeAdapter> _cache = new();

    private ConfigScopeAdapter(ConfigScope source)
    {
        Source = source;
        Priority = ToLibraryPriority(source);
        // Id comes from the scope's own ladder rung rather than re-casing ToString() here:
        // the scope knows its name, and a product with a different ladder gets its own.
        // The upper-casing stays — chiclets render in caps, which is presentation and
        // deliberately not baked into ConfigScope.DisplayName.
        Id = source.Id;
        DisplayName = source.DisplayName.ToUpperInvariant();
        IsReadOnly = source.IsReadOnly;
    }

    /// <summary>The underlying <see cref="ConfigScope"/> value.</summary>
    public ConfigScope Source { get; }

    public int Priority { get; }
    public string Id { get; }
    public string DisplayName { get; }
    public bool IsReadOnly { get; }

    /// <summary>Return the singleton <see cref="ConfigScopeAdapter"/> for the given <see cref="ConfigScope"/>.</summary>
    /// <remarks>
    /// Never throws for an unknown scope. Any scope on any ladder is wrappable — the wrapper is
    /// derived entirely from what the scope itself reports.
    /// </remarks>
    public static ConfigScopeAdapter For(ConfigScope scope) =>
        _cache.GetOrAdd(scope, static s => new ConfigScopeAdapter(s));

    /// <summary>
    /// Resolve an <see cref="IEditorScope"/> back to a <see cref="ConfigScope"/>.
    /// Throws if <paramref name="scope"/> is not a <see cref="ConfigScopeAdapter"/> instance.
    /// </summary>
    public static ConfigScope ToConfigScope(IEditorScope scope)
    {
        if (scope is ConfigScopeAdapter cs)
        {
            return cs.Source;
        }

        // Fall back to ID-based resolution for fakes / test doubles. Searches every scope this
        // process has actually wrapped BEFORE the default ladder, so a second product's scope
        // resolves too — checking only ConfigScope.All would silently answer with the default
        // ladder's scope of the same name, or throw for one it has no name for.
        IEnumerable<ConfigScope> candidates = _cache.Keys.Concat(ConfigScope.All);
        foreach (ConfigScope candidate in candidates)
        {
            if (string.Equals(candidate.Id, scope.Id, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new ArgumentException($"Cannot map scope '{scope.Id}' to ConfigScope.", nameof(scope));
    }

    /// <summary>Single canonical formula: inverts ConfigScope's lower=higher-priority convention.</summary>
    /// <remarks>
    /// Derived from the ladder's length rather than the literal 3, so adding a scope does
    /// not silently push every priority off by one.
    /// </remarks>
    public static int ToLibraryPriority(ConfigScope scope)
    {
        // ⚠ Counts the rungs on THIS SCOPE'S OWN ladder, not ConfigScope.All — which is the
        // default ladder's four. A five-rung ladder's lowest scope produced -1 under the old
        // expression, inverting precedence for the whole product with no error anywhere.
        return scope.Ladder.All.Count - 1 - scope.Ordinal;
    }

    public override string ToString()
    {
        return Id;
    }
}