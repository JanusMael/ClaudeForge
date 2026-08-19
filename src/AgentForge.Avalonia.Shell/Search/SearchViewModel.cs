using System.Collections.ObjectModel;

using Avalonia.Threading;

using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Serilog;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Search;

/// <summary>
/// Owns the search bar's debounced typing pipeline, the result-set observable,
/// and the schema-walk that produces match rows.
/// </summary>
/// <remarks>
/// <para>
/// The host view-model still owns navigation state — the selected node, the
/// back-stack, and the command that reacts to a row click. The view binds that
/// command directly to the host, so this VM exposes no row-selection command of
/// its own and never navigates anything itself.
/// </para>
/// <para>
/// Product knowledge reaches this class only as data it is handed: the
/// <see cref="SyntheticSearchEntry"/> list (which hand-written rows exist, and
/// what they mean), the <see cref="SchemaSearchProvider"/> delegates (one per
/// open product), and the two editor interfaces in <c>SearchableEditors.cs</c>
/// (what shape a page is). Nothing below names a product: debounce, quote
/// stripping, the tree walk, the flattening, the snippet and the result cap are
/// the same whichever product is being edited.
/// </para>
/// <para>
/// Threading: <see cref="OnSearchQueryChanged(string)"/> runs on the UI thread and
/// schedules the actual matching pass via <see cref="Dispatcher.UIThread"/>
/// after a 200 ms debounce. The debounce CTS is cancelled and disposed paired
/// with each new keystroke and finally on <see cref="Dispose"/>.
/// </para>
/// </remarks>
public sealed partial class SearchViewModel : ObservableObject, IDisposable
{
    /// <summary>Most rows the popup will show for one query.</summary>
    private const int MaxResults = 50;

    /// <summary>
    /// Snapshot accessor for the host's navigation tree. Re-read on every
    /// search pass so a tree rebuild (workspace reload, project switch) is
    /// reflected without re-creating the search VM.
    /// </summary>
    private readonly Func<IEnumerable<NavigationNodeViewModel>> _getNavigationTree;

    /// <summary>True while the host is mid-load; suppresses search execution.</summary>
    private readonly Func<bool> _isLoadingProbe;

    /// <summary>
    /// The hosted products' hand-written rows. Re-evaluated on every search pass
    /// for two reasons: the entries may carry localized text, and localization is
    /// not culture-aware until the host applies a culture at startup; and the set
    /// itself can depend on which products are currently open.
    /// <para>
    /// Optional, and <see langword="null"/> means "this host pins no rows" — a
    /// missing feature, not a wrong answer, which is why a default is safe here
    /// even though it would not be for a merge policy or a scope ladder. Those
    /// have no neutral value; an empty row list does.
    /// </para>
    /// </summary>
    private readonly Func<IReadOnlyList<SyntheticSearchEntry>>? _getSyntheticEntries;

    /// <summary>
    /// Optional per-product delegates onto the SDK's
    /// <see cref="IAgentConfigClient.SearchSchema"/>. Re-evaluated on every search
    /// pass so a workspace reload (which rebuilds the SDK clients) is reflected
    /// without re-creating the search VM. When <see langword="null"/>, specialised
    /// editors fall back to title-only matching.
    /// </summary>
    private readonly Func<IReadOnlyList<SchemaSearchProvider>>? _getSchemaSearchProviders;

    private CancellationTokenSource? _searchCts;
    private bool _disposed;

    public SearchViewModel(
        Func<IEnumerable<NavigationNodeViewModel>> getNavigationTree,
        Func<bool> isLoadingProbe,
        Func<IReadOnlyList<SyntheticSearchEntry>>? getSyntheticEntries = null,
        Func<IReadOnlyList<SchemaSearchProvider>>? getSchemaSearchProviders = null)
    {
        _getNavigationTree = getNavigationTree ?? throw new ArgumentNullException(nameof(getNavigationTree));
        _isLoadingProbe = isLoadingProbe ?? throw new ArgumentNullException(nameof(isLoadingProbe));
        _getSyntheticEntries = getSyntheticEntries;
        _getSchemaSearchProviders = getSchemaSearchProviders;
    }

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isSearchOpen;

    /// <summary>
    /// Match rows for the current <see cref="SearchQuery"/>. Cleared on empty
    /// input; capped at <see cref="MaxResults"/> schema rows to keep the dropdown
    /// navigable. Synthetic rows are pinned above the cap — a product pins few
    /// enough of them that they are always worth showing.
    /// </summary>
    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = [];

    /// <summary>
    /// Debouncer + dispatcher pivot for typing input. The leak-free CTS swap
    /// (capture-previous, publish-new, then-cancel-old) is load-bearing: the
    /// original code cancelled then reassigned, leaking one CTS per keystroke.
    /// </summary>
    partial void OnSearchQueryChanged(string value)
    {
        // Capture the previous CTS so it can be disposed *after* its replacement
        // is published. CancellationTokenSource holds OS-level handles internally.
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
    /// matches against name / title / description / JSON path, preceded by
    /// whatever hand-written rows the hosted products pin for this query.
    /// </summary>
    /// <remarks>
    /// Exposed as <c>internal</c> so unit tests can drive the matching pass
    /// directly without standing up an Avalonia dispatcher (the public
    /// <see cref="OnSearchQueryChanged(string)"/> path is debounce +
    /// dispatcher-pivot only, not testable headlessly).
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

        // Synthetic rows go in FIRST so they sit at the top of the list, ahead of
        // schema property matches, and they do not consume the result budget.
        AddSyntheticHits(query);

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
                if (count >= MaxResults)
                {
                    break;
                }

                if (child.Editor is ISchemaGroupEditor groupEditor)
                {
                    // Schema-driven pages: flatten all schema nodes (including nested
                    // objects like sandbox.allowUnsandboxedCommands) and match per-property.
                    foreach (SchemaNode schema in FlattenSchemaNodes(groupEditor.SchemaNodes))
                    {
                        if (count >= MaxResults)
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
                        // so that the target editor can locate and highlight the correct property.
                        SearchResults.Add(new SearchResultViewModel(child, sectionTitle, groupEditor.GroupName, title,
                            path, snippet, desc));
                        count++;
                    }
                }
                else if (child.Editor is not null)
                {
                    // Specialised editor pages. Try SDK-backed property-level matches
                    // first; fall back to a page-title match only when no specific
                    // properties matched.
                    string pageTitle = child.Title ?? string.Empty;
                    string? ownedPrefix = (child.Editor as IJsonPathScopedEditor)?.OwnedJsonPathPrefix;
                    bool addedSpecific = false;

                    if (ownedPrefix is not null && sectionSdkHits is not null)
                    {
                        foreach (SchemaSearchResult hit in sectionSdkHits)
                        {
                            if (count >= MaxResults)
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

            if (count >= MaxResults)
            {
                break;
            }
        }

        IsSearchOpen = SearchResults.Count > 0;
    }

    /// <summary>
    /// Walk the hosted products' <see cref="SyntheticSearchEntry"/> list and add a
    /// row for every entry whose trigger matches and whose target node exists,
    /// then drop the rows those matches displace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The query is lower-cased and <em>trimmed</em> once here, and every
    /// <see cref="SearchTrigger"/> rule then compares ordinally against that one
    /// normalised form. Trimming is a deliberate widening: the trigger rules used
    /// to disagree about it, so a leading space stopped a prefix rule from firing
    /// while a contains rule on the same row still fired. One normalisation is the
    /// only way a declarative rule set stays predictable.
    /// </para>
    /// <para><c>internal</c> so unit tests can drive the table without a dispatcher.</para>
    /// </remarks>
    internal void AddSyntheticHits(string query)
    {
        IReadOnlyList<SyntheticSearchEntry>? entries = _getSyntheticEntries?.Invoke();
        if (entries is null || entries.Count == 0)
        {
            return;
        }

        string normalized = query.ToLowerInvariant().Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        IEnumerable<NavigationNodeViewModel> tree = _getNavigationTree();

        // Rows are collected before publishing so suppression is order-independent:
        // an entry displaces its opposite whether it was declared before or after it.
        List<(SyntheticSearchEntry Entry, SearchResultViewModel Row)> matched = [];
        HashSet<string> suppressed = new(StringComparer.Ordinal);

        foreach (SyntheticSearchEntry entry in entries)
        {
            if (!entry.Trigger.Matches(normalized))
            {
                continue;
            }

            NavigationNodeViewModel? target = entry.FindTarget(tree);
            if (target is null)
            {
                // The page this row points at is not in this install's tree.
                // Dropping the row silently is correct — and it must not suppress
                // anything either, or a missing page would hide a present one.
                continue;
            }

            matched.Add((entry, new SearchResultViewModel(
                target,
                entry.SectionTitle,
                entry.GroupTitle,
                entry.DisplayName,
                entry.PropertyKey,
                entry.Snippet,
                entry.Description)
            {
                IsSynthetic = true,
            }));

            foreach (string id in entry.Suppresses)
            {
                suppressed.Add(id);
            }
        }

        foreach ((SyntheticSearchEntry entry, SearchResultViewModel row) in matched)
        {
            if (!suppressed.Contains(entry.Id))
            {
                SearchResults.Add(row);
            }
        }
    }

    /// <summary>
    /// Yields every node in the tree depth-first, including nested
    /// <see cref="SchemaNode.Properties"/> of object-type nodes. Lets
    /// <see cref="ExecuteSearch"/> find properties whose only match is in a
    /// nested node's description (e.g. "dangerously" inside a child of the
    /// <c>sandbox</c> object).
    /// </summary>
    /// <remarks><c>internal</c> so unit tests cover the recursion directly.</remarks>
    internal static IEnumerable<SchemaNode> FlattenSchemaNodes(IEnumerable<SchemaNode> nodes)
    {
        foreach (SchemaNode node in nodes)
        {
            yield return node;
            foreach (SchemaNode descendant in FlattenSchemaNodes(node.Properties))
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
    /// Cancels any in-flight debounce timer and releases the CTS. Called from the
    /// host view-model's own disposal.
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
