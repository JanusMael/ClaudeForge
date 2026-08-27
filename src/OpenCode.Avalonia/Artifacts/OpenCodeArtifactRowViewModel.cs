using Bennewitz.Ninja.AgentForge.Artifacts;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Sdk.Artifacts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Artifacts;

/// <summary>
/// One declaration inside an artifact's chain.
/// </summary>
public sealed class OpenCodeArtifactDeclarationViewModel(OpenCodeArtifactDeclaration declaration)
{
    /// <summary>Which precedence layer it came from.</summary>
    public string ScopeName => declaration.Entry.Scope.DisplayName;

    /// <summary>Where it is — a path, or the name itself for something built in.</summary>
    public string Location => declaration.Entry.Location;

    /// <summary>How it was declared, for display.</summary>
    public string FormLabel => OpenCodeArtifactRowViewModel.FormLabelFor(declaration.Entry.Form);

    /// <summary>Whether it lives in another tool's directory.</summary>
    public bool IsCrossTool => declaration.IsCrossTool;

    /// <summary>Why that matters, spelled out rather than left to a badge.</summary>
    public string CrossToolTooltip => Strings.ArtifactsCrossToolTip;

    /// <summary>True for a declaration with no file behind it, so nothing offers to open one.</summary>
    public bool HasFile => declaration.Entry.Form == ArtifactForm.File;
}

/// <summary>
/// One artifact: its winning declaration, everything else that declares it, and what is wrong.
/// </summary>
/// <remarks>
/// ⛔⛔ <b>The chain summary is the whole reason this type reads
/// <see cref="OpenCodeArtifactItem.Semantics"/> instead of just counting.</b> For skills the losing
/// copies genuinely never load, so "overridden" is accurate. For agents and commands they are
/// <b>live</b>, contributing fields the winner does not set — measured — so the same sentence there
/// would tell the user to delete a file that is in force. One count, two opposite meanings.
/// </remarks>
public sealed partial class OpenCodeArtifactRowViewModel(OpenCodeArtifactItem item) : ObservableObject
{
    /// <summary>Whether the declaration chain is expanded.</summary>
    /// <remarks>
    /// Collapsed by default: the overwhelmingly common case is a single declaration, and expanding
    /// every row by default would bury the handful that actually have a chain worth reading.
    /// </remarks>
    [ObservableProperty] private bool _isExpanded;

    /// <summary>The name OpenCode knows it by.</summary>
    public string Name => item.Name;

    /// <summary>Its declared description, when it has one.</summary>
    public string? Description => item.Description;

    /// <summary>True when there is a description to render, so the row can collapse the space.</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(item.Description);

    /// <summary>The scope of the highest-precedence declaration.</summary>
    public string EffectiveScopeName => item.Effective.Scope.DisplayName;

    /// <summary>Where the highest-precedence declaration lives.</summary>
    public string EffectiveLocation => item.Effective.Location;

    /// <summary>How the highest-precedence declaration was written.</summary>
    public string EffectiveFormLabel => FormLabelFor(item.Effective.Form);

    /// <summary>Every declaration, highest precedence first.</summary>
    public IReadOnlyList<OpenCodeArtifactDeclarationViewModel> Declarations { get; } =
        [.. item.Declarations.Select(d => new OpenCodeArtifactDeclarationViewModel(d))];

    /// <summary>True when more than one source declared this name.</summary>
    public bool HasChain => item.HasMultipleDeclarations;

    /// <summary>
    /// What the chain means, in this kind's terms.
    /// </summary>
    /// <remarks>
    /// Both wordings quote a count of 2 or more, so neither needs a singular form — a chain of one
    /// is not a chain and <see cref="HasChain"/> keeps this off the row entirely.
    /// </remarks>
    public string ChainSummary => item.Semantics switch
    {
        OpenCodeChainSemantics.DeepMerge =>
            string.Format(Strings.ArtifactsChainMergedFmt, item.Declarations.Count),
        _ => string.Format(Strings.ArtifactsChainOverriddenFmt, item.Declarations.Count),
    };

    /// <summary>True when any declaration lives in another tool's directory.</summary>
    public bool IsCrossTool => item.IsCrossTool;

    /// <summary>The cross-tool badge's label.</summary>
    public string CrossToolLabel => Strings.ArtifactsCrossTool;

    /// <summary>Why the cross-tool badge matters.</summary>
    public string CrossToolTooltip => Strings.ArtifactsCrossToolTip;

    /// <summary>
    /// True only when OpenCode will not load this at all — never for a mere warning.
    /// </summary>
    public bool IsInert => item.IsInert;

    /// <summary>The "not loaded" badge's label.</summary>
    public string InertLabel => Strings.ArtifactsInert;

    /// <summary>Everything wrong with it, in plain sentences.</summary>
    public IReadOnlyList<string> IssueMessages { get; } = [.. item.Issues.Select(MessageFor)];

    /// <summary>True when there is anything to say about problems.</summary>
    public bool HasIssues => item.Issues.Count > 0;

    /// <summary>
    /// Whether this row survives <paramref name="filter"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Matches the LOCATION as well as the name and description.</b> The question a user
    /// brings to this page is often "what is reading from that directory?", and a filter that only
    /// searched names would answer it with an empty list.
    /// </remarks>
    public bool Matches(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        string needle = filter.Trim();
        if (Contains(Name, needle) || Contains(Description, needle))
        {
            return true;
        }

        foreach (OpenCodeArtifactDeclarationViewModel declaration in Declarations)
        {
            if (Contains(declaration.Location, needle) || Contains(declaration.ScopeName, needle))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A display label for a declaration's form.</summary>
    internal static string FormLabelFor(ArtifactForm form) => form switch
    {
        ArtifactForm.BuiltIn => Strings.ArtifactsFormBuiltIn,
        ArtifactForm.Inline => Strings.ArtifactsFormInline,
        ArtifactForm.Remote => Strings.ArtifactsFormRemote,
        _ => Strings.ArtifactsFormFile,
    };

    /// <summary>
    /// The sentence for one issue.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The wording carries the evidence level, deliberately.</b> Two of these were measured
    /// against the installed binary and say so flatly; the third is taken from OpenCode's own
    /// specification and could not be confirmed here, so it says that rather than borrowing the
    /// others' certainty. A page that states an unverified claim in the same voice as a verified
    /// one teaches users to discount both.
    /// </remarks>
    private static string MessageFor(OpenCodeArtifactIssue issue) => issue switch
    {
        OpenCodeArtifactIssue.SkillHasNoDeclaredName => Strings.ArtifactsIssueNoName,
        OpenCodeArtifactIssue.SkillNameDiffersFromFolder => Strings.ArtifactsIssueNameDiffers,
        _ => Strings.ArtifactsIssueNoDescription,
    };

    private static bool Contains(string? haystack, string needle)
        => haystack is not null
           && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
