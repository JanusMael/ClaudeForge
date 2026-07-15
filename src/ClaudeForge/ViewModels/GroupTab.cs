using CommunityToolkit.Mvvm.ComponentModel;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// One tab in a <see cref="SettingsGroupEditorViewModel"/>'s top-level tab strip.
/// The strip is data-driven (<see cref="SettingsGroupEditorViewModel.Tabs"/>) so
/// groups can contribute extra tabs at any index and hide the built-ins — see
/// <see cref="IGroupTabCustomizer"/>.
/// </summary>
/// <remarks>
/// <para>
/// The view (<c>SettingsGroupEditorView</c>) renders the tab body by matching
/// <see cref="Id"/> in <c>GroupTabBodyTemplate</c>, then binds the body control's
/// <c>DataContext</c> to <see cref="Content"/>. Built-in tabs use the group VM as
/// their content; contributed tabs (e.g. the Permissions sub-tabs) use the
/// compound editor VM.
/// </para>
/// <para>
/// <see cref="Header"/> is observable because the JSON tab's header is dynamic
/// ("JSON (all)" / "JSON (active)").
/// </para>
/// </remarks>
public sealed partial class GroupTab : ObservableObject
{
    // ── Built-in tab ids (shared by the VM seed + the view's body selector) ──
    public const string PropertiesId = "properties";
    public const string EffectiveId = "effective";
    public const string JsonId = "json";

    /// <summary>Stable identifier the view's body selector switches on.</summary>
    public required string Id { get; init; }

    /// <summary>Localized tab-strip header. Observable for the dynamic JSON header.</summary>
    [ObservableProperty] private string _header = string.Empty;

    /// <summary>The <c>DataContext</c> the body control binds to.</summary>
    public required object Content { get; init; }

    /// <summary>Optional screen-reader name for the tab header.</summary>
    public string? AutomationName { get; init; }

    /// <summary>
    /// When <see langword="true"/>, this tab is the group's preferred initial
    /// selection on first visit (no remembered selection yet). Lets an
    /// <see cref="IGroupTabCustomizer"/> drive the landing tab; when no tab is
    /// marked, the first tab wins. Settable so a customizer can promote an
    /// already-seeded built-in (e.g. relabel Properties → "Overview" and mark it).
    /// </summary>
    public bool IsDefaultTab { get; set; }
}
