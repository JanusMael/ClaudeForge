using Bennewitz.Ninja.ClaudeForge.Core.Schema;
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
    /// <summary>
    /// The hook lifecycle events the loaded settings schema accepts — each with its
    /// name and the schema's human description — in a stable display order (curated
    /// overlay first, then any schema events not yet curated). Headless consumers —
    /// CLI tools, MCP servers — enumerate the valid events (and can surface their
    /// descriptions) from here without touching the schema or the GUI; the editor
    /// derives from the same source, so both agree. Falls back to a curated default
    /// (names only, null descriptions) when the client hasn't loaded a schema yet
    /// (e.g. before <see cref="IClaudeConfigClient.OpenAsync"/>).
    /// </summary>
    IReadOnlyList<HookEventInfo> KnownEvents { get; }

    /// <summary>
    /// The hook command variants the loaded settings schema defines — each with its
    /// <c>type</c> discriminator, the schema's description, and its field descriptions — as
    /// declared in <c>$defs.hookCommand.anyOf</c> (<c>command</c>, <c>prompt</c>, <c>agent</c>,
    /// <c>http</c>, …). Headless consumers get the per-type help text and per-field tooltips from
    /// here; the editor's Type picker derives from the same source instead of a hardcoded mirror.
    /// Complements <see cref="KnownEvents"/> — that surfaces the lifecycle events (the <c>hooks</c>
    /// object's keys); this surfaces the shape of an individual hook. Empty when no schema is
    /// available (a non-Claude-Code client); callers fall back to their own defaults.
    /// </summary>
    IReadOnlyList<HookCommandVariantInfo> KnownCommandTypes { get; }

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