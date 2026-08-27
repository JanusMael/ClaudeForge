using System.Collections.ObjectModel;
using Bennewitz.Ninja.AgentForge.Artifacts;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCode.Sdk.Artifacts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Artifacts;

/// <summary>
/// One tab: every artifact of a single kind.
/// </summary>
public sealed partial class OpenCodeArtifactTabViewModel : ObservableObject
{
    private readonly List<OpenCodeArtifactRowViewModel> _all;

    internal OpenCodeArtifactTabViewModel(OpenCodeArtifactGroup group)
    {
        Kind = group.Kind;
        Title = TitleFor(group.Kind);
        _all = [.. group.Items.Select(i => new OpenCodeArtifactRowViewModel(i))];
        Rows = [.. _all];
    }

    /// <summary>Which kind this tab holds.</summary>
    public ArtifactKind Kind { get; }

    /// <summary>The tab's label.</summary>
    public string Title { get; }

    /// <summary>The rows currently shown, after filtering.</summary>
    public ObservableCollection<OpenCodeArtifactRowViewModel> Rows { get; }

    /// <summary>How many artifacts of this kind exist, regardless of the filter.</summary>
    public int TotalCount => _all.Count;

    /// <summary>
    /// A count that says what is hidden, not just what is shown.
    /// </summary>
    /// <remarks>
    /// ⚠ A bare count of visible rows over a filtered list reads as "this is everything", which is
    /// exactly wrong while a filter is active. When nothing is filtered out this collapses to the
    /// plain total, so the common case stays quiet.
    /// </remarks>
    public string CountLabel => Rows.Count == _all.Count
        ? string.Format(Strings.ArtifactsCountFmt, _all.Count)
        : string.Format(Strings.ArtifactsCountFilteredFmt, Rows.Count, _all.Count);

    /// <summary>True when there is nothing to show at all.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// What to say when the list is empty — and it matters which emptiness this is.
    /// </summary>
    /// <remarks>
    /// ⭐ "You have none of these" and "your filter excluded all of them" are different facts, and
    /// showing the first when the second is true sends a user looking for a file they do have.
    /// </remarks>
    public string EmptyMessage => _all.Count == 0
        ? Strings.ArtifactsEmptyNone
        : Strings.ArtifactsEmptyFiltered;

    /// <summary>Re-apply <paramref name="filter"/> to this tab's rows.</summary>
    internal void ApplyFilter(string? filter)
    {
        Rows.Clear();
        foreach (OpenCodeArtifactRowViewModel row in _all)
        {
            if (row.Matches(filter))
            {
                Rows.Add(row);
            }
        }

        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    /// <summary>
    /// The tab label for a kind.
    /// </summary>
    /// <remarks>
    /// <see cref="ArtifactKind.Memory"/> is shown as "Rules" because that is what OpenCode calls
    /// these files. The shared enum name reflects Claude's vocabulary, and surfacing it here would
    /// label an OpenCode page with another product's word for the same thing.
    /// </remarks>
    private static string TitleFor(ArtifactKind kind) => kind switch
    {
        ArtifactKind.Agent => Strings.ArtifactsTabAgents,
        ArtifactKind.Command => Strings.ArtifactsTabCommands,
        ArtifactKind.Skill => Strings.ArtifactsTabSkills,
        ArtifactKind.Plugin => Strings.ArtifactsTabPlugins,
        _ => Strings.ArtifactsTabRules,
    };
}

/// <summary>
/// The page: every agent, command, skill, rule and plugin OpenCode reads, and where from.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>A projection, not a second brain.</b> Everything true here was decided in
/// <see cref="OpenCodeArtifactInventory"/> — grouping, ordering, precedence, cross-tool badges and
/// diagnoses — and this type only turns it into rows and applies a filter. That split is what
/// makes every claim the page renders testable without a UI, and it is why the inventory carries
/// the per-kind chain semantics rather than the view.
/// </para>
/// <para>
/// ⚠ <b>Read-only, by decision.</b> This lists and explains; it does not create, edit or delete.
/// Editing these files means a front-matter editor and a markdown body editor, which the plan
/// assigns to a later slice. Shipping the explanation first is deliberate: the thing users cannot
/// currently do at all is find out which of four copies of an agent is winning.
/// </para>
/// </remarks>
public sealed partial class OpenCodeArtifactsPageViewModel : ObservableObject
{
    private readonly OpenCodeEnvironment _environment;

    /// <summary>Build the page for one environment and working directory.</summary>
    /// <param name="environment">Decides the global config directory.</param>
    /// <param name="workingDirectory">
    /// The project to resolve project-scope artifacts against, or <see langword="null"/> for none.
    /// </param>
    public OpenCodeArtifactsPageViewModel(
        OpenCodeEnvironment environment, string? workingDirectory)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _workingDirectory = workingDirectory ?? string.Empty;
        Reload();
    }

    /// <summary>The five tabs, in the order the plan specifies.</summary>
    public ObservableCollection<OpenCodeArtifactTabViewModel> Tabs { get; } = [];

    /// <summary>
    /// The project directory the project-scope sources are resolved against.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>Asked for, never guessed.</b> Most of what this page exists to explain — the ancestor
    /// walk, the inverted precedence, which of four copies of an agent wins — is project-scope, and
    /// with no directory the page can only show the global and cross-tool sources. The obvious
    /// shortcut is <see cref="Environment.CurrentDirectory"/>, and it is wrong: for a GUI launched
    /// from a shortcut that is some arbitrary directory the user has never heard of, and the page
    /// would then confidently describe a project they are not in.
    /// </remarks>
    [ObservableProperty] private string _workingDirectory;

    /// <summary>Label for the working-directory box.</summary>
    public string WorkingDirectoryLabel => Strings.ArtifactsWorkingDirectory;

    /// <summary>Placeholder for the working-directory box.</summary>
    public string WorkingDirectoryPlaceholder => Strings.ArtifactsWorkingDirectoryPlaceholder;

    /// <summary>True while no project directory is set, so the page can say what is missing.</summary>
    public bool HasNoProject => string.IsNullOrWhiteSpace(WorkingDirectory);

    /// <summary>What to tell a user who has not chosen a project yet.</summary>
    public string NoProjectMessage => Strings.ArtifactsNoProject;

    /// <summary>
    /// Rebuild every tab from the filesystem, then re-apply the active filter.
    /// </summary>
    /// <remarks>
    /// ⚠ Re-applying the filter is not optional. Rebuilding produces unfiltered tabs, so skipping
    /// it would leave the filter box populated beside a list that plainly ignores it.
    /// </remarks>
    public void Reload()
    {
        ArtifactKind? previous = SelectedTab?.Kind;

        Tabs.Clear();
        foreach (OpenCodeArtifactGroup group in OpenCodeArtifactInventory.Build(
                     _environment, string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory))
        {
            Tabs.Add(new OpenCodeArtifactTabViewModel(group));
        }

        if (!string.IsNullOrEmpty(FilterText))
        {
            foreach (OpenCodeArtifactTabViewModel tab in Tabs)
            {
                tab.ApplyFilter(FilterText);
            }
        }

        // Keep the user on the tab they were reading; a reload that silently jumps back to Agents
        // makes the directory box feel like it reset the page rather than refreshed it.
        SelectedTab = Tabs.FirstOrDefault(t => t.Kind == previous) ?? Tabs.FirstOrDefault();
    }

    partial void OnWorkingDirectoryChanged(string value)
    {
        OnPropertyChanged(nameof(HasNoProject));
        Reload();
    }

    /// <summary>The page heading.</summary>
    public string Title => Strings.ArtifactsPageTitle;

    /// <summary>One sentence saying what the page is.</summary>
    public string Subtitle => Strings.ArtifactsPageSubtitle;

    /// <summary>Placeholder shown inside the filter box.</summary>
    public string FilterPlaceholder => Strings.ArtifactsFilterPlaceholder;

    /// <summary>
    /// The filter box's accessible name.
    /// </summary>
    /// <remarks>
    /// ⚠ Deliberately a separate string from the placeholder. They serve different readers, and a
    /// UIA lookup by the on-screen placeholder text finds nothing — a trap this project has
    /// already paid for once when driving the keybinds editor.
    /// </remarks>
    public string FilterAutomationName => Strings.ArtifactsFilterAutoName;

    /// <summary>The tab currently shown.</summary>
    [ObservableProperty] private OpenCodeArtifactTabViewModel? _selectedTab;

    /// <summary>The active filter text.</summary>
    [ObservableProperty] private string _filterText = string.Empty;

    /// <summary>
    /// Filtering applies to EVERY tab, not only the visible one.
    /// </summary>
    /// <remarks>
    /// ⚠ Otherwise a tab's count would still claim the unfiltered total until the user happened to
    /// click it, so the badge on an unvisited tab would contradict the filter that is plainly
    /// active on screen.
    /// </remarks>
    partial void OnFilterTextChanged(string value)
    {
        foreach (OpenCodeArtifactTabViewModel tab in Tabs)
        {
            tab.ApplyFilter(value);
        }
    }
}
