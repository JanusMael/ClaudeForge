namespace Bennewitz.Ninja.ClaudeForge.Core.Schema;

/// <summary>
/// Owns the hook-event vocabulary the Hooks editor presents. The authoritative
/// set of valid events is the settings schema's <c>hooks.properties</c> (pinned
/// with <c>additionalProperties:false</c>); this type layers a curated display
/// ORDER over that fresh schema set and supplies a fallback set for when the
/// schema can't be read (an offline/minimal schema, or a bare test node).
/// </summary>
/// <remarks>
/// <para>
/// This replaces the former hardcoded <c>HooksEditorViewModel.KnownEventTypes</c>
/// mirror: the editor now derives its live list from the schema and uses this
/// class only for ordering + graceful degradation, so a schema refresh that adds
/// or removes an event flows through with no code change. A guard test keeps
/// <see cref="CuratedOrder"/> a subset of the schema (a stale entry fails CI).
/// </para>
/// <para>
/// Pure domain — no UI, no localization. Mirrors the <c>ModelCatalog</c>
/// "Core owns the data, the editor consumes it" precedent.
/// </para>
/// </remarks>
public static class HookEventCatalog
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    /// <summary>
    /// Curated display order for the events we recognise — and the fallback set
    /// when the schema doesn't expose <c>hooks.properties</c>. This is an OVERLAY,
    /// not an allowlist: it never decides what's valid (the schema does), it only
    /// orders recognised events and seeds a sensible default. Keep the order
    /// stable so the user's muscle memory in the left rail holds:
    /// lifecycle → compact → tool calls → permission/elicitation → prompt/stop →
    /// sub-agents → worktrees → cwd/file watchers → config → setup → notification.
    /// </summary>
    public static readonly IReadOnlyList<string> CuratedOrder =
    [
        "SessionStart", "SessionEnd",
        "PreCompact", "PostCompact",
        "PreToolUse", "PostToolUse", "PostToolUseFailure",
        "PermissionRequest",
        "Elicitation", "ElicitationResult",
        "UserPromptSubmit",
        "Stop",
        "SubagentStart", "SubagentStop",
        "TeammateIdle", "TaskCompleted",
        "WorktreeCreate", "WorktreeRemove",
        "CwdChanged", "FileChanged",
        "ConfigChange",
        "InstructionsLoaded",
        "Setup",
        "Notification",
    ];

    /// <summary>
    /// The events to offer proactively in the editor: the live
    /// <paramref name="schemaEventNames"/> (fresh source of truth), ordered by
    /// <see cref="CuratedOrder"/>, with any schema events we don't curate
    /// appended in schema order (so a newly-shipped event surfaces without a code
    /// change). When <paramref name="schemaEventNames"/> is <see langword="null"/>
    /// or empty — the schema didn't expose <c>hooks.properties</c> — falls back to
    /// <see cref="CuratedOrder"/> so the editor still offers the standard set.
    /// </summary>
    public static IReadOnlyList<string> ResolveOrder(IReadOnlyCollection<string>? schemaEventNames)
    {
        if (schemaEventNames is null || schemaEventNames.Count == 0)
        {
            return CuratedOrder;
        }

        HashSet<string> schemaSet = new(schemaEventNames, Ord);
        HashSet<string> curatedSet = new(CuratedOrder, Ord);
        List<string> ordered = new(schemaSet.Count);

        // Recognised events first, in curated order (skipping any the schema dropped).
        foreach (string name in CuratedOrder)
        {
            if (schemaSet.Contains(name))
            {
                ordered.Add(name);
            }
        }

        // Then schema events we don't curate, in schema order.
        foreach (string name in schemaEventNames)
        {
            if (!curatedSet.Contains(name))
            {
                ordered.Add(name);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Record-carrying overload of <see cref="ResolveOrder(IReadOnlyCollection{string})"/>:
    /// orders the schema events by the curated overlay (same rule as the name-based
    /// version) while preserving each event's <see cref="HookEventInfo.Description"/>.
    /// Falls back to <see cref="CuratedOrder"/> (names only, null descriptions) when
    /// <paramref name="schemaEvents"/> is <see langword="null"/>/empty.
    /// </summary>
    public static IReadOnlyList<HookEventInfo> ResolveOrder(IReadOnlyList<HookEventInfo>? schemaEvents)
    {
        if (schemaEvents is null || schemaEvents.Count == 0)
        {
            return CuratedOrder.Select(n => new HookEventInfo(n, null)).ToList();
        }

        Dictionary<string, HookEventInfo> byName = new(Ord);
        foreach (HookEventInfo e in schemaEvents)
        {
            byName.TryAdd(e.Name, e);
        }

        // Reuse the name-based ordering, then re-attach the descriptions.
        return ResolveOrder(schemaEvents.Select(e => e.Name).ToList())
               .Select(n => byName.TryGetValue(n, out HookEventInfo? info) ? info : new HookEventInfo(n, null))
               .ToList();
    }

    /// <summary>
    /// The subset of <paramref name="candidateEventNames"/> that are NOT part of
    /// the live schema's hook events — i.e. unrecognised or deprecated events.
    /// Order follows <paramref name="candidateEventNames"/>; duplicates are
    /// collapsed. Returns an empty list when the schema set is unknown
    /// (<see langword="null"/>/empty): we can't judge, so we don't flag anything
    /// (forgiving-by-default, matching the editor's tolerance of unknown events
    /// on disk).
    /// </summary>
    public static IReadOnlyList<string> UnrecognizedEvents(
        IEnumerable<string> candidateEventNames, IReadOnlyCollection<string>? schemaEventNames)
    {
        if (schemaEventNames is null || schemaEventNames.Count == 0)
        {
            return [];
        }

        HashSet<string> schemaSet = new(schemaEventNames, Ord);
        return candidateEventNames.Where(n => !schemaSet.Contains(n)).Distinct(Ord).ToList();
    }
}
