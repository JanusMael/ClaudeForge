using System.Globalization;
using System.Collections.ObjectModel;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Backup;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Navigation;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Search;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.AgentForge.Core.Updates;
using Bennewitz.Ninja.AgentForge.Sdk;
using Avalonia.Threading;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Messages;
using Bennewitz.Ninja.OpenCode.Avalonia.Artifacts;
using Bennewitz.Ninja.OpenCode.Avalonia.Essentials;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Adapters;
using Bennewitz.Ninja.OpenCodeForge.Localization;
using Bennewitz.Ninja.OpenCodeForge.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace Bennewitz.Ninja.OpenCodeForge.ViewModels;

/// <summary>One hosted product: its client, its schema, and its page layout.</summary>
/// <param name="Product">Which product this section edits.</param>
/// <param name="Client">The already-constructed client for it.</param>
/// <param name="Layout">How its schema keys bucket into pages.</param>
/// <param name="HeaderText">Navigation header, localized.</param>
/// <param name="Danger">
/// Which of this document's settings deserve attention, or <see langword="null"/> for none.
/// <para>
/// ⚠ <b>Per DOCUMENT, not per product.</b> <c>opencode.json</c> and <c>tui.json</c> carry separate
/// tables, so this belongs beside <see cref="Layout"/> — the other thing that is already paired
/// per document — rather than on <see cref="ProductDescriptor"/>, which is a persisted data record
/// and has no business holding a service.
/// </para>
/// </param>
public sealed record HostedSection(
    ProductDescriptor Product,
    AgentConfigClientCore Client,
    SchemaPageLayout Layout,
    Func<string> HeaderText,
    IDangerClassifier? Danger = null);

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
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Deep-link and persisted-state key for the artifacts page.
    /// </summary>
    /// <remarks>
    /// A constant rather than the display title, because <see cref="NavigationNodeViewModel.Title"/>
    /// is a display label and this one is localized — matching on it would break the moment a
    /// translation lands.
    /// </remarks>
    public const string ArtifactsNodeId = "artifacts";

    /// <summary>Deep-link and persisted-state key for the Essentials page.</summary>
    public const string EssentialsNodeId = "essentials";

    /// <summary>Deep-link and persisted-state key for the Backup / Restore page.</summary>
    public const string BackupNodeId = "backup-restore";

    /// <summary>Deep-link and persisted-state key for the disk-footprint page.</summary>
    public const string FootprintNodeId = "footprint";

    /// <summary>Card id for the auto-update opt-out on the Essentials page.</summary>
    /// <remarks>
    /// Public so a test can find that card among the schema-backed ones without matching on its
    /// display text, which is localized.
    /// </remarks>
    public const string EssentialsCheckForUpdatesCardId = "app.checkForUpdatesOnLaunch";

    /// <summary>Sections in navigation order.</summary>
    public IReadOnlyList<HostedSection> Sections { get; }

    /// <summary>
    /// The Backup / Restore page's view-model, once the nav tree has been built.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Held in a field because <see cref="BackupRestoreViewModel.Dispose"/> cancels an
    /// in-flight backup.</b> A page rebuilt on every navigation would abort a running backup the
    /// moment the user clicked elsewhere to wait it out. It is also what lets
    /// <see cref="OnBackupStateChanged"/> read the page's directories back without going through
    /// the tree.
    /// </remarks>
    private BackupRestoreViewModel? _backupVm;

    /// <summary>
    /// The disk-footprint page's view-model, once the nav tree has been built.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Cached for a different reason from <see cref="_backupVm"/>.</b> That one is held
    /// because disposing it aborts an in-flight backup; this one holds no cancellable work and
    /// nothing to dispose. It is cached because <c>InitializeAsync</c> can run more than once and a
    /// second page would leave the first subscribed to nothing but still measuring — and because
    /// the rows already on screen should survive a rebuild rather than blanking while the walk
    /// re-runs.
    /// </remarks>
    private OpenCodeFootprintViewModel? _footprintVm;

    /// <summary>
    /// The dialog service the Backup page prompts through.
    /// </summary>
    /// <remarks>
    /// Constructed here rather than injected, unlike the sibling app: this window has no other
    /// dialog-bearing page yet, and <see cref="AvaloniaDialogService"/> resolves the owner window
    /// from the application lifetime on each call, so it needs nothing at construction. ⚠ Its
    /// <c>RegisterSaveChangesDialog</c> hook is deliberately left unregistered — the only caller is
    /// <c>ShowSaveChangesDialogAsync</c>, which this app reaches only through the Backup page's
    /// save-before-backup bridge, and that bridge is not wired (see <c>BuildBackupNode</c>).
    /// </remarks>
    private readonly AvaloniaDialogService _dialogService = new();

    /// <summary>The navigation tree: one header per section, one child per settings page.</summary>
    public ObservableCollection<NavigationNodeViewModel> Navigation { get; } = [];

    /// <summary>Window title.</summary>
    public string Title => Strings.AppTitle;

    /// <summary>This build's version, for the status-bar button and the About dialog.</summary>
    public static string AppVersion => BackupConstants.AppVersion;

    /// <summary>
    /// The registry the pages were built from, kept so a mid-session schema check re-fetches
    /// into the same instance the badges report on.
    /// </summary>
    /// <remarks>
    /// ⚠ Assigned by <see cref="InitializeAsync"/>, so it is null until then — a check
    /// triggered before the window has loaded has nothing to refresh, and says so rather than
    /// quietly building a second registry whose results no badge would reflect.
    /// </remarks>
    private SchemaRegistry? _registry;

    /// <summary>The page whose editor is showing.</summary>
    [ObservableProperty] private NavigationNodeViewModel? _selectedNode;

    /// <summary>
    /// Let a page act on being arrived at or left.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Added with the first page that needs it, not speculatively.</b> Two of the Essentials
    /// cards report the filesystem rather than the document — a global <c>AGENTS.md</c> can appear
    /// while the app is open — so without this dispatch they would show whatever was true at
    /// startup, for the whole session. The interface has default no-op bodies, so every existing
    /// page is unaffected.
    /// </remarks>
    partial void OnSelectedNodeChanged(
        NavigationNodeViewModel? oldValue, NavigationNodeViewModel? newValue)
    {
        if (oldValue?.Editor is INavigablePage leaving)
        {
            leaving.OnNavigatedFrom(!ReferenceEquals(oldValue.Editor, newValue?.Editor));
        }

        if (newValue?.Editor is INavigablePage entering)
        {
            entering.OnNavigatedTo();
        }
    }

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
                OpenCodePageLayout.Config, () => Strings.SectionOpenCode,
                OpenCodeDangerTable.Config),
            new HostedSection(OpenCodeProducts.Tui, new OpenCodeTuiClient(),
                OpenCodePageLayout.Tui, () => Strings.SectionOpenCodeTui,
                OpenCodeDangerTable.Tui))
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

        // Subscribed here rather than at the loop's start: the dismiss can arrive before the
        // launch check has finished, and the latch it sets is what stops the loop being started
        // at all.
        UpdateBanner.Dismissed += OnUpdateBannerDismissed;

        Search = new SearchViewModel(
            getNavigationTree: () => Navigation,
            isLoadingProbe: () => IsLoading,
            getSyntheticEntries: () => OpenCodeSyntheticSearch.Build(Strings.SectionOpenCode),
            getSchemaSearchProviders: BuildSchemaSearchProviders);

        // The Essentials cards' "View in <page>" buttons publish this. Without a subscriber the
        // button is a control that does nothing — which looks like a broken app, not a missing
        // feature — so it is registered here rather than left for the slice that adds more cards.
        WeakReferenceMessenger.Default.Register<MainWindowViewModel, NavigateToNavGroupMessage>(
            this, static (recipient, message) => recipient.OnNavigateToNavGroup(message));
    }

    /// <summary>
    /// Deep-link from an Essentials card: select the page whose title matches, and filter its
    /// editor to the named property when it has one.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Silently no-ops on no match, deliberately.</b> The alternative is throwing from a
    /// button click on a mis-titled card, and the guard that catches the real mistake is a test
    /// asserting every card's target resolves — not a runtime crash in the user's face.
    /// </remarks>
    private void OnNavigateToNavGroup(NavigateToNavGroupMessage message)
    {
        if (string.IsNullOrEmpty(message.GroupTitle))
        {
            return;
        }

        NavigationNodeViewModel? target = FindNodeByTitle(message.GroupTitle);
        if (target is null)
        {
            return;
        }

        SelectedNode = target;

        if (!string.IsNullOrEmpty(message.PropertyFilter)
            && target.Editor is SettingsGroupEditorViewModel groupEditor)
        {
            groupEditor.ApplyNavigationFilter(message.PropertyFilter);
        }
    }

    /// <summary>
    /// The first node titled <paramref name="title"/> — top level first, then section children.
    /// </summary>
    internal NavigationNodeViewModel? FindNodeByTitle(string title)
    {
        foreach (NavigationNodeViewModel node in Navigation)
        {
            if (string.Equals(node.Title, title, StringComparison.Ordinal))
            {
                return node;
            }
        }

        // ⚠ Both sections' pages are searched, and the two documents share page titles
        // ("General" exists in the config layout and could in the TUI one), so the FIRST match
        // wins and section order decides. That is only safe while the cards all belong to the
        // config document; a TUI card would need the section in the message.
        foreach (NavigationNodeViewModel header in Navigation)
        {
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
    /// <param name="schemaRegistry">
    /// The registry the pages are built from. <see langword="null"/> creates one WITH network
    /// access, which is what the app wants.
    /// <para>
    /// ⚠ A test seam, and it exists because the provenance badge is otherwise untestable: the
    /// badge reports whether a schema was fetched or bundled, and a test that cannot control
    /// the network cannot assert either state without depending on the machine it runs on.
    /// Mirrors <c>AgentConfigClientCore</c>, which already takes one for the same reason.
    /// </para>
    /// </param>
    public async Task InitializeAsync(
        CancellationToken ct = default, SchemaRegistry? schemaRegistry = null)
    {
        // CreateWithNetwork, not `new`: a bare registry is OFFLINE by design.
        //
        // ⛔ The cache directory is THIS APP'S, not OpenCode's own cache root. Three reasons, and
        // the first two are the ones that bite: `~/.cache/opencode` is a directory OpenCode
        // manages and may clear, and it is one of the roots the disk-footprint page MEASURES — so
        // the app's schema cache would show up as OpenCode's disk usage and invite the user to
        // delete it. Beside the window-state file instead, which is where this app already keeps
        // its own things, and which follows $OPENCODE_CONFIG_DIR so a redirected install (and
        // every test that redirects) stays isolated for free.
        SchemaRegistry registry = schemaRegistry ?? SchemaRegistry.CreateWithNetwork(
            cacheDirectory: Path.Combine(
                OpenCodePaths.GlobalDirectory(OpenCodeEnvironment.FromProcess()), "cache", "schemas"));
        _registry = registry;
        List<string> failures = [];
        IsLoading = true;

        // The section whose document holds the Essentials-page keys, once it has opened. Captured
        // in the loop rather than looked up afterwards so a section that FAILED to open is never
        // handed to a card — GetEffective on an unopened client throws, and it would throw inside
        // a fire-and-forget read.
        HostedSection? essentialsSection = null;

        foreach (HostedSection section in Sections)
        {
            try
            {
                await section.Client.OpenAsync(projectRoot: null, ct).ConfigureAwait(false);

                if (section.Product == OpenCodeProducts.Config)
                {
                    essentialsSection = section;
                }

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

                ApplyProvenanceBadge(header, registry, section);

                Navigation.Add(header);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error(ex, "[Init] section {Product} failed to load", section.Product.Id);
                failures.Add(section.Product.DisplayName);
            }
        }

        // The artifacts page, after the settings sections and outside the loop: it is not a
        // schema section and has no client to open, and it must appear even when every section
        // above failed — a user whose config is too broken to load is exactly the user who needs
        // to see which files OpenCode is reading.
        Navigation.Add(new NavigationNodeViewModel(Strings.SectionArtifacts)
        {
            NodeId = ArtifactsNodeId,
            IsTopLevel = true,
            Editor = new OpenCodeArtifactsPageViewModel(OpenCodeEnvironment.FromProcess(), null),
        });

        // Backup / Restore, after the artifacts page and outside the loop for the same reasons: no
        // schema section, no client to open, and it has to appear even when every section above
        // failed — a user whose config will not load is exactly the user reaching for a restore.
        Navigation.Add(BuildBackupNode());

        // The disk-footprint page, beside Backup and for the same reasons — plus one of its own:
        // it reads the filesystem rather than any document, so it is the page most likely to still
        // be useful on a machine whose config will not parse.
        //
        // ⛔ Handed the CONFIG section's client, and it must be a client rather than a locally
        // built service — see OpenCodeFootprintViewModel's remarks. Taken from `Sections` rather
        // than from the loop's `essentialsSection`, because that variable is only assigned when the
        // section OPENED, and this page owes nothing to an opened document.
        Navigation.Add(new NavigationNodeViewModel(Strings.HeadingFootprint)
        {
            NodeId = FootprintNodeId,
            IsTopLevel = true,
            Editor = _footprintVm ??= new OpenCodeFootprintViewModel(
                Sections.First(s => s.Product == OpenCodeProducts.Config).Client),
        });

        // Essentials goes FIRST, and is inserted rather than appended because it is built last:
        // its editable card needs a client that has finished opening. Like the artifacts page it
        // appears even when nothing loaded — its derived cards read the environment and the
        // filesystem, so "which file was this app even trying to open?" still has an answer.
        Navigation.Insert(0, new NavigationNodeViewModel(Strings.SectionEssentials)
        {
            NodeId = EssentialsNodeId,
            IsTopLevel = true,
            Editor = new OpenCodeEssentialsViewModel(
                essentialsSection?.Client,
                OpenCodeEnvironment.FromProcess(),
                essentialsSection?.Layout ?? OpenCodePageLayout.Config,

                // ⚠ Taken from the section list, NOT from essentialsSection — the danger table is
                // a static tiering of keys and needs no open client, while the CLIENT above must
                // have opened (GetEffective throws otherwise, inside a fire-and-forget read).
                // Gating the table on the client too would grey out every dot on a section whose
                // open threw, which is the one place the tiers are the only thing still working.
                // ⓘ Note this is NOT the malformed-file case: ConfigFileLoader catches
                // JsonException on purpose and loads an unparseable file as an empty root with
                // SettingsDocument.LoadFailure set, so a broken file opens successfully.
                Sections.FirstOrDefault(s => s.Product == OpenCodeProducts.Config)?.Danger,

                // The app's own preferences. Supplied from here because only the app knows where
                // its state file is — OpenCode.Avalonia sits below this assembly and cannot reach
                // WindowStateService. The TEXT comes from this app's resx too, so the strings the
                // card shows are the same ones used anywhere else they appear.
                appPreferences:
                [
                    new EssentialsAppPreference(
                        Id: EssentialsCheckForUpdatesCardId,
                        Title: Strings.EssentialsCardCheckForUpdatesTitle,
                        Body: Strings.EssentialsCardCheckForUpdatesBody,
                        Get: () => WindowStateService.Load().CheckForUpdatesOnLaunch,
                        Set: WindowStateService.SaveCheckForUpdatesOnLaunch),
                ]),
        });

        // Detection last: it runs a child process, and a slow or hung binary must not delay the
        // settings pages the user came for.
        InstallStatus = await OpenCodeInstallProbe.DetectAsync(ct).ConfigureAwait(false);
        HasProbedForInstall = true;

        IsLoading = false;

        StartUpdateCheck();

        // ⚠ Explicitly the Essentials node, not Navigation[0].Children[0]. That expression used to
        // mean "the first section's first page"; inserting a childless top-level node at the front
        // silently turned it into null, leaving the window with a populated tree and an empty page
        // area. Naming the landing page says what is meant and cannot rot the same way.
        SelectedNode =
            Navigation.FirstOrDefault(n => string.Equals(n.NodeId, EssentialsNodeId, StringComparison.Ordinal))
            ?? Navigation.FirstOrDefault(n => n.Children.Count > 0)?.Children.FirstOrDefault();

        // ⚠ AFTER the landing page, never instead of it. An unresolvable --deep-link must leave a
        // working window on its usual page rather than an empty editor area, and assigning the
        // landing node first is what guarantees that without a second fallback expression here.
        ApplyDeepLinkIfRequested();

        Status = failures.Count == 0
            ? string.Empty
            : $"Could not load: {string.Join(", ", failures)}. See the log for details.";
    }

    /// <summary>
    /// Honour <c>--deep-link &lt;nodeId&gt;</c> by selecting that node, if it resolves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>This is the one thing that makes a page on this window observable.</b> Everything
    /// below a running window is stripped of the App's resource dictionaries and cannot
    /// instantiate views, so no test can look at a page; before this flag, looking at one meant
    /// clicking to it by hand, which is exactly the step that gets skipped.
    /// </para>
    /// <para>
    /// ⚠ <b>Searches top-level nodes AND their children, because the tree is two kinds of thing.</b>
    /// Essentials, Artifacts, Backup and Footprint are top-level and carry ids; the schema pages
    /// are children of a section header. A search that walked only the top level would silently
    /// fail to resolve every settings page, which is the larger half of the tree.
    /// </para>
    /// <para>
    /// ⚠ <b>Expands the parent of a child node.</b> Selecting a node inside a collapsed header
    /// shows the right editor beside a tree that does not show the selection — which reads as the
    /// flag having done nothing.
    /// </para>
    /// </remarks>
    private void ApplyDeepLinkIfRequested()
    {
        if (DebugFlags.DeepLinkNodeId is not { } requested)
        {
            return;
        }

        foreach (NavigationNodeViewModel top in Navigation)
        {
            if (string.Equals(top.NodeId, requested, StringComparison.Ordinal))
            {
                SelectedNode = top;
                Log.Information("[DeepLink] node={NodeId} resolved=true level=top", requested);
                return;
            }

            foreach (NavigationNodeViewModel child in top.Children)
            {
                if (!string.Equals(child.NodeId, requested, StringComparison.Ordinal))
                {
                    continue;
                }

                top.IsExpanded = true;
                SelectedNode = child;
                Log.Information("[DeepLink] node={NodeId} resolved=true level=child", requested);
                return;
            }
        }

        // Named rather than silent: an id that matches nothing is almost always a typo or a page
        // that failed to load, and both are things the person who passed the flag needs told.
        Log.Warning(
            "[DeepLink] node={NodeId} resolved=false; known ids: {Known}. Landing on {Landed}.",
            requested,
            string.Join(", ", Navigation.SelectMany(n => n.Children.Prepend(n))
                .Select(n => n.NodeId)
                .Where(id => !string.IsNullOrEmpty(id))),
            SelectedNode?.NodeId ?? "(nothing)");
    }

    /// <summary>
    /// Label a section header with which copy of its schema the pages beneath it were built from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>Network-first made this necessary.</b> A section's shape now comes either from the
    /// binary or from a download that happened moments ago, and until this badge nothing on
    /// screen — or in a screenshot attached to a bug report — distinguished them. "The editor
    /// shows a field I do not have" and "the editor is missing a field I do have" are both
    /// explained by provenance and by nothing else.
    /// </para>
    /// <para>
    /// ⚠ <b>Reads the registry that BUILT THE PAGES</b>, which is what the badge's wording
    /// claims and all it claims. Each client also holds its own registry and validates saves
    /// against that copy; the two fetch the same URL moments apart, so they agree in every
    /// non-pathological case, but they are not the same instance. Sharing one is worth doing and
    /// is deliberately not done here — it would change client construction, and this badge does
    /// not depend on it because it does not speak for save-validation.
    /// </para>
    /// <para>
    /// ⓘ <b>The null-provenance guard is defensive and currently UNREACHABLE</b>, which a canary
    /// established rather than reasoning: making it render "bundled" reddened nothing. By the
    /// time this runs, <c>BuildPagesAsync</c> has either recorded provenance or thrown — and a
    /// throw skips the header entirely. It stays because <c>ProvenanceFor</c> is genuinely
    /// nullable and an absent badge is the honest rendering, but do not go looking for the test
    /// that covers it.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The "Update available" banner's view-model, bound by <c>MainWindow.axaml</c>.
    /// </summary>
    /// <remarks>
    /// The banner's markup sets its own <c>DataContext</c> to this, so its bindings are
    /// unprefixed and it owns its own commands — no ancestor binding back to this view-model,
    /// which would resolve by reflection and trip trim analysis.
    /// </remarks>
    public UpdateBannerViewModel UpdateBanner { get; } = new();

    /// <summary>How long between automatic re-checks once the launch check has run.</summary>
    /// <remarks>
    /// Four hours, matching the sibling app. Long enough that a machine left open for a week
    /// makes a handful of requests, short enough that a long-running session still learns about
    /// a release the day it lands.
    /// </remarks>
    private static readonly TimeSpan UpdateRecheckInterval = TimeSpan.FromHours(4);

    /// <summary>Cancels the periodic re-check. Null when no loop is running.</summary>
    private CancellationTokenSource? _updateRecheckCts;

    /// <summary>Latched once the user dismisses the banner, so the loop does not re-raise it.</summary>
    private bool _updateBannerDismissed;

    private bool _disposed;

    /// <summary>
    /// Run the once-per-launch update check, then start the periodic re-check.
    /// </summary>
    /// <remarks>
    /// Fire-and-forget: the window is already usable and an update check must never be something
    /// the user waits behind. Every failure inside the check already collapses to "no update",
    /// so the <c>catch</c> is for the genuinely unexpected — and it logs rather than surfacing,
    /// because a failed update check is not a thing the user can act on.
    /// </remarks>
    private void StartUpdateCheck()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                UpdateCheckResult result =
                    await AppUpdateService.CheckOncePerLaunchAsync().ConfigureAwait(false);

                // Marshal before touching bound state: ApplyResult writes observable properties.
                await Dispatcher.UIThread.InvokeAsync(() => UpdateBanner.ApplyResult(result));
            }
            catch (Exception ex)
            {
                Log.Information(ex, "[UpdateCheck] Launch check threw unexpectedly; no banner.");
            }

            StartUpdateRecheckLoop();
        });
    }

    /// <summary>
    /// Re-check every <see cref="UpdateRecheckInterval"/> for the life of the window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ <b>This loop is the reason this view-model is <see cref="IDisposable"/>.</b> It is a
    /// detached task whose only stop signal is the token below; without disposal it would
    /// outlive the window that started it, keep waking every four hours, and keep marshalling to
    /// a dispatcher for a window that has gone. The loop and <see cref="Dispose"/> are one
    /// feature, not two.
    /// </para>
    /// <para>
    /// Uses <see cref="AppUpdateService.CheckPeriodicAsync"/> — no launch latch, so it fires
    /// repeatedly, but still gated on the user's opt-out, because a background timer is not
    /// consent the way a button press is. The banner keeps honouring the persisted per-version
    /// dismiss list, so only a genuinely newer release surfaces.
    /// </para>
    /// <para>
    /// ⚠ All view-model state is touched on the UI thread; only the network await runs off it.
    /// </para>
    /// </remarks>
    private void StartUpdateRecheckLoop()
    {
        if (_disposed || _updateBannerDismissed || _updateRecheckCts is not null)
        {
            return;
        }

        _updateRecheckCts = new CancellationTokenSource();
        CancellationToken ct = _updateRecheckCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(UpdateRecheckInterval, ct).ConfigureAwait(false);

                    UpdateCheckResult result =
                        await AppUpdateService.CheckPeriodicAsync(ct).ConfigureAwait(false);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        // A dismiss can race in between the delay elapsing and this marshal;
                        // do not resurrect a banner the user just closed.
                        if (!_updateBannerDismissed && !_disposed)
                        {
                            UpdateBanner.ApplyResult(result);
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on dismiss or dispose — this is how the loop is meant to end.
            }
            catch (Exception ex)
            {
                Log.Information(
                    ex,
                    "[UpdateCheck] Unhandled exception in the periodic re-check; loop ends, banner unchanged.");
            }
        }, ct);
    }

    /// <summary>Stop the periodic re-check. Idempotent.</summary>
    private void StopUpdateRecheckLoop()
    {
        _updateRecheckCts?.Cancel();
        _updateRecheckCts?.Dispose();
        _updateRecheckCts = null;
    }

    /// <summary>The user closed the banner: latch it off and stop re-checking this session.</summary>
    /// <remarks>
    /// The persisted dismiss already suppresses this tag on later launches; the latch is what
    /// stops the loop re-raising it in the session where it was closed.
    /// </remarks>
    private void OnUpdateBannerDismissed(object? sender, EventArgs e)
    {
        _updateBannerDismissed = true;
        StopUpdateRecheckLoop();
    }

    /// <summary>
    /// Stop the background update re-check.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Called from the window's <c>Closing</c> handler</b>, which is the only place that
    /// knows the window is going. Idempotent, because <c>Closing</c> can fire more than once on
    /// some shutdown paths.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UpdateBanner.Dismissed -= OnUpdateBannerDismissed;
        StopUpdateRecheckLoop();

        // ⚠ Unsubscribe BEFORE disposing: Dispose cancels the backup CTS, which can settle
        // observable properties on the way down and re-enter OnBackupStateChanged — writing the
        // state file from a window that is already going.
        if (_backupVm is not null)
        {
            _backupVm.PersistentStateChanged -= OnBackupStateChanged;
            _backupVm.Dispose();
            _backupVm = null;
        }
    }

    /// <summary>
    /// Build the Backup / Restore nav node, constructing its view-model on first use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ <b>The products come from <see cref="Sections"/>, not from
    /// <c>OpenCodeBackupPage.DefaultProducts</c>.</b> A product this window hosts is a product the
    /// user can back up, and taking both lists from one source is what stops them drifting when a
    /// third section arrives. The fallback constant exists for callers with no window.
    /// </para>
    /// <para>
    /// ⚠ <b>No save-before-backup bridge, unlike the sibling app.</b> ClaudeForge supplies
    /// <c>IsAnyWorkspaceDirty</c> / <c>SaveAllWorkspaces</c> / <c>OnRestoreCompleted</c> so a
    /// backup taken mid-edit can offer to flush first. This window has no save-all pipeline to
    /// call, so the page's unsaved-edits prompts stay dormant rather than being wired to something
    /// that cannot honour them. ⛔ The consequence is real and belongs in the follow-up: a restore
    /// here does NOT reload the open documents, so the editor keeps showing the pre-restore file
    /// until the app is restarted.
    /// </para>
    /// </remarks>
    private NavigationNodeViewModel BuildBackupNode()
    {
        if (_backupVm is null)
        {
            WindowState state = WindowStateService.Load();

            _backupVm = new BackupRestoreViewModel(
                _dialogService,
                OpenCodeBackupPage.Options([.. Sections.Select(s => s.Product)]))
            {
                CredentialsPreference = state.IncludeCredentialsInBackup,
                LastBackupUtc = state.LastBackupUtc,
                InitialBackupDirectory = state.BackupDirectory ?? string.Empty,
                InitialRestoreDirectory = state.RestoreDirectory ?? string.Empty,
            };
            _backupVm.PersistentStateChanged += OnBackupStateChanged;
        }

        _backupVm.Refresh();

        return new NavigationNodeViewModel(Strings.HeadingBackupRestore)
        {
            NodeId = BackupNodeId,
            IsTopLevel = true,
            Editor = _backupVm,
        };
    }

    /// <summary>
    /// Persist the Backup page's folders, credentials answer and last-backup time.
    /// </summary>
    /// <remarks>
    /// ⚠ <b><c>Refresh()</c> raises this up to three times in a row</b> — once each as
    /// BackupDirectory, RestoreDirectory and the credentials preference settle. Every one is a
    /// read-modify-write of the state file. That is tolerable at this volume and is the reason
    /// <see cref="WindowStateService.SaveBackupState"/> takes all four at once rather than offering
    /// a setter per field; if it ever stops being tolerable, debounce here the way the sibling app
    /// does rather than splitting the save.
    /// </remarks>
    private void OnBackupStateChanged(object? sender, EventArgs e)
    {
        if (sender is not BackupRestoreViewModel vm)
        {
            return;
        }

        WindowStateService.SaveBackupState(
            vm.BackupDirectory, vm.RestoreDirectory, vm.CredentialsPreference, vm.LastBackupUtc);
    }

    internal static void ApplyProvenanceBadge(
        NavigationNodeViewModel header, SchemaRegistry registry, HostedSection section)
    {
        SchemaProvenance? provenance = registry.ProvenanceFor(section.Product.SchemaFileName);
        if (provenance is null)
        {
            return;
        }

        if (provenance.Source == SchemaSource.Bundled)
        {
            header.Badge = Strings.SchemaBadgeBundled;
            header.BadgeTooltip = string.Format(
                CultureInfo.CurrentCulture, Strings.SchemaBadgeTooltipBundledFmt, provenance.ShortSha);
            return;
        }

        // Local time, not UTC: the badge is read by a human looking at a clock, and the tooltip
        // carries the digest for anything that needs to be compared across machines.
        string when = provenance.FetchedUtc is { } utc
            ? utc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)
            : string.Empty;

        header.Badge = string.Format(
            CultureInfo.CurrentCulture, Strings.SchemaBadgeFetchedFmt, when);
        header.BadgeTooltip = string.Format(
            CultureInfo.CurrentCulture, Strings.SchemaBadgeTooltipFetchedFmt, when, provenance.ShortSha);
    }

    /// <summary>
    /// Re-fetch every hosted schema, re-label the nav badges, and return a localized one-line
    /// summary. Backs the About dialog's <em>Check for schema updates</em> button.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>This is what makes <see cref="NavigationNodeViewModel.Badge"/> observable rather
    /// than <c>init</c>.</b> The nodes were built at load time; re-badging them in place is
    /// the only way a mid-session check shows without discarding the tree, and discarding it
    /// would throw away expansion state, selection, and any editor mid-edit.
    /// </para>
    /// <para>
    /// ⛔ <b>It does not reload.</b> An <c>Updated</c> result means the badge and the pages
    /// now disagree, which is why the summary says to reload rather than implying the new
    /// shape is already on screen.
    /// </para>
    /// <para>
    /// ⓘ Unlike ClaudeForge, this app's SDK clients hold their OWN registries, so a check here
    /// moves the pages' copy and leaves save-validation on whatever each client fetched at
    /// startup. That divergence predates this action — see the badge's remarks — and sharing
    /// one registry is the fix for both.
    /// </para>
    /// </remarks>
    public async Task<string> CheckForSchemaUpdatesAsync(CancellationToken ct = default)
    {
        if (_registry is null)
        {
            return Strings.SchemaCheckUnavailable;
        }

        IReadOnlyList<SchemaRefreshResult> results = await SchemaRefresher
            .RefreshAsync(_registry, Sections.Select(s => s.Product), ct)
            .ConfigureAwait(true);

        foreach (HostedSection section in Sections)
        {
            NavigationNodeViewModel? header = Navigation
                .FirstOrDefault(n => string.Equals(n.Title, section.HeaderText(), StringComparison.Ordinal));

            if (header is not null)
            {
                ApplyProvenanceBadge(header, _registry, section);
            }
        }

        return SummariseSchemaCheck(results);
    }

    /// <summary>
    /// Turn per-product results into the one line the dialog shows.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Severity order, not concatenation:</b> Failed, then Updated, then Unavailable,
    /// then up to date. The row is one line, and the per-product detail is in each section's
    /// own badge — which the caller has just refreshed.
    /// </remarks>
    internal static string SummariseSchemaCheck(IReadOnlyList<SchemaRefreshResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        static string Names(IEnumerable<SchemaRefreshResult> subset) =>
            string.Join(", ", subset.Select(r => r.Product.DisplayName));

        List<SchemaRefreshResult> failed = [.. results.Where(r => r.Status == SchemaRefreshStatus.Failed)];
        if (failed.Count > 0)
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.SchemaCheckFailedFmt, Names(failed));
        }

        List<SchemaRefreshResult> updated = [.. results.Where(r => r.Status == SchemaRefreshStatus.Updated)];
        if (updated.Count > 0)
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.SchemaCheckUpdatedFmt, Names(updated));
        }

        if (results.Any(r => r.Status == SchemaRefreshStatus.Unavailable))
        {
            return Strings.SchemaCheckUnavailable;
        }

        return Strings.SchemaCheckUpToDate;
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

        // ⚠ One factory PER SECTION, not one shared across both. The factory carries this
        // document's danger table, and the two documents' tables barely overlap — a shared
        // instance would label tui.json's rows with opencode.json's policy.
        OpenCodeEditorFactory editorFactory = new(section.Danger);

        List<NavigationNodeViewModel> pages = [];
        foreach (SchemaPage page in section.Layout.Arrange(nodes))
        {
            SettingsGroupEditorViewModel editor = new(
                page.Title,
                page.Nodes,
                workspace,
                scope,
                editorFactory,
                OpenCodeSettingsGroupText.Create(),
                groupDescription: page.Description,
                sdkClient: section.Client);

            pages.Add(new NavigationNodeViewModel(page.Title) { Editor = editor });
        }

        return pages;
    }
}
