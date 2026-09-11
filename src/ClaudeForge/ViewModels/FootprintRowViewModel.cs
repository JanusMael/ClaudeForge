using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;

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
    /// <remarks>
    /// ⚠ <b>Keyed by <see cref="FootprintCategory.Id"/>, not by the category value.</b>
    /// <c>FootprintCategory</c> became a struct when the category set moved into product data, and
    /// a struct cannot appear in a constant pattern — but its id is a string, so the switch stays a
    /// switch. This is also the honest shape: the id is the machine key the resx lookup is keyed
    /// by, exactly as AXAML brush lookups are keyed by <c>ConfigScope.Id</c>.
    /// <para>
    /// The fallback matters more than it used to. These seven arms are Claude's catalog; a product
    /// with its own catalog lands on <c>ToString()</c> until it supplies its own label map, which
    /// is a visibly-unlocalised row rather than a crash or a wrong label.
    /// </para>
    /// </remarks>
    public string HumanLabel => Category.Id switch
    {
        "session-transcripts" => Strings.LabelFootprintCategoryTranscripts,
        "session-metadata" => Strings.LabelFootprintCategorySessions,
        "prompt-history" => Strings.LabelFootprintCategoryHistory,
        "bash-command-log" => Strings.LabelFootprintCategoryBashLog,
        "cost-tracker-log" => Strings.LabelFootprintCategoryCostLog,
        "todos" => Strings.LabelFootprintCategoryTodos,
        "file-edit-history" => Strings.LabelFootprintCategoryFileHistory,
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
    public string Tooltip => Category.Id switch
    {
        "session-transcripts" => Strings.TipFootprintCategoryTranscripts,
        "session-metadata" => Strings.TipFootprintCategorySessions,
        "prompt-history" => Strings.TipFootprintCategoryHistory,
        "bash-command-log" => Strings.TipFootprintCategoryBashLog,
        "cost-tracker-log" => Strings.TipFootprintCategoryCostLog,
        "todos" => Strings.TipFootprintCategoryTodos,
        "file-edit-history" => Strings.TipFootprintCategoryFileHistory,
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