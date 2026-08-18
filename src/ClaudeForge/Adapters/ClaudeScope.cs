using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Adapters;

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
public sealed class ClaudeScope : IEditorScope
{
    /// <summary>
    /// Keyed by scope rather than indexed by ordinal — see the class remarks. Built from
    /// <see cref="ConfigScope.All"/> so a scope added to the ladder is wrapped
    /// automatically instead of falling off the end of a hand-maintained array.
    /// </summary>
    private static readonly Dictionary<ConfigScope, ClaudeScope> _cache =
        ConfigScope.All.ToDictionary(scope => scope, scope => new ClaudeScope(scope));

    private ClaudeScope(ConfigScope source)
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

    /// <summary>Return the singleton <see cref="ClaudeScope"/> for the given <see cref="ConfigScope"/>.</summary>
    public static ClaudeScope For(ConfigScope scope)
    {
        return _cache.TryGetValue(scope, out ClaudeScope? wrapper)
            ? wrapper
            : throw new ArgumentOutOfRangeException(
                nameof(scope), scope, "No ClaudeScope wrapper exists for this scope.");
    }

    /// <summary>
    /// Resolve an <see cref="IEditorScope"/> back to a <see cref="ConfigScope"/>.
    /// Throws if <paramref name="scope"/> is not a <see cref="ClaudeScope"/> instance.
    /// </summary>
    public static ConfigScope ToConfigScope(IEditorScope scope)
    {
        if (scope is ClaudeScope cs)
        {
            return cs.Source;
        }

        // Fall back to ID-based resolution for fakes / test doubles. Resolved against
        // ConfigScope.All rather than a hand-written list of the four ids, so the mapping
        // cannot drift out of step with the ladder.
        foreach (ConfigScope candidate in ConfigScope.All)
        {
            if (string.Equals(candidate.ToString(), scope.Id, StringComparison.OrdinalIgnoreCase))
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
        return ConfigScope.All.Count - 1 - scope.Ordinal;
    }

    public override string ToString()
    {
        return Id;
    }
}