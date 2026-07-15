namespace Bennewitz.Ninja.ClaudeForge.Core.Schema;

/// <summary>
/// One field within a hook command variant: its JSON property name plus the
/// schema's human-readable description (<see langword="null"/> when the schema
/// doesn't describe it).
/// </summary>
/// <param name="Name">Property name, e.g. <c>timeout</c>, <c>if</c>, <c>statusMessage</c>.</param>
/// <param name="Description">Schema description, or <see langword="null"/>.</param>
public sealed record HookFieldInfo(string Name, string? Description);

/// <summary>
/// One hook command variant as declared in the settings schema's
/// <c>$defs.hookCommand.anyOf[]</c>: its <c>type</c> discriminator (e.g.
/// <c>command</c>, <c>prompt</c>, <c>http</c>), the variant's schema description, and
/// each of its fields' descriptions. Surfaced through <c>IHooksAccessor.KnownCommandTypes</c>
/// so headless callers (CLI, MCP servers) and the GUI editor share ONE schema-derived
/// source for the per-type picker text and per-field tooltips — replacing the hardcoded
/// GUI mirror that predated this. The mirror of Part 1's <see cref="HookEventInfo"/>, one
/// level deeper (command variants + fields instead of lifecycle events).
/// </summary>
/// <param name="Type">The <c>type</c> const discriminator, e.g. <c>command</c>.</param>
/// <param name="Description">The variant's schema description, or <see langword="null"/>.</param>
/// <param name="Fields">Each field's name + description (the <c>type</c> discriminator itself is excluded).</param>
public sealed record HookCommandVariantInfo(
    string Type,
    string? Description,
    IReadOnlyList<HookFieldInfo> Fields);
