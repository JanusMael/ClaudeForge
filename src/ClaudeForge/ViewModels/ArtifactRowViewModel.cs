using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// One artifact row in the Agents &amp; Skills page's segmented lists — a
/// thin, display-oriented wrapper over an <see cref="EditableMemoryEntry"/>
/// from the scope-aware <see cref="EditableMemoryService"/>.
///
/// <para>
/// <see cref="Subtitle"/> (the front-matter <c>description</c>) is loaded
/// <b>lazily</b> by the page VM after the fast stat-only snapshot populates
/// the rows — so the lists appear instantly and subtitles fill in shortly
/// after.  <see cref="Source"/> disambiguates same-named artifacts shipped by
/// different plugins.
/// </para>
/// </summary>
public sealed partial class ArtifactRowViewModel(EditableMemoryEntry entry) : ObservableObject
{
    /// <summary>The underlying discovery entry (path, scope, category, etc.).</summary>
    public EditableMemoryEntry Entry { get; } = entry;

    /// <summary>Friendly label — file name (agents/commands) or parent dir (skills).</summary>
    public string DisplayName => Entry.DisplayName;

    /// <summary>
    /// Disambiguating origin: "User" / "Project" for writable rows, the
    /// providing plugin's name for plugin rows.  Shown as a per-row chip so
    /// same-named artifacts from different plugins are distinguishable.
    /// </summary>
    public string Source => Entry.Source;

    /// <summary>Absolute path — passed to Reveal / Open-externally commands.</summary>
    public string AbsolutePath => Entry.AbsolutePath;

    /// <summary><see langword="false"/> for plugin-provided artifacts.</summary>
    public bool IsWritable => Entry.IsWritable;

    /// <summary><see langword="true"/> for plugin-scoped (read-only) rows.</summary>
    public bool IsPlugin => Entry.Scope == EditableMemoryScope.Plugin;

    /// <summary>
    /// Whether this row may be deleted — writable (User / Project) artifacts
    /// only.  Plugin-provided rows are never deletable (the governing theme:
    /// never delete things installed by another thing).
    /// </summary>
    public bool IsDeletable => IsWritable;

    /// <summary>
    /// <see langword="true"/> for skills — delete removes the whole skill
    /// directory (its <c>SKILL.md</c> plus any sibling assets), not just the file.
    /// </summary>
    public bool IsSkill => Entry.Category == UserMemoryCategory.Skill;

    /// <summary>Short scope label: "User" / "Project" / "Plugin".</summary>
    public string ScopeLabel => Entry.Scope switch
    {
        EditableMemoryScope.User => "User",
        EditableMemoryScope.Project => "Project",
        EditableMemoryScope.Plugin => "Plugin",
        _ => Entry.Scope.ToString(),
    };

    /// <summary>
    /// Front-matter <c>description</c> subtitle, loaded lazily.  Empty until
    /// the page VM's background fill resolves it; then the description, or
    /// "(no description)" if the file has no description key.
    /// </summary>
    [ObservableProperty] private string? _subtitle;
}