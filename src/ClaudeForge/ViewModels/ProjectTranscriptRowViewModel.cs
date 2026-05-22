using System.Globalization;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// One row of the per-project transcript breakdown expander on the Memory
/// page Footprint tab. Wraps <see cref="ProjectTranscriptStats"/> with
/// formatting helpers (humanised size + relative timestamp) so the View
/// stays free of converters.
/// </summary>
public sealed class ProjectTranscriptRowViewModel
{
    private readonly ProjectTranscriptStats _stats;

    public ProjectTranscriptRowViewModel(ProjectTranscriptStats stats)
    {
        _stats = stats;
    }

    /// <summary>Raw mangled directory name; needed by <see cref="Bennewitz.Ninja.ClaudeForge.Sdk.IClaudeConfigClient.DeleteProjectTranscriptsAsync"/>.</summary>
    public string MangledName => _stats.MangledName;

    /// <summary>Best-effort decoded display name (e.g. <c>/Users/brian/myproject</c>).</summary>
    public string DisplayName => _stats.DisplayName;

    public string AbsolutePath => _stats.AbsolutePath;
    public int FileCount => _stats.FileCount;
    public long TotalBytes => _stats.TotalBytes;
    public DateTime LastWriteUtc => _stats.LastWriteUtc;

    /// <summary>
    /// Humanised aggregate size (e.g. "2.4 MB"). Identical formatting rule
    /// as <see cref="FootprintRowViewModel.HumanSize"/> so the two surfaces
    /// agree visually.
    /// </summary>
    public string HumanSize => FormatBytes(TotalBytes);

    /// <summary>
    /// Relative human-readable timestamp ("2 days ago", "3 hours ago"). For
    /// rows older than 30 days, falls back to a yyyy-MM-dd date stamp so
    /// the user sees the year. <see cref="DateTime.MinValue"/> renders as
    /// an em-dash to flag empty husks where no transcript was ever written.
    /// </summary>
    public string LastWriteDisplay
    {
        get
        {
            if (LastWriteUtc == DateTime.MinValue)
            {
                return "—";
            }

            TimeSpan delta = DateTime.UtcNow - LastWriteUtc;
            if (delta.TotalSeconds < 60)
            {
                return "just now";
            }

            if (delta.TotalMinutes < 60)
            {
                return $"{(int)delta.TotalMinutes} min ago";
            }

            if (delta.TotalHours < 24)
            {
                return $"{(int)delta.TotalHours} hr ago";
            }

            if (delta.TotalDays < 7)
            {
                return $"{(int)delta.TotalDays} days ago";
            }

            if (delta.TotalDays < 30)
            {
                return $"{(int)(delta.TotalDays / 7)} wk ago";
            }

            return LastWriteUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double KB = 1024d;
        const double MB = KB * 1024;
        const double GB = MB * 1024;
        return bytes switch
        {
            >= (long)GB => $"{bytes / GB:0.0} GB",
            >= (long)MB => $"{bytes / MB:0.0} MB",
            >= (long)KB => $"{bytes / KB:0.0} KB",
            var _ => $"{bytes} B",
        };
    }
}