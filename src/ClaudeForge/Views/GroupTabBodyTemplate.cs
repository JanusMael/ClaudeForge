using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Views;

/// <summary>
/// Selects the body control for a <see cref="GroupTab"/> in
/// <c>SettingsGroupEditorView</c>'s data-driven tab strip, keyed on
/// <see cref="GroupTab.Id"/>. The chosen control's <c>DataContext</c> is set to
/// the tab's <see cref="GroupTab.Content"/> (the group VM for built-in tabs, the
/// compound editor VM for contributed tabs), so each body's compiled bindings
/// resolve against the right source.
/// </summary>
/// <remarks>
/// Only the selected tab's body is built (Avalonia instantiates the
/// <see cref="IDataTemplate"/> result for the active item), so heavy bodies — the
/// virtualized property list, the permissions surfaces — are not all materialized
/// at once. Built-in ids come from <see cref="GroupTab"/>; permissions ids from
/// <see cref="ClaudeGroupTabCustomizer"/>.
/// </remarks>
public sealed class GroupTabBodyTemplate : IDataTemplate
{
    public bool Match(object? data) => data is GroupTab;

    public Control Build(object? data)
    {
        GroupTab tab = (GroupTab)data!;

        Control body = tab.Id switch
        {
            GroupTab.PropertiesId => new GroupPropertiesView(),
            GroupTab.EffectiveId => new GroupEffectiveView(),
            GroupTab.JsonId => new GroupJsonView(),
            ClaudeGroupTabCustomizer.PermCommonId => new PermissionsCommonView(),
            ClaudeGroupTabCustomizer.PermBuildId => new PermissionsBuildView(),
            ClaudeGroupTabCustomizer.PermListsId => new PermissionsListsView(),
            ClaudeGroupTabCustomizer.PermAdvancedId => new PermissionsAdvancedView(),
            // Defensive fallback: an unknown id renders its id as plain text rather
            // than throwing — a contributed tab with no matching body is visible,
            // not a crash.
            var _ => new TextBlock { Text = tab.Id },
        };

        // The contributed permissions activity bodies are plain panels with no
        // scroller of their own, so wrap them so overflow scrolls vertically.
        // Built-in bodies are returned as-is: Properties has a virtualized
        // ScrollViewer (wrapping it again would break virtualization), Effective
        // is a self-scrolling DataGrid, and JSON is a self-scrolling highlight
        // block.
        bool needsScroller = tab.Id is ClaudeGroupTabCustomizer.PermCommonId
                                     or ClaudeGroupTabCustomizer.PermBuildId
                                     or ClaudeGroupTabCustomizer.PermListsId;

        Control root = needsScroller
            ? new ScrollViewer
            {
                Content = body,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(0, 0, 6, 0),
            }
            : body;

        // Bind to the tab's content (group VM or compound editor VM), overriding
        // the inherited GroupTab DataContext from the content presenter. Set on
        // the outer control so the inner body inherits it.
        root.DataContext = tab.Content;
        return root;
    }
}
