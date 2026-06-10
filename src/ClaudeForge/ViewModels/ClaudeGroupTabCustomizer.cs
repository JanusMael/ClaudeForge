using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// ClaudeForge's <see cref="IGroupTabCustomizer"/>: contributes the Permissions
/// group's Common / Build / Lists / Advanced tabs as siblings of the built-in
/// Properties tab (inserted right after it, before Effective/JSON).
/// </summary>
/// <remarks>
/// The contributed tabs bind to the group's single
/// <see cref="PermissionsEditorViewModel"/> (the same instance the Properties
/// tab's header renders), so the header and the activity tabs share one source
/// of truth. Future compound groups (Hooks, MCP, …) add their own branch here.
/// </remarks>
public sealed class ClaudeGroupTabCustomizer : IGroupTabCustomizer
{
    /// <summary>Shared stateless instance.</summary>
    public static ClaudeGroupTabCustomizer Instance { get; } = new();

    // Permissions contributed-tab ids (the view's body selector matches these).
    public const string PermissionsGroupName = "Permissions";
    public const string PermCommonId = "perm.common";
    public const string PermBuildId = "perm.build";
    public const string PermListsId = "perm.lists";
    public const string PermAdvancedId = "perm.advanced";

    public void Customize(
        string groupName,
        IList<GroupTab> tabs,
        IReadOnlyList<LibVm.PropertyEditorViewModel> editors)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(editors);

        if (groupName != PermissionsGroupName)
        {
            return;
        }

        PermissionsEditorViewModel? perm = editors.OfType<PermissionsEditorViewModel>().FirstOrDefault();
        if (perm is null)
        {
            return; // defensive: no permissions editor → leave built-ins as-is
        }

        // Relabel the built-in Properties tab to "Overview" for this group:
        // it no longer hosts the property grid's worth of content — just the
        // permissions header (explainer + Default Mode + help link). The rename
        // is scoped to the permissions branch; other groups keep "Properties".
        GroupTab? propertiesTab = tabs.FirstOrDefault(t => t.Id == GroupTab.PropertiesId);
        if (propertiesTab is not null)
        {
            propertiesTab.Header = Strings.TabPermOverview;
            // Overview is the permissions group's preferred landing tab on first
            // visit (header + Default Mode + education). Remembered selection and
            // deep-links still override it.
            propertiesTab.IsDefaultTab = true;
        }

        // Insert immediately after the Properties/Overview tab (which keeps the
        // header: explainer + Default Mode + help link).
        int insertAt = IndexAfterProperties(tabs);

        // Advanced (disableBypass + additionalDirectories) is NOT a tab — it
        // lives as a collapsed accordion at the end of the Overview body
        // (PermissionsEditorView). PermAdvancedId is retained for compatibility
        // but no longer contributed as a sibling tab.
        GroupTab[] contributed =
        [
            new() { Id = PermCommonId, Header = Strings.TabPermCommon, Content = perm },
            new() { Id = PermBuildId, Header = Strings.TabPermBuild, Content = perm },
            new() { Id = PermListsId, Header = Strings.TabPermLists, Content = perm },
        ];

        for (int i = 0; i < contributed.Length; i++)
        {
            tabs.Insert(insertAt + i, contributed[i]);
        }
    }

    // Position just after the built-in Properties tab; falls back to the front
    // if Properties was hidden by an earlier customizer.
    private static int IndexAfterProperties(IList<GroupTab> tabs)
    {
        for (int i = 0; i < tabs.Count; i++)
        {
            if (tabs[i].Id == GroupTab.PropertiesId)
            {
                return i + 1;
            }
        }

        return 0;
    }
}
