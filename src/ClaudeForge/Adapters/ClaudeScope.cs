using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Adapters;

/// <summary>
/// Wraps <see cref="ConfigScope"/> as an <see cref="IEditorScope"/>, flipping the
/// priority convention so that higher <see cref="Priority"/> wins (library contract)
/// rather than lower enum value (Core's legacy convention).
/// </summary>
/// <remarks>
/// Cached singletons — identity equality works correctly with <c>AreSame</c>.
/// Priority formula: <c>3 - (int)configScope</c>
///   Managed=0 → Priority=3 (highest)
///   Local=1   → Priority=2
///   Project=2 → Priority=1
///   User=3    → Priority=0 (lowest)
///
/// IMPORTANT — cache ordering: the <see cref="_cache"/> array is indexed by the
/// numeric value of <see cref="ConfigScope"/> (see <see cref="For"/>). Entries
/// MUST match the current <c>ConfigScope</c> enum order; otherwise
/// <c>For(ConfigScope.User)</c> would silently return the wrapper for a
/// different scope. After the post-Project-scope priority correction the
/// enum order is Managed=0, Local=1, Project=2, User=3 — the order below.
/// </remarks>
public sealed class ClaudeScope : IEditorScope
{
    private static readonly ClaudeScope[] _cache =
    [
        new(ConfigScope.Managed), // index 0
        new(ConfigScope.Local), // index 1
        new(ConfigScope.Project), // index 2
        new(ConfigScope.User), // index 3
    ];

    private ClaudeScope(ConfigScope source)
    {
        Source = source;
        Priority = ToLibraryPriority(source);
        Id = source.ToString().ToLowerInvariant();
        DisplayName = source.ToString().ToUpperInvariant();
        IsReadOnly = source == ConfigScope.Managed;
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
        return _cache[(int)scope];
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

        // Fall back to ID-based resolution for fakes / test doubles
        return scope.Id switch
        {
            "managed" => ConfigScope.Managed,
            "user" => ConfigScope.User,
            "project" => ConfigScope.Project,
            "local" => ConfigScope.Local,
            var _ => throw new ArgumentException($"Cannot map scope '{scope.Id}' to ConfigScope.", nameof(scope)),
        };
    }

    /// <summary>Single canonical formula: inverts ConfigScope's lower=higher-priority convention.</summary>
    public static int ToLibraryPriority(ConfigScope scope)
    {
        return 3 - (int)scope;
    }

    public override string ToString()
    {
        return Id;
    }
}