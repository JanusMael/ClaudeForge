using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;

/// <summary>
/// Per-group exception hook for the otherwise-uniform
/// settings-group tab strip. Lets a specific group
/// contribute extra tabs at any index and/or hide the built-in
/// Properties / Effective / JSON tabs.
/// </summary>
/// <remarks>
/// Called by the group editor after the built-in tabs
/// are seeded and the property editors are built, so an implementation can reach
/// a compound editor via <c>editors.OfType&lt;T&gt;()</c>
/// to use as a contributed tab's content.
/// </remarks>
public interface IGroupTabCustomizer
{
    /// <summary>
    /// Mutate <paramref name="tabs"/> in place for the group named
    /// <paramref name="groupName"/>. The list arrives seeded with the built-in
    /// tabs in order (Properties, Effective, JSON). Implementations may
    /// <c>Insert</c> contributed tabs at any index and/or <c>Remove</c> defaults.
    /// A no-op leaves the built-in layout unchanged.
    /// </summary>
    /// <param name="groupName">The group's title, as shown in the navigation tree.</param>
    /// <param name="tabs">The mutable, pre-seeded tab list.</param>
    /// <param name="editors">The group's property editors (for reaching a
    /// compound editor VM to bind a contributed tab to).</param>
    /// <param name="selectTab">
    /// Deferred navigation callback (the group VM's <c>SelectTab(tabId)</c>). An
    /// implementation can capture it so a contributed tab — or an in-editor
    /// hyperlink — can deep-link to a sibling tab after the strip is built.
    /// <see langword="null"/> in unit tests that exercise the customizer in isolation.
    /// </param>
    void Customize(
        string groupName,
        IList<GroupTab> tabs,
        IReadOnlyList<PropertyEditorViewModel> editors,
        Action<string>? selectTab = null);
}
