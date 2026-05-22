using System.Text.Json.Serialization;

namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Controls how much of the Claude Code footprint a backup includes.
/// </summary>
/// <remarks>
/// Serialised as a string (<c>"SettingsOnly"</c> / <c>"Full"</c>) for readability
/// when users inspect a backup's <c>manifest.json</c> with a text editor.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<BackupMode>))]
public enum BackupMode
{
    /// <summary>
    /// Default. Includes <c>~/.claude.json</c>, settings / hooks / agents / slash-commands,
    /// per-project <c>.claude</c> folders, external worktrees, and Claude Desktop config —
    /// but **excludes** <c>~/.claude/projects/</c> (session transcripts, memory artefacts)
    /// which can be hundreds of megabytes and is safely rebuildable from the transcripts.
    /// </summary>
    SettingsOnly = 0,

    /// <summary>
    /// Everything <see cref="SettingsOnly"/> includes, plus <c>~/.claude/projects/</c>.
    /// Suitable for full disaster-recovery and machine-to-machine migrations.
    /// </summary>
    Full = 1,

    /// <summary>
    /// Same file scope as <see cref="SettingsOnly"/> (no <c>projects/</c>), but
    /// every <c>*.json</c> file is parsed and any value whose key matches the
    /// <c>SensitiveKeys</c> classifier (env, headers, credentials, auth,
    /// authorization, plus the substring catchers token / secret / password /
    /// apikey / bearer …) is replaced with the literal string
    /// <c>"[redacted]"</c> before being written into the archive.
    /// <para>
    /// Intended for sharing your configuration shape with support, the
    /// community, or a bug report WITHOUT leaking API keys, MCP auth headers,
    /// or OAuth tokens.  <strong>Non-restorable</strong>: <c>RestoreEngine</c>
    /// refuses to apply a Sanitized backup because doing so would overwrite
    /// the user's working config with the literal <c>"[redacted]"</c>
    /// placeholders.  The Restore list shows a clear "(sanitized — for
    /// sharing)" chip on these archives so the user can recognise them at
    /// a glance.
    /// </para>
    /// </summary>
    Sanitized = 2,
}