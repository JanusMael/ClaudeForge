using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Bennewitz.Ninja.ClaudeForge.Sdk.Diagnostics;

namespace Bennewitz.Ninja.ClaudeForge.Converters;

/// <summary>
/// Returns a tooltip string for DataGrid value cells that may be truncated.
/// Short, single-line strings (≤50 chars) return null so no tooltip appears.
/// JSON blobs (starting with { or [) are pretty-printed with indentation.
/// All other long or multi-line strings are returned as-is.
/// </summary>
/// <remarks>
/// <para>
/// Output is bounded to <see cref="MaxTooltipLines"/> lines and
/// <see cref="MaxTooltipChars"/> characters.  A tooltip whose rendered
/// height exceeds the display causes Avalonia's positioner to fly the
/// popup between above-cursor and below-cursor placements as it tries
/// to fit on-screen — the user observes that as flicker, and may need
/// to leave and re-hover before the tooltip settles.  Capping the
/// content size up front lets the popup measure once and stay put.
/// </para>
/// <para>
/// When truncation occurs, an explicit footer line points the user to
/// the per-cell context-menu Copy action so the full value is still
/// recoverable without the tooltip.
/// </para>
/// </remarks>
public sealed class LongValueTooltipConverter : IValueConverter
{
    /// <summary>Shared singleton; use as <c>{x:Static conv:LongValueTooltipConverter.Instance}</c>.</summary>
    public static readonly LongValueTooltipConverter Instance = new();

    /// <summary>
    /// Below this length (single-line, no JSON shape), the tooltip is
    /// suppressed entirely so short values don't get a tooltip on hover.
    /// UI-policy threshold; not part of the SDK formatter contract.
    /// </summary>
    private const int ShortThreshold = 50;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? s = value as string;
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }

        // JSON objects and arrays: always pretty-print and show a tooltip so the
        // user can read the full structure regardless of how compact the
        // serialised form is. The 50-char threshold is intentionally NOT applied
        // here — a value like {"allow":["Bash(*)"]} is well under 50 chars but is
        // still a blob that benefits from indented formatting. Pretty-print +
        // cap moved to ClaudeForge.Sdk.Diagnostics.JsonFormatting in the
        // SDK-first migration.
        //
        // for JSON content we return a monospace-styled
        // TextBlock rather than a raw string so the indentation, key
        // alignment, and bracket structure read as code.  Avalonia's
        // ToolTip.Tip accepts any object; when given a Control it hosts
        // it directly inside the tooltip popup.  Non-JSON content stays
        // a plain string so Avalonia's default tooltip styling applies.
        if (JsonFormatting.LooksLikeJson(s))
        {
            string? pretty = JsonFormatting.TryPrettyPrint(s);
            if (pretty is not null)
            {
                return BuildMonospaceTooltipContent(JsonFormatting.Cap(pretty));
            }
            // Parse failed — fall through to the raw-string path (no
            // monospace wrap; the content isn't structured JSON so the
            // monospace font has no real benefit).
        }

        // Non-JSON: skip the tooltip for short, single-line strings.
        if (s.Length <= ShortThreshold && !s.Contains('\n') && !s.Contains('\r'))
        {
            return null;
        }

        return JsonFormatting.Cap(s);
    }

    /// <summary>
    /// Wrap pretty-printed JSON in a monospace-styled <see cref="TextBlock"/>
    /// so the tooltip popup renders indentation and bracket structure
    /// uniformly.  The font family matches the dialog service's Code
    /// segment renderer (<c>Consolas,Menlo,monospace</c>) so save-dialog
    /// tooltips and schema-validation dialog bodies share visual identity.
    /// </summary>
    private static TextBlock BuildMonospaceTooltipContent(string body)
    {
        return new TextBlock
        {
            Text = body,
            FontFamily = new FontFamily("Consolas,Menlo,monospace"),
            TextWrapping = TextWrapping.NoWrap,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}