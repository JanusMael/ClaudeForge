namespace Bennewitz.Ninja.ClaudeForge.Core.Schema;

/// <summary>
/// One hook lifecycle event as described by the settings schema: its name plus
/// the schema's human-readable description (<see langword="null"/> when the schema
/// doesn't describe it). Ordered/curated by <see cref="HookEventCatalog"/> and
/// surfaced through <c>IHooksAccessor.KnownEvents</c> so headless callers (CLI,
/// MCP servers) and the GUI editor share one schema-derived source — including the
/// descriptions, not just the names.
/// </summary>
/// <param name="Name">Event name, e.g. <c>CwdChanged</c>.</param>
/// <param name="Description">Schema description, or <see langword="null"/>.</param>
public sealed record HookEventInfo(string Name, string? Description);
