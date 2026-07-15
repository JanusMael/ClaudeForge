using System.Collections.ObjectModel;
using Avalonia.Threading;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using Schema_SchemaNode = Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaNode;
using SchemaNode = Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaNode;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// Pairs a navigation section title (e.g. "Claude Code") with the SDK
/// <see cref="IClaudeConfigClient.SearchSchema"/> delegate for that product.
/// Consumed by <see cref="SearchViewModel"/> to surface property-level results
/// for specialized editors (Permissions, Hooks, MCP Servers) whose schema
/// nodes are not available via <c>SettingsGroupEditorViewModel.SchemaNodes</c>.
/// </summary>
public sealed record SchemaSearchProvider(
    string SectionTitle,
    Func<string, IReadOnlyList<SchemaSearchResult>> Search);

/// <summary>
/// Owns the search bar's debounced typing pipeline, the result-set
/// observable, and the schema-walk that produces match rows. Extracted
/// from <see cref="MainWindowViewModel"/> in to
/// shrink the god-class and isolate the search machinery for unit testing.
/// </summary>
/// <remarks>
/// <para>
/// The parent (<see cref="MainWindowViewModel"/>) still owns navigation
/// state — <c>SelectedNode</c>, the back-stack, and the
/// <c>SelectSearchResultCommand</c> that reacts to a row click. The view
/// binds the command directly to the parent VM via the standard cast
/// pattern (<c>{Binding #RootWindow.((vm:MainWindowViewModel)DataContext).SelectSearchResultCommand}</c>),
/// so this VM does not need to expose a row-selection command of its own.
/// </para>
/// <para>
/// Threading: <see cref="OnSearchQueryChanged(string)"/> runs on the UI thread and
/// schedules the actual matching pass via <see cref="Dispatcher.UIThread.Post"/>
/// after a 200 ms debounce, mirroring the prior MWVM implementation. The
/// debounce CTS is cancelled and disposed paired with each new keystroke and
/// finally on <see cref="Dispose"/>.
/// </para>
/// </remarks>
public sealed partial class SearchViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Snapshot accessor for the parent's navigation tree. Re-read on every
    /// search pass so a tree rebuild (workspace reload, project switch) is
    /// reflected without re-creating the search VM.
    /// </summary>
    private readonly Func<IEnumerable<NavigationNodeViewModel>> _getNavigationTree;

    /// <summary>True while the parent is mid-load; suppresses search execution.</summary>
    private readonly Func<bool> _isLoadingProbe;

    /// <summary>Title of the Claude Code header node — used by the synthetic
    /// <c>--dangerouslySkipPermissions</c> result to find the Permissions node.</summary>
    private readonly string _claudeCodeNavTitle;

    /// <summary>
    /// Optional providers that delegate to the SDK's
    /// <see cref="IClaudeConfigClient.SearchSchema"/> per product section. Re-evaluated
    /// on every search pass so a workspace reload (which rebuilds the SDK clients)
    /// is reflected without re-creating the search VM.  When <see langword="null"/>
    /// (e.g. unit tests that don't need SDK-backed search), specialized editors
    /// fall back to title-only matching.
    /// </summary>
    private readonly Func<IReadOnlyList<SchemaSearchProvider>>? _getSchemaSearchProviders;

    private CancellationTokenSource? _searchCts;
    private bool _disposed;

    public SearchViewModel(
        Func<IEnumerable<NavigationNodeViewModel>> getNavigationTree,
        Func<bool> isLoadingProbe,
        string claudeCodeNavTitle,
        Func<IReadOnlyList<SchemaSearchProvider>>? getSchemaSearchProviders = null)
    {
        _getNavigationTree = getNavigationTree ?? throw new ArgumentNullException(nameof(getNavigationTree));
        _isLoadingProbe = isLoadingProbe ?? throw new ArgumentNullException(nameof(isLoadingProbe));
        _claudeCodeNavTitle = claudeCodeNavTitle ?? throw new ArgumentNullException(nameof(claudeCodeNavTitle));
        _getSchemaSearchProviders = getSchemaSearchProviders;
    }

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isSearchOpen;

    /// <summary>
    /// Match rows for the current <see cref="SearchQuery"/>. Cleared on empty
    /// input; capped at 50 rows to keep the dropdown navigable.
    /// </summary>
    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = [];

    /// <summary>
    /// Debouncer + dispatcher pivot for typing input. Lifted verbatim from
    /// MWVM so the leak-free CTS swap (capture-previous, publish-new,
    /// then-cancel-old) is preserved.
    /// </summary>
    partial void OnSearchQueryChanged(string value)
    {
        // Capture the previous CTS so it can be disposed *after* its replacement
        // is published. CancellationTokenSource holds OS-level handles internally;
        // the previous code (Cancel() then assign new) leaked one CTS per keystroke
        // because the cancelled instance was never disposed.
        CancellationTokenSource? previous = _searchCts;
        _searchCts = new CancellationTokenSource();
        CancellationToken ct = _searchCts.Token;
        previous?.Cancel();
        previous?.Dispose();
        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults.Clear();
            IsSearchOpen = false;
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(200, ct);
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                Dispatcher.UIThread.Post(() => ExecuteSearch(value));
            }
            catch (OperationCanceledException)
            {
                /* normal on rapid typing — new keystroke cancelled this one */
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Search] Background search task failed");
            }
        }, ct);
    }

    /// <summary>
    /// Walk the navigation tree and populate <see cref="SearchResults"/> with
    /// matches against name / title / description. Includes a synthetic
    /// <c>--dangerouslySkipPermissions</c> row when the query prefixes that
    /// CLI flag — even though it is not a config key per se, the user's
    /// intent is to find the equivalent permissions setting.
    /// </summary>
    /// <remarks>
    /// Exposed as <c>internal</c> so unit tests can drive the matching pass
    /// directly without standing up an Avalonia dispatcher (the public
    /// <see cref="OnSearchQueryChanged(string)"/> path is debounce + dispatcher-pivot
    /// only, not testable headlessly).
    /// </remarks>
    internal void ExecuteSearch(string query)
    {
        SearchResults.Clear();
        if (_isLoadingProbe())
        {
            return;
        }

        // Phrase-quote stripping: a query wrapped in matching straight or curly
        // quotes is treated as the literal text inside the quotes. Mid-typing
        // mismatched quotes (e.g. user just typed the opening ") are left as-is
        // — the search will return no results until the closing quote arrives,
        // which is the correct UX for an explicit phrase request.
        query = StripPhraseQuotes(query);
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        // ── Synthetic results: Essentials page cards (2026-05-07) ───────────
        // Each pinned Essentials card has a trigger phrase set; if the query
        // matches any of them (substring, case-insensitive), surface a hit
        // pointing at the Essentials node with the card id in PropertyKey.
        // SelectSearchResult picks up IsSynthetic + PropertyKey and activates
        // the per-card amber callout.  Hits are added FIRST so they appear
        // at the top of the results list, ahead of schema property matches.
        TryAddEssentialsSyntheticHits(query);

        // ── Synthetic result: --dangerouslySkipPermissions ──────────────────
        // When the user starts typing "danger..." (anything that prefixes the CLI
        // flag name), surface a pinned result pointing to the Permissions page
        // even though "dangerouslySkipPermissions" is not a config key by itself.
        // The config-file equivalent is permissions.defaultMode = "bypassPermissions".
        const string dangerFlagLower = "dangerouslyskippermissions";
        if (query.Length >= 3 &&
            dangerFlagLower.StartsWith(query.ToLowerInvariant(), StringComparison.Ordinal))
        {
            NavigationNodeViewModel? permNode = FindPermissionsNode();
            if (permNode is not null)
            {
                const string synDesc = "Equivalent to --dangerouslySkipPermissions. "
                                       + "Set permissions.defaultMode = bypassPermissions to suppress all tool "
                                       + "permission prompts. Only use this in fully isolated environments.";
                SearchResults.Add(new SearchResultViewModel(
                    permNode,
                    _claudeCodeNavTitle,
                    "Permissions",
                    "--dangerouslySkipPermissions",
                    string.Empty, // Empty PropertyKey — show all editors; hint banner provides the guidance
                    "Set permissions.defaultMode = bypassPermissions",
                    synDesc)
                {
                    IsSynthetic = true,
                });
            }
        }

        // ── Synthetic result: permissions.defaultMode = bypassPermissions ───
        // Typing "bypass" / "bypassPermissions" deep-links to SELECTING the bypass
        // default mode. Distinct from the --dangerouslySkipPermissions CLI-flag
        // synthetic above (different trigger) and from the "Disable bypass-
        // permissions mode" Essentials card (the opposite intent). Queries
        // containing "disable" are excluded so "disable bypass" surfaces only the
        // lock-out card.
        const string bypassLower = "bypasspermissions";
        string queryLower = query.ToLowerInvariant();
        if (query.Length >= 3
            && (bypassLower.StartsWith(queryLower, StringComparison.Ordinal)
                || queryLower.Contains("bypass", StringComparison.Ordinal))
            && !queryLower.Contains("disable", StringComparison.Ordinal))
        {
            NavigationNodeViewModel? permNode = FindPermissionsNode();
            if (permNode is not null)
            {
                const string bypassDesc = "Set permissions.defaultMode = bypassPermissions to suppress all "
                                          + "tool permission prompts. Only use this in fully isolated environments.";
                SearchResults.Add(new SearchResultViewModel(
                    permNode,
                    _claudeCodeNavTitle,
                    "Permissions",
                    "permissions.defaultMode = bypassPermissions",
                    "permissions.defaultMode", // real sub-path key — deep-links to the Default Mode editor
                    "permissions.defaultMode = bypassPermissions",
                    bypassDesc)
                {
                    IsSynthetic = true,
                });

                // Suppress the OPPOSITE-INTENT "Disable bypass-permissions mode"
                // Essentials card for an enable-bypass query: its "disable bypass"
                // trigger substring-matches a bare "bypass" via the bidirectional
                // Essentials matcher, so without this both rows surface at once.
                SearchResultViewModel? disableCard = SearchResults.FirstOrDefault(
                    r => r.IsSynthetic && r.PropertyKey == EssentialsViewModel.CardIdDisableBypass);
                if (disableCard is not null)
                {
                    SearchResults.Remove(disableCard);
                }
            }
        }

        // Cache SDK schema-search results per section once per query so the
        // delegate is invoked at most once per product, not once per nav child.
        IReadOnlyList<SchemaSearchProvider>? providers = _getSchemaSearchProviders?.Invoke();
        Dictionary<string, IReadOnlyList<SchemaSearchResult>> sdkBySection = new(StringComparer.Ordinal);
        if (providers is not null)
        {
            foreach (SchemaSearchProvider p in providers)
            {
                sdkBySection[p.SectionTitle] = p.Search(query);
            }
        }

        int count = 0;
        foreach (NavigationNodeViewModel navNode in _getNavigationTree())
        {
            // Check both the header node and its children
            IEnumerable<NavigationNodeViewModel> nodesToSearch = navNode.Children.Count > 0
                ? navNode.Children.AsEnumerable()
                : new[] { navNode }.AsEnumerable();

            string sectionTitle = navNode.Title ?? string.Empty;
            sdkBySection.TryGetValue(sectionTitle, out IReadOnlyList<SchemaSearchResult>? sectionSdkHits);

            foreach (NavigationNodeViewModel child in nodesToSearch)
            {
                if (count >= 50)
                {
                    break;
                }

                if (child.Editor is SettingsGroupEditorViewModel groupEditor)
                {
                    // Standard pages: flatten all schema nodes (including nested objects
                    // like sandbox.allowUnsandboxedCommands) and match per-property.
                    foreach (Schema_SchemaNode schema in FlattenSchemaNodes(groupEditor.SchemaNodes))
                    {
                        if (count >= 50)
                        {
                            break;
                        }

                        string name = schema.Name ?? string.Empty;
                        string title = schema.Title ?? name;
                        string desc = schema.Description ?? string.Empty;
                        string path = schema.JsonPath ?? string.Empty;

                        if (!name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                            !title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                            !desc.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                            !path.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string snippet = BuildSnippet(desc, query, 70);
                        // Use JsonPath (e.g. "sandbox.allowUnsandboxedCommands") as the PropertyKey
                        // so that FilteredEditors can locate and highlight the correct editor.
                        SearchResults.Add(new SearchResultViewModel(child, sectionTitle, groupEditor.GroupName, title,
                            path, snippet, desc));
                        count++;
                    }
                }
                else if (child.Editor is not null)
                {
                    // Specialized editor pages (Permissions, Hooks, MCP Servers, …).
                    // Try SDK-backed property-level matches first; fall back to a
                    // page-title match only when no specific properties matched.
                    string pageTitle = child.Title ?? string.Empty;
                    string? ownedPrefix = GetOwnedJsonPathPrefix(child.Editor);
                    bool addedSpecific = false;

                    if (ownedPrefix is not null && sectionSdkHits is not null)
                    {
                        foreach (SchemaSearchResult hit in sectionSdkHits)
                        {
                            if (count >= 50)
                            {
                                break;
                            }

                            // Match the editor's owned JsonPath subtree (e.g. "permissions"
                            // matches "permissions" itself and "permissions.allow", but not
                            // "hooks.permissions").
                            string path = hit.JsonPath ?? string.Empty;
                            if (!path.Equals(ownedPrefix, StringComparison.OrdinalIgnoreCase) &&
                                !path.StartsWith(ownedPrefix + ".", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            string displayTitle = !string.IsNullOrWhiteSpace(hit.Title) ? hit.Title : hit.Name;
                            string snippet = !string.IsNullOrEmpty(hit.Snippet)
                                ? hit.Snippet
                                : BuildSnippet(hit.Description, query, 70);
                            SearchResults.Add(new SearchResultViewModel(
                                child, sectionTitle, pageTitle,
                                displayTitle, path, snippet, hit.Description));
                            count++;
                            addedSpecific = true;
                        }
                    }

                    // Page-title fallback — only when no property-level hit was added.
                    // Avoids the redundant "Permissions" row when "permissions.allow"
                    // already navigates the user to the same page with extra context.
                    if (!addedSpecific &&
                        (pageTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         query.Contains(pageTitle, StringComparison.OrdinalIgnoreCase)))
                    {
                        SearchResults.Add(new SearchResultViewModel(
                            child, sectionTitle, pageTitle, pageTitle,
                            string.Empty, string.Empty, string.Empty));
                        count++;
                    }
                }
            }

            if (count >= 50)
            {
                break;
            }
        }

        IsSearchOpen = SearchResults.Count > 0;
    }

    /// <summary>
    /// Maps a specialized editor view-model type to the JsonPath segment it owns
    /// (e.g. <see cref="PermissionsEditorViewModel"/> ⇒ <c>"permissions"</c>).
    /// Returns <see langword="null"/> for editors that are not backed by a single
    /// JsonPath subtree — those fall back to page-title matching.
    /// </summary>
    /// <remarks>
    /// Hardcoded rather than dispatched via a virtual property because the
    /// specialized editors are a small, closed set defined in this assembly. If
    /// you add a new specialized editor that is JsonPath-rooted, add a case here
    /// and a corresponding test in <c>SearchViewModelTests</c>.
    /// </remarks>
    private static string? GetOwnedJsonPathPrefix(object editor)
    {
        return editor switch
        {
            PermissionsEditorViewModel => "permissions",
            HooksEditorViewModel => "hooks",
            McpServersEditorViewModel => "mcpServers",
            var _ => null,
        };
    }

    /// <summary>
    /// Trigger phrases (lower-case, ordinal-comparison) for synthetic
    /// Essentials-card hits.  Each entry maps a card id (the
    /// <see cref="EssentialsCardViewModel.Id"/>) to the set of substrings
    /// the query is allowed to contain to surface that card.  Substrings
    /// rather than prefixes so a query like "thinking tokens" matches
    /// the same card as "max thinking" or "MAX_THINKING_TOKENS".
    /// </summary>
    /// <remarks>
    /// Kept hardcoded in this VM rather than on each card object so the
    /// search-side and the card-side lifecycles stay independent — search
    /// queries match card ids without needing the
    /// <see cref="EssentialsViewModel"/> instance to be alive (e.g. before
    /// the first navigation tree build).  A SearchViewModelTests fixture
    /// asserts every card id in <see cref="EssentialsViewModel"/> appears
    /// here so a future addition isn't silently skipped.
    /// </remarks>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>>
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
    /// Map from <see cref="EssentialsCardViewModel.Id"/> to a localised
    /// human-friendly card title for the search popup.  Not localised at
    /// table-build time because <see cref="Bennewitz.Ninja.ClaudeForge.Localization.Strings"/>
    /// may not be culture-aware until <c>ApplyCulture</c> runs in
    /// <c>Program.Main</c>; we look up at search time instead.
    /// </summary>
    private static string GetEssentialsCardTitle(string cardId)
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

    /// <summary>
    /// Walk the <see cref="EssentialsTriggers"/> table and add a synthetic
    /// hit for every card whose trigger phrase set contains the query.
    /// </summary>
    /// <remarks><c>internal</c> so unit tests can drive the table without
    /// standing up an Avalonia dispatcher.</remarks>
    internal void TryAddEssentialsSyntheticHits(string query)
    {
        NavigationNodeViewModel? essentialsNode = FindEssentialsNode();
        if (essentialsNode is null)
        {
            return;
        }

        string lower = query.ToLowerInvariant().Trim();
        if (lower.Length < 2)
        {
            return;
        }

        string? groupTitle = Strings.NavTitleEssentials;

        foreach ((string cardId, IReadOnlyList<string> triggers) in EssentialsTriggers)
        {
            // A card matches when the lowered query contains any trigger
            // OR any trigger contains the lowered query (so partial typing
            // — "san" prefixes "sandbox" — still surfaces the hit early).
            bool matched = false;
            foreach (string t in triggers)
            {
                if (lower.Contains(t, StringComparison.Ordinal) ||
                    t.Contains(lower, StringComparison.Ordinal))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                continue;
            }

            string title = GetEssentialsCardTitle(cardId);
            SearchResults.Add(new SearchResultViewModel(
                essentialsNode,
                _claudeCodeNavTitle,
                groupTitle,
                title,
                cardId, // PropertyKey == card id ⇒ amber callout target
                string.Empty,
                title)
            {
                IsSynthetic = true,
            });
        }
    }

    /// <summary>
    /// Returns the synthetic Essentials nav node, or <see langword="null"/>
    /// when the navigation tree hasn't been built yet (or has been rebuilt
    /// without the Essentials node — defensive: an upcoming refactor could
    /// gate the page behind a flag).
    /// </summary>
    private NavigationNodeViewModel? FindEssentialsNode()
    {
        IEnumerable<NavigationNodeViewModel> tree = _getNavigationTree();
        // Title comparison is hardcoded English (matches NavTitleEssentials in MWVM).
        return tree.FirstOrDefault(n => n.Title == "Essentials"
                                        && n.Editor is EssentialsViewModel);
    }

    /// <summary>
    /// Returns the "Permissions" child node under "Claude Code", or the first
    /// "Permissions" child anywhere in the tree if the Claude Code section is absent.
    /// Used to provide the navigation target for the synthetic
    /// <c>--dangerouslySkipPermissions</c> search result.
    /// </summary>
    private NavigationNodeViewModel? FindPermissionsNode()
    {
        IEnumerable<NavigationNodeViewModel> tree = _getNavigationTree();
        NavigationNodeViewModel? ccNode = tree.FirstOrDefault(n => n.Title == _claudeCodeNavTitle);
        return ccNode?.Children.FirstOrDefault(c => c.Title == "Permissions")
               ?? tree
                  .SelectMany(n => n.Children)
                  .FirstOrDefault(c => c.Title == "Permissions");
    }

    /// <summary>
    /// Yields every node in the tree depth-first, including nested
    /// <see cref="SchemaNode.Properties"/> of object-type nodes. Lets
    /// <see cref="ExecuteSearch"/> find properties whose only match is in a
    /// nested node's description (e.g. "dangerously" inside a child of the
    /// <c>sandbox</c> object).
    /// </summary>
    /// <remarks><c>internal</c> so unit tests cover the recursion directly.</remarks>
    internal static IEnumerable<Schema_SchemaNode> FlattenSchemaNodes(IEnumerable<Schema_SchemaNode> nodes)
    {
        foreach (Schema_SchemaNode node in nodes)
        {
            yield return node;
            foreach (Schema_SchemaNode descendant in FlattenSchemaNodes(node.Properties))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// If <paramref name="query"/> begins and ends with a matched pair of
    /// quote characters, returns the unquoted interior. Otherwise returns
    /// <paramref name="query"/> unchanged.  Recognised pairs: straight
    /// double <c>"…"</c>, straight single <c>'…'</c>, curly double
    /// <c>“…”</c>, and curly single <c>‘…’</c>.  Mismatched or single-side
    /// quotes are left intact so a user mid-typing a phrase doesn't
    /// inadvertently match every result.
    /// </summary>
    /// <remarks><c>internal</c> so unit tests can exercise the helper directly.</remarks>
    internal static string StripPhraseQuotes(string query)
    {
        if (query.Length < 2)
        {
            return query;
        }

        char first = query[0];
        char last = query[^1];
        bool isPair =
            (first == '"' && last == '"') ||
            (first == '\'' && last == '\'') ||
            (first == '“' && last == '”') || // “ ”
            (first == '‘' && last == '’'); // ‘ ’
        return isPair ? query[1..^1] : query;
    }

    internal static string BuildSnippet(string text, string query, int maxLen)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        int idx = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return text.Length <= maxLen ? text : text[..maxLen] + "…";
        }

        int start = Math.Max(0, idx - 20);
        int end = Math.Min(text.Length, idx + query.Length + 30);
        string snip = text[start..end];
        if (start > 0)
        {
            snip = "…" + snip;
        }

        if (end < text.Length)
        {
            snip += "…";
        }

        return snip;
    }

    /// <summary>
    /// Cancels any in-flight debounce timer and releases the CTS. Called
    /// from the parent's <see cref="MainWindowViewModel.Dispose"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }
}