using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>One row of the Tier 2 footprint table — wraps
/// <see cref="FootprintCategoryStats"/> with the localised label and a
/// humanised size string.</summary>
public sealed class FootprintRowViewModel
{
    private readonly FootprintCategoryStats _stats;

    public FootprintRowViewModel(FootprintCategoryStats stats)
    {
        _stats = stats;
    }

    public FootprintCategory Category => _stats.Category;
    public string AbsolutePath => _stats.AbsolutePath;
    public int FileCount => _stats.FileCount;
    public long TotalBytes => _stats.TotalBytes;
    public bool IsInStandardBackup => _stats.IsInStandardBackup;

    /// <summary>Localised human label per category.</summary>
    public string HumanLabel => Category switch
    {
        FootprintCategory.SessionTranscripts => Strings.LabelFootprintCategoryTranscripts,
        FootprintCategory.SessionMetadata => Strings.LabelFootprintCategorySessions,
        FootprintCategory.PromptHistory => Strings.LabelFootprintCategoryHistory,
        FootprintCategory.BashCommandLog => Strings.LabelFootprintCategoryBashLog,
        FootprintCategory.CostTrackerLog => Strings.LabelFootprintCategoryCostLog,
        FootprintCategory.Todos => Strings.LabelFootprintCategoryTodos,
        FootprintCategory.FileEditHistory => Strings.LabelFootprintCategoryFileHistory,
        var _ => Category.ToString(),
    };

    /// <summary>Humanised byte count (e.g. "2.4 MB"). Stable rounding to 1 decimal.</summary>
    public string HumanSize => FormatBytes(TotalBytes);

    /// <summary>Localised "Yes" / "No" for the In-Standard-Backup column.</summary>
    public string InStandardBackupLabel =>
        IsInStandardBackup ? Strings.LabelYes : Strings.LabelNo;

    /// <summary>
    /// One-sentence description shown as a hover tooltip on the row's
    /// human label.  Surfaces what the category contains and how Claude
    /// uses it — useful context before deciding to delete a category.
    /// </summary>
    public string Tooltip => Category switch
    {
        FootprintCategory.SessionTranscripts => Strings.TipFootprintCategoryTranscripts,
        FootprintCategory.SessionMetadata => Strings.TipFootprintCategorySessions,
        FootprintCategory.PromptHistory => Strings.TipFootprintCategoryHistory,
        FootprintCategory.BashCommandLog => Strings.TipFootprintCategoryBashLog,
        FootprintCategory.CostTrackerLog => Strings.TipFootprintCategoryCostLog,
        FootprintCategory.Todos => Strings.TipFootprintCategoryTodos,
        FootprintCategory.FileEditHistory => Strings.TipFootprintCategoryFileHistory,
        var _ => string.Empty,
    };

    private static string FormatBytes(long bytes)
    {
        // Order matters: largest unit first that fits the value cleanly.
        // FormattableString.Invariant forces the decimal separator to '.'
        // regardless of CurrentCulture — the size badge is a technical
        // display whose separator should not flip to ',' on de-DE / fr-FR
        // (would also break the FootprintRowViewModelTests assertions).
        const double KB = 1024d;
        const double MB = KB * 1024;
        const double GB = MB * 1024;
        return bytes switch
        {
            >= (long)GB => FormattableString.Invariant($"{bytes / GB:0.0} GB"),
            >= (long)MB => FormattableString.Invariant($"{bytes / MB:0.0} MB"),
            >= (long)KB => FormattableString.Invariant($"{bytes / KB:0.0} KB"),
            var _ => FormattableString.Invariant($"{bytes} B"),
        };
    }
}