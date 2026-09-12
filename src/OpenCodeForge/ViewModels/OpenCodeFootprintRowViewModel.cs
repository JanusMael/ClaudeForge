using Bennewitz.Ninja.AgentForge.Sdk.Memory;
using Bennewitz.Ninja.OpenCodeForge.Localization;

namespace Bennewitz.Ninja.OpenCodeForge.ViewModels;

/// <summary>
/// One row of the disk-footprint table: a measured category, in this app's words.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <c>ClaudeForge.ViewModels.FootprintRowViewModel</c> rather than a copy of it —
/// that one's label map is Claude's seven ids against Claude's resx, and neither half transfers.
/// </para>
/// <para>
/// ⚠ <b>Keyed by <see cref="FootprintCategory.Id"/>, and the fallback is deliberate.</b> A
/// category added to <c>OpenCodeFootprint.Catalog</c> without a label here renders its
/// PascalCase id — visibly unlocalised, which is what sends someone to fix it, rather than a
/// blank cell or a crash. <c>OpenCodeFootprintWiringTests</c> fails the build long before a user
/// sees one.
/// </para>
/// <para>
/// ⛔ <b>No "in standard backup" column, unlike the sibling app, and its absence is information.</b>
/// Every one of OpenCode's six categories is <c>IsInStandardBackup: false</c> — the config root's
/// own <c>.gitignore</c> excludes <c>node_modules</c>, the cache and state roots are not archived
/// at all, and the session database travels only behind an explicit credentials opt-in. A column
/// of six identical "No"s reads as a table someone forgot to fill in; the page says it once, in a
/// banner, where it is actually a warning.
/// </para>
/// </remarks>
internal sealed class OpenCodeFootprintRowViewModel
{
    private readonly FootprintCategoryStats _stats;

    internal OpenCodeFootprintRowViewModel(FootprintCategoryStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        _stats = stats;
    }

    /// <summary>The category this row reports on.</summary>
    public FootprintCategory Category => _stats.Category;

    /// <summary>Where on disk it lives — also what the Reveal button opens.</summary>
    public string AbsolutePath => _stats.AbsolutePath;

    /// <summary>How many files matched.</summary>
    public int FileCount => _stats.FileCount;

    /// <summary>Aggregate bytes, for sorting and for the total.</summary>
    public long TotalBytes => _stats.TotalBytes;

    /// <summary>Localised category name.</summary>
    public string HumanLabel => Category.Id switch
    {
        "node-modules" => Strings.LabelFootprintNodeModules,
        "download-temps" => Strings.LabelFootprintDownloadTemps,
        "model-catalog" => Strings.LabelFootprintModelCatalog,
        "logs" => Strings.LabelFootprintLogs,
        "locks" => Strings.LabelFootprintLocks,
        "session-database" => Strings.LabelFootprintSessionDatabase,
        var _ => Category.ToString(),
    };

    /// <summary>
    /// What the category holds and whether losing it costs anything — the sentence that decides
    /// whether a user reaches for the folder this row points at.
    /// </summary>
    public string Tooltip => Category.Id switch
    {
        "node-modules" => Strings.TipFootprintNodeModules,
        "download-temps" => Strings.TipFootprintDownloadTemps,
        "model-catalog" => Strings.TipFootprintModelCatalog,
        "logs" => Strings.TipFootprintLogs,
        "locks" => Strings.TipFootprintLocks,
        "session-database" => Strings.TipFootprintSessionDatabase,
        var _ => string.Empty,
    };

    /// <summary>
    /// <see langword="true"/> for the one category that cannot be regenerated.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>The page's single most important distinction, and it is invisible in the numbers.</b>
    /// The database is the smallest meaningful row and the only irreplaceable one; the largest row
    /// by an order of magnitude is the most disposable. A user scanning for "what is taking up
    /// space" reads the sizes and gets the priority exactly backwards, so the row that must not be
    /// deleted says so in words rather than relying on its position in the list.
    /// </remarks>
    public bool IsIrreplaceable =>
        string.Equals(Category.Id, "session-database", StringComparison.Ordinal);

    /// <summary>The irreplaceable badge's text, or empty when the row does not carry one.</summary>
    public string IrreplaceableLabel =>
        IsIrreplaceable ? Strings.LabelFootprintIrreplaceable : string.Empty;

    /// <summary>Humanised size — "52.5 MB".</summary>
    public string HumanSize => FormatBytes(TotalBytes);

    /// <summary>
    /// Humanised bytes, with the separator pinned to <c>.</c> regardless of culture.
    /// </summary>
    /// <remarks>
    /// Same shape and the same reason as the sibling app's: a size badge is technical display, and
    /// flipping its decimal separator to <c>,</c> on de-DE reads as a thousands separator on a
    /// number where that changes the meaning by a factor of a thousand.
    /// </remarks>
    internal static string FormatBytes(long bytes)
    {
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
