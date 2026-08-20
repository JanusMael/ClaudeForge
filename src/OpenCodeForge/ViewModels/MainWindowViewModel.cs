using System.Collections.ObjectModel;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Navigation;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Search;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Adapters;
using Bennewitz.Ninja.OpenCodeForge.Localization;
using Bennewitz.Ninja.OpenCodeForge.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;

namespace Bennewitz.Ninja.OpenCodeForge.ViewModels;

/// <summary>One hosted product: its client, its schema, and its page layout.</summary>
/// <param name="Product">Which product this section edits.</param>
/// <param name="Client">The already-constructed client for it.</param>
/// <param name="Layout">How its schema keys bucket into pages.</param>
/// <param name="HeaderText">Navigation header, localized.</param>
public sealed record HostedSection(
    ProductDescriptor Product,
    AgentConfigClientCore Client,
    SchemaPageLayout Layout,
    Func<string> HeaderText);

/// <summary>
/// The window's view-model: opens both OpenCode configurations and builds a settings page per
/// schema group.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately thin. Every non-trivial piece is either the shell's
/// (<see cref="SchemaPageLayout"/>, <see cref="SettingsGroupEditorViewModel"/>) or this product's
/// SDK — the point of the extraction phases was that a second app needs composition, not
/// machinery.
/// </para>
/// <para>
/// ⚠ <b>Settings pages only, by decision.</b> There is no backup, restore, profile or memory
/// surface here yet. Several of those depend on services still shaped around the other product
/// (footprint categories, the archive layout, backup modes), and the plan assigns them their own
/// phases. Adding them now would force those decisions early and out of order.
/// </para>
/// </remarks>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly OpenCodeEditorFactory _editorFactory = new();

    /// <summary>Sections in navigation order.</summary>
    public IReadOnlyList<HostedSection> Sections { get; }

    /// <summary>The navigation tree: one header per section, one child per settings page.</summary>
    public ObservableCollection<NavigationNodeViewModel> Navigation { get; } = [];

    /// <summary>Window title.</summary>
    public string Title => Strings.AppTitle;

    /// <summary>The page whose editor is showing.</summary>
    [ObservableProperty] private NavigationNodeViewModel? _selectedNode;

    /// <summary>
    /// Install state of the agent this app configures.
    /// </summary>
    /// <remarks>
    /// Shown as a banner rather than blocking anything. Editing a config for a not-yet-installed
    /// agent is legitimate — provisioning a machine, or fixing a config that broke the install —
    /// so detection informs and never prevents.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstallBanner))]
    [NotifyPropertyChangedFor(nameof(InstallBannerText))]
    private OpenCodeInstallStatus _installStatus = OpenCodeInstallStatus.NotFound;

    /// <summary>
    /// True once detection has run AND found nothing.
    /// </summary>
    /// <remarks>
    /// ⚠ Gated on <see cref="HasProbedForInstall"/> so the banner does not flash during startup.
    /// InstallStatus begins as NotFound, which is indistinguishable from a completed negative
    /// probe — without the gate every launch would show "not detected" for a moment.
    /// </remarks>
    public bool ShowInstallBanner => HasProbedForInstall && !InstallStatus.IsInstalled;

    /// <summary>Whether detection has completed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstallBanner))]
    private bool _hasProbedForInstall;

    /// <summary>Banner text: what was not found, and that editing still works.</summary>
    public string InstallBannerText =>
        "OpenCode was not detected on this machine. You can still edit its configuration — "
        + "the settings below are saved to disk either way.";

    /// <summary>Ways to install it, for the platform in use.</summary>
    public IReadOnlyList<InstallOption> InstallOptions { get; } =
        OpenCodeInstallCommands.ForCurrentPlatform();

    /// <summary>Status line — also where a load failure surfaces.</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>True while <see cref="InitializeAsync"/> is running.</summary>
    /// <remarks>
    /// Search consults this so a query typed during startup does not report "no results" against
    /// a tree that is still empty — the shell shows a loading state instead.
    /// </remarks>
    // Starts true: the window is shown before InitializeAsync finishes, and a query typed in
    // that window would otherwise be answered "no results" against an empty tree.
    [ObservableProperty] private bool _isLoading = true;

    /// <summary>
    /// The chosen search result. Setting it navigates to that page and closes the search.
    /// </summary>
    /// <remarks>
    /// Clearing the query afterwards is deliberate: leaving the result list up after navigating
    /// hides the page the user just asked for. The property resets itself to null so selecting
    /// the same result twice in a row navigates both times.
    /// </remarks>
    [ObservableProperty] private SearchResultViewModel? _selectedSearchResult;

    partial void OnSelectedSearchResultChanged(SearchResultViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedNode = value.Node;
        Search.SearchQuery = string.Empty;
        SelectedSearchResult = null;
    }

    /// <summary>Global search over the navigation tree, the schema, and the synthetic table.</summary>
    /// <remarks>
    /// Every piece of matching, ordering and suppression is the shell's. What this app supplies is
    /// three callbacks: the tree, the synthetic entries, and one schema-search provider per
    /// section. That is the whole cost of search for a second app.
    /// </remarks>
    public SearchViewModel Search { get; }

    /// <summary>Construct with this app's two products.</summary>
    public MainWindowViewModel()
        : this(
            new HostedSection(OpenCodeProducts.Config, new OpenCodeClient(),
                OpenCodePageLayout.Config, () => Strings.SectionOpenCode),
            new HostedSection(OpenCodeProducts.Tui, new OpenCodeTuiClient(),
                OpenCodePageLayout.Tui, () => Strings.SectionOpenCodeTui))
    {
    }

    /// <summary>Construct with an explicit section list. Test seam.</summary>
    /// <remarks>
    /// The sections are a required argument rather than defaulted, so a test cannot accidentally
    /// exercise the real user's configuration files.
    /// </remarks>
    public MainWindowViewModel(params HostedSection[] sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        if (sections.Length == 0)
        {
            throw new ArgumentException("At least one section is required.", nameof(sections));
        }

        Sections = [.. sections];

        Search = new SearchViewModel(
            getNavigationTree: () => Navigation,
            isLoadingProbe: () => IsLoading,
            getSyntheticEntries: () => OpenCodeSyntheticSearch.Build(Strings.SectionOpenCode),
            getSchemaSearchProviders: BuildSchemaSearchProviders);
    }

    /// <summary>One schema-search provider per loaded section.</summary>
    /// <remarks>
    /// The client is captured in a local per iteration rather than read from the section inside
    /// the lambda: a reload swaps the client, and a lambda that re-read it would search a
    /// half-replaced one.
    /// </remarks>
    internal IReadOnlyList<SchemaSearchProvider> BuildSchemaSearchProviders()
    {
        List<SchemaSearchProvider> providers = new(Sections.Count);
        foreach (HostedSection section in Sections)
        {
            AgentConfigClientCore client = section.Client;
            providers.Add(new SchemaSearchProvider(section.HeaderText(), q => client.SearchSchema(q)));
        }

        return providers;
    }

    /// <summary>
    /// Open every section's configuration and build its pages.
    /// </summary>
    /// <remarks>
    /// One section failing must not take the others down — a user with no TUI config should still
    /// get their main configuration, so each section is opened independently and its failure is
    /// reported rather than thrown.
    /// </remarks>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        SchemaRegistry registry = new();
        List<string> failures = [];
        IsLoading = true;

        foreach (HostedSection section in Sections)
        {
            try
            {
                await section.Client.OpenAsync(projectRoot: null, ct).ConfigureAwait(false);
                IReadOnlyList<NavigationNodeViewModel> pages =
                    await BuildPagesAsync(registry, section, ct).ConfigureAwait(false);

                // Expanded on arrival: a collapsed header hides every page behind a click, and
                // one of those pages is already selected — the user would see an empty-looking
                // tree beside a populated editor.
                NavigationNodeViewModel header = new(section.HeaderText()) { IsExpanded = true };
                foreach (NavigationNodeViewModel page in pages)
                {
                    header.Children.Add(page);
                }

                Navigation.Add(header);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error(ex, "[Init] section {Product} failed to load", section.Product.Id);
                failures.Add(section.Product.DisplayName);
            }
        }

        // Detection last: it runs a child process, and a slow or hung binary must not delay the
        // settings pages the user came for.
        InstallStatus = await OpenCodeInstallProbe.DetectAsync(ct).ConfigureAwait(false);
        HasProbedForInstall = true;

        IsLoading = false;
        SelectedNode = Navigation.FirstOrDefault()?.Children.FirstOrDefault();
        Status = failures.Count == 0
            ? string.Empty
            : $"Could not load: {string.Join(", ", failures)}. See the log for details.";
    }

    private async Task<IReadOnlyList<NavigationNodeViewModel>> BuildPagesAsync(
        SchemaRegistry registry, HostedSection section, CancellationToken ct)
    {
        var root = await registry.GetSettingsNodeAsync(section.Product, ct).ConfigureAwait(false);
        IReadOnlyList<SchemaNode> nodes = SchemaTreeBuilder.BuildTopLevel(root);

        SettingsWorkspace? workspace = section.Client.WorkspaceForGui;
        if (workspace is null)
        {
            // Nothing to edit and nothing to show: a section whose workspace never materialised
            // would otherwise produce pages bound to null and fail at first render.
            throw new InvalidOperationException(
                $"'{section.Product.Id}' opened without producing a workspace.");
        }

        // One shared scope context per section, so changing scope on any of its pages moves them
        // all — the pages of one product are one editing surface.
        SharedScopeContext scope = new(section.Client.EditableScopes.FirstOrDefault());
        scope.AvailableScopes = section.Client.EditableScopes;

        List<NavigationNodeViewModel> pages = [];
        foreach (SchemaPage page in section.Layout.Arrange(nodes))
        {
            SettingsGroupEditorViewModel editor = new(
                page.Title,
                page.Nodes,
                workspace,
                scope,
                _editorFactory,
                OpenCodeSettingsGroupText.Create(),
                groupDescription: page.Description,
                sdkClient: section.Client);

            pages.Add(new NavigationNodeViewModel(page.Title) { Editor = editor });
        }

        return pages;
    }
}
