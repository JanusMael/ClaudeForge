using CommunityToolkit.Mvvm.ComponentModel;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;

/// <summary>
/// One tab in a settings group editor's top-level tab strip.
/// The strip is data-driven so
/// groups can contribute extra tabs at any index and hide the built-ins — see
/// <see cref="IGroupTabCustomizer"/>.
/// </summary>
/// <remarks>
/// <para>
/// The host's group view renders the tab body by matching
/// <see cref="Id"/> in <c>GroupTabBodyTemplate</c>, then binds the body control's
/// <c>DataContext</c> to <see cref="Content"/>. Built-in tabs use the group VM as
/// their content; contributed tabs use a compound editor VM.
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
    /// What assistive technology announces for the generated tab.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔⛔ <b>A <c>TabControl</c> bound to <c>ItemsSource</c> names its generated
    /// <c>TabItem</c>s from the ITEM, not from the <c>ItemTemplate</c>.</b> Setting
    /// <see cref="AutomationName"/> on the <c>TextBlock</c> inside the template — which
    /// <c>SettingsGroupEditorView</c> does — names that <c>TextBlock</c> and leaves the focusable
    /// <c>TabItem</c> to fall back to <c>ToString()</c>. Measured through UI Automation on the
    /// running app: every settings-group tab announced
    /// <c>Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings.GroupTab</c> — six identical
    /// announcements on the Permissions group alone, on the primary surface of the app.
    /// </para>
    /// <para>
    /// ⚠ <b><c>AxamlAccessibilityCoverageTests</c> is structurally unable to catch this</b>, and
    /// scored this file's view at zero: it requires <c>AutomationProperties.Name</c> on controls
    /// declared in a view, and that attribute IS present in the template. The name that actually
    /// reaches the user comes from a bound view-model instead.
    /// </para>
    /// <para>
    /// ⭐ <b>Second occurrence of one bug.</b> <c>OpenCodeArtifactTabViewModel</c> had it too and
    /// was fixed the same way; those are the repo's only two <c>ItemsSource</c>-bound
    /// <c>TabControl</c>s, so it was a 100% hit rate on the pattern.
    /// <c>ItemsSourceBoundTabsTests</c> now fails for any third one that forgets.
    /// </para>
    /// <para>
    /// Falls back to <see cref="Header"/> when no explicit name is set, because the visible header
    /// is already a good announcement — and never to <c>base.ToString()</c>, which is the defect.
    /// </para>
    /// </remarks>
    public override string ToString()
        => !string.IsNullOrWhiteSpace(AutomationName) ? AutomationName
         : !string.IsNullOrWhiteSpace(Header) ? Header
         : Id;

    /// <summary>
    /// When <see langword="true"/>, this tab is the group's preferred initial
    /// selection on first visit (no remembered selection yet). Lets an
    /// <see cref="IGroupTabCustomizer"/> drive the landing tab; when no tab is
    /// marked, the first tab wins. Settable so a customizer can promote an
    /// already-seeded built-in (e.g. relabel Properties → "Overview" and mark it).
    /// </summary>
    public bool IsDefaultTab { get; set; }
}
