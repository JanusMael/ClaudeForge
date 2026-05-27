namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

/// <summary>
/// Origin scope of an editable agent / skill / slash-command artifact.
/// Distinct from <c>ConfigScope</c> (Managed / Local / Project / User),
/// which models settings-file precedence — these three are the only
/// scopes that actually host agent/skill/command files, and "Plugin"
/// (read-only) has no ConfigScope analogue.
/// </summary>
public enum EditableMemoryScope
{
    /// <summary>User home — <c>~/.claude/{agents,commands,skills}/</c>. Writable.</summary>
    User = 0,

    /// <summary>Open project — <c>&lt;project&gt;/.claude/{agents,commands,skills}/</c>. Writable.</summary>
    Project = 1,

    /// <summary>
    /// Provided by an installed Claude Code plugin under
    /// <c>~/.claude/plugins/…</c>.  Read-only: editing in place would fight
    /// the plugin's own update mechanism, so the editor surfaces these with
    /// a "plugin-managed" badge and no edit affordance.
    /// </summary>
    Plugin = 2,
}