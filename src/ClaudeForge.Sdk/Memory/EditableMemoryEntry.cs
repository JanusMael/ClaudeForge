using System;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

/// <summary>
/// One discovered agent / skill / slash-command file, scope-tagged for the
/// editing surface.  Produced by <see cref="EditableMemoryService.Snapshot"/>.
///
/// <para>
/// Enumeration is <b>stat-only</b> — no file contents are read during
/// discovery, so a snapshot over hundreds of plugin files returns fast.  The
/// <c>description</c> front-matter subtitle is loaded lazily by the UI via
/// <see cref="EditableMemoryService.LoadDescription"/> once a row is shown.
/// </para>
/// </summary>
/// <param name="AbsolutePath">Full path on disk; "Reveal in Explorer" + read/write use this.</param>
/// <param name="Category">
/// <see cref="UserMemoryCategory.Subagent"/>, <see cref="UserMemoryCategory.Skill"/>,
/// or <see cref="UserMemoryCategory.SlashCommand"/> — the three editable kinds.
/// </param>
/// <param name="Scope">User / Project / Plugin origin.</param>
/// <param name="DisplayName">
/// Friendly label: file name without extension for agents / commands; the
/// parent directory name for skills (whose file is always <c>SKILL.md</c>).
/// </param>
/// <param name="Source">
/// Disambiguating origin label shown per row.  <c>"User"</c> / <c>"Project"</c>
/// for writable scopes; the providing plugin's path-derived name (e.g.
/// <c>"everything-claude-code"</c>) for <see cref="EditableMemoryScope.Plugin"/>
/// entries — so two same-named artifacts from different plugins are
/// distinguishable at a glance.
/// </param>
/// <param name="IsWritable">
/// <see langword="false"/> for <see cref="EditableMemoryScope.Plugin"/> entries;
/// the editor disables save for these.
/// </param>
/// <param name="SizeBytes">File size in bytes.</param>
/// <param name="LastWriteUtc">Last-write timestamp (UTC); a sort key.</param>
public sealed record EditableMemoryEntry(
    string AbsolutePath,
    UserMemoryCategory Category,
    EditableMemoryScope Scope,
    string DisplayName,
    string Source,
    bool IsWritable,
    long SizeBytes,
    DateTime LastWriteUtc);
