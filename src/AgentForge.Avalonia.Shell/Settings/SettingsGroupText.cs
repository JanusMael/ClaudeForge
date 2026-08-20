namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;

/// <summary>
/// The words a settings group editor needs, supplied by the host.
/// </summary>
/// <remarks>
/// <para>
/// Neutral code takes its user-facing text as <b>data</b> rather than reading a resource. This
/// is the decision Phase 5's save-dialog slice established, and the reason is specific: the
/// localization parity tests find resource files by walking to one hardcoded directory, so a
/// <c>.resx</c> added anywhere else escapes all four of their contracts — missing keys,
/// <c>TODO</c> placeholders, copies of English, and format-specifier mismatches. Moving keys into
/// a shell resource file would silently un-translate them.
/// </para>
/// <para>
/// Every member is <see langword="required"/>. A default would let a second product inherit the
/// first product's words, or ship untranslated English while every other string on the page is
/// localized — visible only to someone reading that page in that language.
/// </para>
/// <para>
/// ⚠ <b>The two JSON headers were hardcoded English before this type existed</b>
/// (<c>"JSON (all)"</c> / <c>"JSON (active)"</c>, written inline in the group editor while the
/// two tabs beside them were resource-backed). Making them data does not translate them — the
/// host still has to supply real strings — but it does put them somewhere a translator can see.
/// </para>
/// </remarks>
public sealed record SettingsGroupText
{
    /// <summary>Header for the built-in properties tab.</summary>
    public required string TabProperties { get; init; }

    /// <summary>Header for the built-in effective-value tab.</summary>
    public required string TabEffective { get; init; }

    /// <summary>
    /// Header for the JSON tab when placeholders are shown, i.e. every schema key including
    /// those absent from the file.
    /// </summary>
    public required string TabJsonAll { get; init; }

    /// <summary>
    /// Header for the JSON tab when only keys actually present in the file are shown.
    /// </summary>
    public required string TabJsonActive { get; init; }
}
