using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Search;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.OpenCodeForge.ViewModels;

/// <summary>
/// Search hits that are not schema properties: the questions users actually type.
/// </summary>
/// <remarks>
/// <para>
/// The shell owns matching, ordering and suppression; this is only the table. Two kinds of entry
/// live here. First, settings whose schema key does not resemble the words a user would search for
/// — nobody types <c>subagent_depth</c>, they type "how deep can agents nest". Second, and more
/// valuable, the <b>gotchas</b>: a user whose config is being ignored searches for the
/// <i>symptom</i>, not for the setting, so the symptom has to be a hit that lands on the
/// explanation.
/// </para>
/// <para>
/// ⚠ <b>The three trigger flavours differ subtly and hand-written predicates get them wrong.</b>
/// <c>Phrases</c> is bidirectional, so it fires when the query is a fragment of the phrase OR the
/// phrase contains the query — that is what makes partial typing land early. <c>PrefixOf</c> is
/// one long term the query must start. <c>Mentions</c> is one-directional, so a short query does
/// not reach a longer unrelated row. Getting this wrong already caused one shipped double-fire in
/// the sibling app.
/// </para>
/// </remarks>
public static class OpenCodeSyntheticSearch
{
    /// <summary>Entry id for the "my project config is being ignored" gotcha.</summary>
    public const string EntryIdProjectConfigDisabled = "gotcha-project-config-disabled";

    /// <summary>Entry id for the "config directory moved" gotcha.</summary>
    public const string EntryIdConfigDirMoved = "gotcha-config-dir";

    /// <summary>Entry id for the rule-ordering gotcha.</summary>
    public const string EntryIdPermissionOrder = "gotcha-permission-order";

    /// <summary>
    /// Build the table. Pages are located by title, so a page this install does not have simply
    /// yields no target and the entry is dropped — an entry never invents a destination.
    /// </summary>
    /// <param name="sectionTitle">Navigation header of the main configuration section.</param>
    public static IReadOnlyList<SyntheticSearchEntry> Build(string sectionTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionTitle);

        return
        [
            // ── settings whose key is not what a user types ──
            new SyntheticSearchEntry
            {
                Id = "agents-nesting-depth",
                Trigger = new SearchTrigger
                {
                    Phrases = ["subagent depth", "nested agents", "agent nesting", "how deep agents"],
                },
                FindTarget = t => FindPage(t, "Agents"),
                SectionTitle = sectionTitle,
                GroupTitle = "Agents",
                DisplayName = "Sub-agent depth",
                PropertyKey = "subagent_depth",
                Description = "How many levels deep an agent may delegate to another agent.",
            },
            new SyntheticSearchEntry
            {
                Id = "sharing-auto",
                Trigger = new SearchTrigger
                {
                    Phrases = ["share", "auto share", "autoshare", "share session", "public link"],
                },
                FindTarget = t => FindPage(t, "Sharing"),
                SectionTitle = sectionTitle,
                GroupTitle = "Sharing",
                DisplayName = "Session sharing",
                PropertyKey = "share",
                Description = "Whether sessions are shared, and whether that happens automatically.",
            },
            new SyntheticSearchEntry
            {
                Id = "snapshot",
                Trigger = new SearchTrigger { Phrases = ["snapshot", "restore point", "checkpoint"] },
                FindTarget = t => FindPage(t, "Sharing"),
                SectionTitle = sectionTitle,
                GroupTitle = "Sharing",
                DisplayName = "Snapshots",
                PropertyKey = "snapshot",
                Description = "Point-in-time snapshots of a session.",
            },
            new SyntheticSearchEntry
            {
                Id = "plugins",
                Trigger = new SearchTrigger { Phrases = ["plugin", "extension", "add-on"] },
                FindTarget = t => FindPage(t, "Extensions"),
                SectionTitle = sectionTitle,
                GroupTitle = "Extensions",
                DisplayName = "Plugins",
                PropertyKey = "plugin",
                Description = "Plugins loaded at startup.",
            },
            new SyntheticSearchEntry
            {
                Id = "permission-allow",
                Trigger = new SearchTrigger
                {
                    // "permission" alone must not fire this: the schema walk already returns the
                    // permission property itself, and two hits for one word reads as a bug.
                    Phrases = ["permission allow", "allow tool", "allow command", "deny command", "ask before"],
                },
                FindTarget = t => FindPage(t, "Permissions"),
                SectionTitle = sectionTitle,
                GroupTitle = "Permissions",
                DisplayName = "Tool permissions",
                PropertyKey = "permission",
                Description = "Which tools may run, and which invocations need asking first.",
            },

            // ── gotchas: the symptom is the query ──
            new SyntheticSearchEntry
            {
                Id = EntryIdProjectConfigDisabled,
                Trigger = new SearchTrigger
                {
                    Phrases =
                    [
                        "project config ignored", "config not loading", "settings ignored",
                        "opencode_disable_project_config", "project settings not applied",
                    ],
                },
                FindTarget = t => FindPage(t, "Advanced"),
                SectionTitle = sectionTitle,
                GroupTitle = "Advanced",
                DisplayName = "Project config is being ignored",
                Description =
                    "OPENCODE_DISABLE_PROJECT_CONFIG=1 removes the project layer entirely. "
                    + "While it is set, a project's config file is read by nothing — including "
                    + "this editor's effective view.",
            },
            new SyntheticSearchEntry
            {
                Id = EntryIdConfigDirMoved,
                Trigger = new SearchTrigger
                {
                    Phrases =
                    [
                        "opencode_config_dir", "config directory", "where is my config",
                        "config location", "which file is being edited",
                    ],
                },
                FindTarget = t => FindPage(t, "Advanced"),
                SectionTitle = sectionTitle,
                GroupTitle = "Advanced",
                DisplayName = "Where the config file lives",
                Description =
                    "OPENCODE_CONFIG_DIR relocates the global config directory. When it is set, "
                    + "the file under ~/.config/opencode is not the one in use.",
            },
            new SyntheticSearchEntry
            {
                Id = EntryIdPermissionOrder,
                Trigger = new SearchTrigger
                {
                    Phrases =
                    [
                        "permission order", "rule order", "deny not working",
                        "rule ignored", "last match wins",
                    ],
                },
                FindTarget = t => FindPage(t, "Permissions"),
                SectionTitle = sectionTitle,
                GroupTitle = "Permissions",
                DisplayName = "A permission rule is not taking effect",
                Description =
                    "Within a permission object the LAST matching rule wins, so broad rules "
                    + "belong first and narrow ones last. A narrow deny placed before a broad "
                    + "ask never fires — which is also what layered config files can produce "
                    + "by merging.",
            },
        ];
    }

    /// <summary>
    /// Find a settings page by title anywhere in the tree.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> when absent rather than throwing: an entry whose target is
    /// missing suppresses nothing and simply does not appear, so a page one install lacks cannot
    /// hide a page it has.
    /// </remarks>
    private static NavigationNodeViewModel? FindPage(
        IEnumerable<NavigationNodeViewModel> tree, string title)
    {
        foreach (NavigationNodeViewModel header in tree)
        {
            if (string.Equals(header.Title, title, StringComparison.Ordinal))
            {
                return header;
            }

            foreach (NavigationNodeViewModel child in header.Children)
            {
                if (string.Equals(child.Title, title, StringComparison.Ordinal))
                {
                    return child;
                }
            }
        }

        return null;
    }
}
