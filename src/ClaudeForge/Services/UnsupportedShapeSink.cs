namespace Bennewitz.Ninja.ClaudeForge.Services;

/// <summary>
/// Sink that records schema properties surfaced via the raw-JSON fallback
/// because the generic editor factory could not classify their shape. The host
/// aggregates these and writes a single non-fatal load-time entry to the log so
/// the information is never lost — the field itself is already flagged read-only/raw
/// in place and stays fully editable as validated raw JSON. (A startup
/// <c>NonFatalNoticeDialog</c> was previously shown but proved redundant +
/// distracting; the dialog machinery is retained but currently unused.) Part of the
/// schema-coverage guarantee (Phase 2).
/// </summary>
public interface IUnsupportedShapeSink
{
    /// <summary>Record one unsupported-shape property.</summary>
    /// <param name="jsonPath">Dot-path of the property, e.g. <c>permissions.futureBlob</c>.</param>
    /// <param name="displayName">Title or name for display, or <see langword="null"/>.</param>
    void Report(string jsonPath, string? displayName);
}

/// <summary>A single recorded unsupported-shape property.</summary>
public sealed record UnsupportedShape(string JsonPath, string? DisplayName);

/// <summary>
/// Thread-safe <see cref="IUnsupportedShapeSink"/> that accumulates reports
/// deduped by path. Reused across the Claude Code + Desktop section builds so a
/// single notice covers both. <see cref="Snapshot"/> returns the deduped list in
/// path order. Editor rebuilds (scope switch, workspace change) reuse existing
/// editor instances, so a given path is reported at most once; the dedupe here
/// is belt-and-suspenders for any rebuild that does recreate an editor.
/// </summary>
public sealed class UnsupportedShapeCollector : IUnsupportedShapeSink
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string?> _byPath = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public void Report(string jsonPath, string? displayName)
    {
        lock (_gate)
        {
            // First report for a path wins the display name; later identical
            // paths are no-ops.
            _byPath.TryAdd(jsonPath, displayName);
        }
    }

    /// <summary>True when at least one unsupported shape has been reported.</summary>
    public bool HasAny
    {
        get
        {
            lock (_gate)
            {
                return _byPath.Count > 0;
            }
        }
    }

    /// <summary>The deduped reports in path order.</summary>
    public IReadOnlyList<UnsupportedShape> Snapshot()
    {
        lock (_gate)
        {
            return _byPath
                   .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                   .Select(kv => new UnsupportedShape(kv.Key, kv.Value))
                   .ToList();
        }
    }
}

/// <summary>
/// Shared text for the "no structured editor for this shape" affordance — the
/// per-field tooltip and the aggregated load-time notice (the title + header are
/// now written to the log; see
/// <c>MainWindowViewModel.MaybeLogUnsupportedShapeNotice</c>). Deliberately NOT
/// localized: it is a rare, technical, diagnostic message about an unmodellable
/// schema shape, consistent with the (also non-localized) <c>FatalErrorDialog</c> /
/// <c>NonFatalNoticeDialog</c> siblings.
/// </summary>
public static class UnsupportedShapeText
{
    /// <summary>Tooltip on the per-field warning badge.</summary>
    public const string FieldTooltip =
        "No structured editor matches this property's shape — edit it as raw JSON below. "
        + "Your input is validated before it can be saved.";

    /// <summary>Title bar of the aggregated load-time notice dialog.</summary>
    public const string NoticeTitle = "Some settings have no structured editor";

    /// <summary>Header paragraph above the path list in the aggregated notice.</summary>
    public const string NoticeHeader =
        "The settings listed below have a shape this app cannot render with a structured "
        + "editor, so each is shown as a raw-JSON box. They remain fully editable and are "
        + "validated before saving — this is just a heads-up. Copy the list below if you "
        + "want to report it.";
}
