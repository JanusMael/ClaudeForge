using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Hooks;

/// <summary>
/// Strongly-typed accessor for the <c>hooks</c> block of Claude Code settings.
/// </summary>
/// <remarks>
/// <see cref="Events"/> returns a lazy snapshot — see SDK design doc §6.6.
/// </remarks>
public interface IHooksAccessor
{
    /// <summary>All hooks across all event kinds.</summary>
    /// <remarks>
    /// Reads from the merged effective view across all loaded scopes. For the
    /// raw value at one specific scope (used by editor UIs that show "what's
    /// stored at THIS scope"), see <see cref="EventsAt"/>.
    /// </remarks>
    IReadOnlyList<HookEvent> Events { get; }

    /// <summary>
    /// Snapshot of hooks stored at the given <paramref name="scope"/> only
    /// (no effective merging). Used by GUI editors that bind to the per-scope
    /// view rather than the merged effective view.
    /// </summary>
    /// <remarks>Lazy materialization — same semantics as <see cref="Events"/>.</remarks>
    IReadOnlyList<HookEvent> EventsAt(ConfigScope scope);

    /// <summary>Add a hook event. No-op when an exact-match equivalent already exists.</summary>
    void Add(HookEvent hook);

    /// <summary>Remove a hook event. Returns <see langword="true"/> when an entry was removed.</summary>
    bool Remove(HookEvent hook);

    /// <summary>Remove all hooks at the default scope.</summary>
    void Clear();
}