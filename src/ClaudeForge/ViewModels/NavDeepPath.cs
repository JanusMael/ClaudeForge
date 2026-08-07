using System.Text;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// Parsing, formatting, and nav-tree resolution for <em>deep paths</em> — the
/// culture-invariant addresses that identify a position inside the app below the
/// navigation node level.
///
/// <para>
/// Grammar (at most <see cref="MaxSegments"/> segments):
/// </para>
/// <code>
/// &lt;path&gt; := &lt;top-level-id&gt; [ "/" &lt;child-id&gt; ] [ "/" &lt;tab-id&gt; [ "/" &lt;item-key&gt; ] ]
///
/// agents-skills
/// agents-skills/skills
/// agents-skills/skills/pdf
/// agents-skills/skills/pdf@user
/// claude-code/permissions
/// claude-code/permissions/properties
/// </code>
///
/// <para>
/// Segments are matched against <see cref="NavigationNodeViewModel.NodeId"/>,
/// never <c>Title</c> — see that property's docs for why. Deliberately a pure
/// static type with no Avalonia or SDK dependency so it is unit-testable without
/// a dispatcher, and so the same grammar backs all three consumers: the
/// <c>--deep-link</c> command-line argument, the persisted
/// <c>WindowState.LastDeepPath</c>, and (if it is ever built) an OS-level
/// <c>claude://</c> protocol handler.
/// </para>
/// </summary>
public static class NavDeepPath
{
    /// <summary>Segment separator. Segments may not contain this character.</summary>
    public const char Separator = '/';

    /// <summary>
    /// Separates an artifact name from its disambiguating source in an item key
    /// (<c>pdf@user</c>). Split on the LAST occurrence so a name that itself
    /// contains an <c>@</c> still resolves.
    /// </summary>
    public const char SourceSeparator = '@';

    /// <summary>
    /// Longest legal path: parent node, child node, tab, item
    /// (<c>claude-code/permissions/properties/&lt;item&gt;</c>).
    /// </summary>
    public const int MaxSegments = 4;

    /// <summary>
    /// Reduce a display title to a stable, typeable id — lower-cased, with every
    /// run of non-alphanumeric characters collapsed to a single <c>-</c> and the
    /// result trimmed of leading / trailing separators.
    /// <para>
    /// Restricted to ASCII alphanumerics on purpose. <see cref="char.IsLetterOrDigit(char)"/>
    /// would preserve non-ASCII letters, which would produce ids nobody can type
    /// on a command line the moment a title carries an accent or a CJK
    /// character. Anything outside <c>a-z0-9</c> becomes a separator instead.
    /// </para>
    /// <para>
    /// Examples: <c>"Agents &amp; Skills"</c> → <c>agents-skills</c>;
    /// <c>"MCP Servers"</c> → <c>mcp-servers</c>;
    /// <c>"Backup / Restore"</c> → <c>backup-restore</c>.
    /// </para>
    /// </summary>
    public static string Slug(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        StringBuilder sb = new(title!.Length);
        bool pendingSeparator = false;
        foreach (char raw in title)
        {
            char c = char.ToLowerInvariant(raw);
            bool keep = c is >= 'a' and <= 'z' or >= '0' and <= '9';
            if (keep)
            {
                // Emit a collapsed separator only once we know a real character
                // follows, so the result never ends with '-'.
                if (pendingSeparator && sb.Length > 0)
                {
                    sb.Append('-');
                }

                pendingSeparator = false;
                sb.Append(c);
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Validate the <em>shape</em> of a raw path and split it into segments.
    /// Shape-only by design: whether the path actually points at a live node is
    /// decided later by <see cref="Resolve"/> against the built tree, because a
    /// path can be perfectly well-formed and still name a page that this install
    /// does not have (Claude Desktop absent, Welcome node hidden).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when <paramref name="raw"/> is well-formed;
    /// otherwise <see langword="false"/> with a human-readable reason in
    /// <paramref name="error"/> suitable for a log line.
    /// </returns>
    public static bool TryParse(string? raw, out IReadOnlyList<string> segments, out string? error)
    {
        segments = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "path is empty";
            return false;
        }

        string trimmed = raw!.Trim();
        if (trimmed[0] == Separator || trimmed[^1] == Separator)
        {
            error = FormattableString.Invariant($"path must not start or end with '{Separator}'");
            return false;
        }

        string[] parts = trimmed.Split(Separator);
        if (parts.Length > MaxSegments)
        {
            error = FormattableString.Invariant(
                $"path has {parts.Length} segments; the maximum is {MaxSegments}");
            return false;
        }

        foreach (string part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                error = "path contains an empty segment";
                return false;
            }

            // Control characters would corrupt the log line and can't come from
            // any legitimate node id or artifact name.
            foreach (char c in part)
            {
                if (char.IsControl(c))
                {
                    error = "path contains a control character";
                    return false;
                }
            }
        }

        segments = parts;
        error = null;
        return true;
    }

    /// <summary>Join segments back into a path. Inverse of <see cref="TryParse"/>.</summary>
    public static string Format(IEnumerable<string> segments)
    {
        return string.Join(Separator, segments);
    }

    /// <summary>
    /// Walk <paramref name="tree"/> to find the node <paramref name="segments"/>
    /// addresses, returning it plus whatever segments remain below it (the tab
    /// id, then the item key).
    ///
    /// <para>
    /// Resolution is strictly left-to-right, which is what removes the
    /// node-vs-tab ambiguity in a path like <c>claude-code/permissions</c>:
    /// segment 1 must name a top-level node; if that node has children and
    /// segment 2 names one of them, segment 2 is consumed as the node. Only then
    /// do the remaining segments mean tab and item. Without the ordering rule,
    /// <c>permissions</c> could equally be read as a tab of the
    /// <c>claude-code</c> header.
    /// </para>
    /// <para>
    /// Node lookup ignores case and skips dividers (which carry no
    /// <see cref="NavigationNodeViewModel.NodeId"/>). Resolution intentionally
    /// succeeds even when the leftover tab / item segments turn out to be
    /// meaningless for the node — applying them is best-effort, and a stale
    /// shortcut must degrade to "landed on the right page" rather than fail.
    /// </para>
    /// </summary>
    public static NavDeepPathResolution Resolve(
        IReadOnlyList<string> segments, IEnumerable<NavigationNodeViewModel> tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        if (segments is null || segments.Count == 0)
        {
            return NavDeepPathResolution.Unresolved;
        }

        NavigationNodeViewModel? node = tree.FirstOrDefault(
            n => !n.IsDivider && IdMatches(n.NodeId, segments[0]));
        if (node is null)
        {
            return NavDeepPathResolution.Unresolved;
        }

        int consumed = 1;
        if (segments.Count > 1 && node.Children.Count > 0)
        {
            NavigationNodeViewModel? child = node.Children.FirstOrDefault(
                c => !c.IsDivider && IdMatches(c.NodeId, segments[1]));
            if (child is not null)
            {
                node = child;
                consumed = 2;
            }
        }

        string[] remaining = segments.Skip(consumed).ToArray();
        return new NavDeepPathResolution(node, remaining);
    }

    /// <summary>
    /// Split an item key into its name and optional disambiguating source —
    /// <c>"pdf@user"</c> → <c>("pdf", "user")</c>, <c>"pdf"</c> →
    /// <c>("pdf", null)</c>.
    /// </summary>
    public static (string Name, string? Source) SplitItemKey(string itemKey)
    {
        ArgumentNullException.ThrowIfNull(itemKey);

        int at = itemKey.LastIndexOf(SourceSeparator);

        // A leading '@' is part of the name, not an empty-source marker.
        if (at <= 0 || at == itemKey.Length - 1)
        {
            return (itemKey, null);
        }

        return (itemKey[..at], itemKey[(at + 1)..]);
    }

    /// <summary>Build the fully-qualified item key for an artifact.</summary>
    public static string FormatItemKey(string name, string? source)
    {
        return string.IsNullOrWhiteSpace(source)
            ? name
            : FormattableString.Invariant($"{name}{SourceSeparator}{source}");
    }

    private static bool IdMatches(string? nodeId, string segment)
    {
        return !string.IsNullOrEmpty(nodeId)
               && string.Equals(nodeId, segment, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Outcome of <see cref="NavDeepPath.Resolve"/>: the addressed node plus the
/// segments below it that the node's editor should interpret (tab id, then item
/// key).
/// </summary>
/// <param name="Node">
/// The addressed node, or <see langword="null"/> when the path names nothing in
/// this tree (a page this install lacks, or a stale id).
/// </param>
/// <param name="RemainingSegments">
/// Segments left after the node was consumed — at most a tab id and an item key.
/// </param>
public sealed record NavDeepPathResolution(
    NavigationNodeViewModel? Node,
    IReadOnlyList<string> RemainingSegments)
{
    /// <summary>The shared "path names nothing here" result.</summary>
    public static NavDeepPathResolution Unresolved { get; } = new(null, Array.Empty<string>());

    /// <summary><see langword="true"/> when a node was found.</summary>
    public bool Resolved => Node is not null;

    /// <summary>The tab / segment id below the node, or <see langword="null"/>.</summary>
    public string? TabId => RemainingSegments.Count > 0 ? RemainingSegments[0] : null;

    /// <summary>The item key below the tab, or <see langword="null"/>.</summary>
    public string? ItemKey => RemainingSegments.Count > 1 ? RemainingSegments[1] : null;
}
