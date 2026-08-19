using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Search;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// This app's hand-written search rows — the product half of the search seam that
/// <see cref="SearchViewModel"/> walks.
///
/// <para>
/// Everything here is Claude knowledge: a CLI flag users type expecting to find
/// its config-file equivalent, the enable-bypass deep link, and the trigger
/// phrases for the pinned Essentials cards. None of it can live in the neutral
/// shell, and none of it is an algorithm — the shell owns matching, ordering and
/// suppression; this file supplies only the words and the nav targets.
/// </para>
/// </summary>
public static class ClaudeSyntheticSearch
{
    /// <summary>
    /// Title of the Essentials nav node. Hardcoded English to match the
    /// culture-invariant <c>NavTitle*</c> constants the nav tree is built from —
    /// the localized <see cref="Strings.NavTitleEssentials"/> is the row's
    /// <em>display</em> group label, which is a different job.
    /// </summary>
    private const string EssentialsNodeTitle = "Essentials";

    /// <summary>Title of the Permissions child node under a product's header.</summary>
    private const string PermissionsNodeTitle = "Permissions";

    /// <summary>Entry id for the <c>--dangerouslySkipPermissions</c> row.</summary>
    public const string EntryIdDangerFlag = "cli-dangerously-skip-permissions";

    /// <summary>Entry id for the "select bypassPermissions" deep link.</summary>
    public const string EntryIdBypassDefaultMode = "permissions-default-mode-bypass";

    /// <summary>
    /// Trigger phrases (lower-case) for the pinned Essentials cards, keyed by
    /// <see cref="EssentialsCardViewModel.Id"/>. Matched bidirectionally by
    /// <see cref="SearchTrigger.Phrases"/>, so a query like "thinking tokens"
    /// reaches the same card as "max thinking" or "MAX_THINKING_TOKENS", and a
    /// partially typed "san" reaches the sandbox cards.
    /// </summary>
    /// <remarks>
    /// Kept here rather than on each card object so the search-side and the
    /// card-side lifecycles stay independent — queries match card ids without
    /// needing an <see cref="EssentialsViewModel"/> instance to be alive (e.g.
    /// before the first navigation tree build). <c>ClaudeSyntheticSearchTests</c>
    /// asserts every card id appears here, so a future card is not silently
    /// un-searchable.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>>
        EssentialsTriggers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [EssentialsViewModel.CardIdMaxThinkingTokens] =
            [
                "thinking", "tokens", "thinking tokens", "max thinking",
                "max_thinking_tokens",
            ],
            [EssentialsViewModel.CardIdMaxOutputTokens] =
            [
                "output tokens", "max output", "max_output",
                "claude_code_max_output_tokens",
            ],
            [EssentialsViewModel.CardIdEnableAllProjectMcp] =
            [
                "auto trust", "auto-trust", "mcp trust",
                "enableallprojectmcpservers", "trust mcp",
            ],
            [EssentialsViewModel.CardIdSandboxEnabled] =
            [
                "sandbox", "bash sandbox", "sandbox.enabled",
            ],
            [EssentialsViewModel.CardIdSandboxDomains] =
            [
                "allowed domains", "network egress", "sandbox domains",
                "alloweddomains",
            ],
            [EssentialsViewModel.CardIdModel] =
            [
                "model", "opus", "haiku", "sonnet", "fable",
            ],
            [EssentialsViewModel.CardIdEffortLevel] =
            [
                "effort", "effort level", "extended thinking",
                "effortlevel",
            ],
            [EssentialsViewModel.CardIdFastMode] =
            [
                "fast mode", "fastmode", "speed",
            ],
            [EssentialsViewModel.CardIdAutoUpdatesChannel] =
            [
                "auto update", "auto-update", "update channel", "stable",
                "latest", "autoupdateschannel",
            ],
            [EssentialsViewModel.CardIdAutoMemoryEnabled] =
            [
                "auto memory", "auto-memory", "memory capture",
                "automemoryenabled",
            ],
            [EssentialsViewModel.CardIdDisableBypass] =
            [
                "disable bypass", "bypass permissions",
                "disablebypasspermissionsmode",
            ],
        };

    /// <summary>
    /// Build the entry list for a search pass.
    /// <para>
    /// Called once per pass rather than cached because the card titles are
    /// localized and <see cref="Strings"/> is not culture-aware until
    /// <c>ApplyCulture</c> runs in <c>Program.Main</c> — a table built at type
    /// initialisation would pin the startup culture into every row.
    /// </para>
    /// </summary>
    /// <param name="sectionTitle">
    /// Nav title of the product header these rows are filed under, and the node
    /// whose Permissions child the permission rows target.
    /// </param>
    public static IReadOnlyList<SyntheticSearchEntry> Build(string sectionTitle)
    {
        ArgumentNullException.ThrowIfNull(sectionTitle);

        List<SyntheticSearchEntry> entries = new(EssentialsTriggers.Count + 2);

        // Essentials cards first — they are the broadest set, and the shell emits
        // rows in list order.
        foreach ((string cardId, IReadOnlyList<string> phrases) in EssentialsTriggers)
        {
            string title = EssentialsCardTitle(cardId);
            entries.Add(new SyntheticSearchEntry
            {
                Id = cardId,
                Trigger = new SearchTrigger { Phrases = phrases },
                FindTarget = FindEssentialsNode,
                SectionTitle = sectionTitle,
                GroupTitle = Strings.NavTitleEssentials,
                DisplayName = title,
                PropertyKey = cardId, // card id ⇒ amber callout target
                Description = title,
            });
        }

        // The CLI flag. Not a config key at all, but the user's intent when they
        // type it is to find the setting that has the same effect. Prefix-only:
        // matching an interior fragment would fire this row on "skip" and "perm".
        entries.Add(new SyntheticSearchEntry
        {
            Id = EntryIdDangerFlag,
            Trigger = new SearchTrigger
            {
                PrefixOf = ["dangerouslyskippermissions"],
                MinQueryLength = 3,
            },
            FindTarget = tree => FindPermissionsNode(tree, sectionTitle),
            SectionTitle = sectionTitle,
            GroupTitle = PermissionsNodeTitle,
            DisplayName = "--dangerouslySkipPermissions",
            // Empty on purpose — show all editors on the page; the hint banner the
            // host raises for this row provides the guidance.
            PropertyKey = string.Empty,
            Snippet = "Set permissions.defaultMode = bypassPermissions",
            Description = "Equivalent to --dangerouslySkipPermissions. "
                          + "Set permissions.defaultMode = bypassPermissions to suppress all tool "
                          + "permission prompts. Only use this in fully isolated environments.",
        });

        // Selecting the bypass default mode. Distinct from the CLI-flag row above
        // (different trigger) and the opposite intent to the "Disable
        // bypass-permissions mode" card — whose "bypass permissions" phrase a bare
        // "bypass" query would otherwise also reach, which is what the veto and the
        // suppression between them are for.
        entries.Add(new SyntheticSearchEntry
        {
            Id = EntryIdBypassDefaultMode,
            Trigger = new SearchTrigger
            {
                PrefixOf = ["bypasspermissions"],
                Mentions = ["bypass"],
                Excluding = ["disable"],
                MinQueryLength = 3,
            },
            FindTarget = tree => FindPermissionsNode(tree, sectionTitle),
            SectionTitle = sectionTitle,
            GroupTitle = PermissionsNodeTitle,
            DisplayName = "permissions.defaultMode = bypassPermissions",
            PropertyKey = "permissions.defaultMode", // deep-links to the Default Mode editor
            Snippet = "permissions.defaultMode = bypassPermissions",
            Description = "Set permissions.defaultMode = bypassPermissions to suppress all "
                          + "tool permission prompts. Only use this in fully isolated environments.",
            Suppresses = [EssentialsViewModel.CardIdDisableBypass],
        });

        return entries;
    }

    /// <summary>
    /// The synthetic Essentials nav node, or <see langword="null"/> when the tree
    /// hasn't been built yet (or was rebuilt without it — defensive: the page
    /// could end up behind a flag).
    /// </summary>
    private static NavigationNodeViewModel? FindEssentialsNode(
        IEnumerable<NavigationNodeViewModel> tree)
    {
        return tree.FirstOrDefault(n => n.Title == EssentialsNodeTitle
                                        && n.Editor is EssentialsViewModel);
    }

    /// <summary>
    /// The "Permissions" child under <paramref name="sectionTitle"/>, falling back
    /// to the first "Permissions" child anywhere in the tree when that section is
    /// absent.
    /// </summary>
    private static NavigationNodeViewModel? FindPermissionsNode(
        IEnumerable<NavigationNodeViewModel> tree, string sectionTitle)
    {
        NavigationNodeViewModel? sectionNode = tree.FirstOrDefault(n => n.Title == sectionTitle);
        return sectionNode?.Children.FirstOrDefault(c => c.Title == PermissionsNodeTitle)
               ?? tree
                  .SelectMany(n => n.Children)
                  .FirstOrDefault(c => c.Title == PermissionsNodeTitle);
    }

    /// <summary>
    /// Localised, human-friendly title for a card id. Looked up per search pass,
    /// not at table-build time — see <see cref="Build"/> for why.
    /// </summary>
    private static string EssentialsCardTitle(string cardId)
    {
        return cardId switch
        {
            EssentialsViewModel.CardIdMaxThinkingTokens => Strings
                .EssentialsCardMaxThinkingTokensTitle,
            EssentialsViewModel.CardIdMaxOutputTokens => Strings
                .EssentialsCardMaxOutputTokensTitle,
            EssentialsViewModel.CardIdEnableAllProjectMcp => Strings
                .EssentialsCardEnableAllMcpTitle,
            EssentialsViewModel.CardIdSandboxEnabled => Strings
                .EssentialsCardSandboxEnabledTitle,
            EssentialsViewModel.CardIdSandboxDomains => Strings
                .EssentialsCardSandboxDomainsTitle,
            EssentialsViewModel.CardIdModel => Strings.EssentialsCardModelTitle,
            EssentialsViewModel.CardIdEffortLevel => Strings.EssentialsCardEffortLevelTitle,
            EssentialsViewModel.CardIdFastMode => Strings.EssentialsCardFastModeTitle,
            EssentialsViewModel.CardIdAutoUpdatesChannel => Strings
                .EssentialsCardAutoUpdatesChannelTitle,
            EssentialsViewModel.CardIdAutoMemoryEnabled => Strings
                .EssentialsCardAutoMemoryEnabledTitle,
            EssentialsViewModel.CardIdDisableBypass =>
                Strings.EssentialsCardDisableBypassTitle,
            var _ => cardId,
        };
    }
}
