namespace Bennewitz.Ninja.ClaudeForge.Sdk.Dialogs;

/// <summary>
/// Visual + behavioural category that <see cref="Bennewitz.Ninja.ClaudeForge.Sdk"/>-aware
/// hosts use to pick header colour, glyph, and confirm-button styling for
/// confirm and alert dialogs.  Categorisation lets the SDK signal intent
/// (destructive vs. neutral vs. error) without each call site having to
/// micro-manage styling.
/// </summary>
/// <remarks>
/// Lives in the SDK so producer and consumer share a single source of
/// truth: <see cref="IClaudeConfigClient"/> implementations may return
/// <see cref="DialogMessage"/> instances tagged with a category, and the
/// hosting GUI / CLI / test harness reads the same value back.  The SDK
/// itself does not render anything.
/// </remarks>
public enum DialogCategory
{
    /// <summary>
    /// Things the user may want to know but isn't being asked to act on
    /// destructively (e.g. "restore completed", "include credentials?").
    /// Neutral header, default-styled buttons.
    /// </summary>
    Information,

    /// <summary>
    /// Reversible neutral action prompt (Apply profile, Sync from live,
    /// Save with dirty workspace prompt).  Neutral header, accent confirm.
    /// </summary>
    Confirmation,

    /// <summary>
    /// Irreversible action prompt (Delete profile, Discard edits, Clear app
    /// data).  Subtle red header, danger-styled confirm button to slow the
    /// user down before clicking.
    /// </summary>
    Destructive,

    /// <summary>
    /// Operation already failed; surfaces the failure and offers a single
    /// OK dismissal.  Red header with error glyph.  Only meaningful for
    /// alert-style dialogs (no confirm/cancel pair).
    /// </summary>
    Error,

    /// <summary>
    /// Single-line text input prompt (new profile name etc.).  Neutral
    /// header.  Selected by input-style dialogs implicitly — callers do
    /// not pass this value.
    /// </summary>
    Input,
}

/// <summary>Kind of a single inline run inside a <see cref="DialogMessage"/>.</summary>
public enum DialogSegmentKind
{
    /// <summary>Plain prose, default font.</summary>
    Text,

    /// <summary>Bold emphasis, otherwise default font.</summary>
    Bold,

    /// <summary>
    /// File-system path.  Hosts render this monospace + accent and make it
    /// click-to-copy with a small clipboard glyph as a discoverability
    /// affordance.
    /// </summary>
    Path,

    /// <summary>
    /// Hyperlink.  Hosts render this accent + underline; click opens
    /// <see cref="DialogSegment.Url"/> in the user's default browser via
    /// the platform shell (no in-app browser).
    /// </summary>
    Hyperlink,

    /// <summary>
    /// Code / JSON / pre-formatted block.  Hosts render this in a
    /// monospace family (Consolas / Menlo / fallback) and preserve
    /// whitespace.  Used for embedding raw JSON, schema-validation
    /// error blocks, command snippets, etc. into dialog prose.
    /// </summary>
    Code,
}

/// <summary>One inline run inside a <see cref="DialogMessage"/>.</summary>
/// <param name="Kind">How the host should render the segment.</param>
/// <param name="Value">
/// The text to render — for <see cref="DialogSegmentKind.Path"/> this is
/// the path itself; for <see cref="DialogSegmentKind.Hyperlink"/> it is
/// the link label (use the same value as <paramref name="Url"/> when the
/// caller wants the URL itself shown).
/// </param>
/// <param name="Url">
/// Target URL for <see cref="DialogSegmentKind.Hyperlink"/>.  Ignored for
/// every other segment kind.
/// </param>
public sealed record DialogSegment(
    DialogSegmentKind Kind,
    string Value,
    string? Url = null);

/// <summary>
/// Multi-segment message body for confirm / alert dialogs.  Built via
/// <see cref="DialogMessage.Builder"/> so the call site reads as prose
/// with <see cref="DialogSegmentKind.Path"/> / <see cref="DialogSegmentKind.Hyperlink"/>
/// runs interleaved.  A plain <see cref="string"/> can be promoted via
/// <see cref="Plain"/>; existing <c>string message</c> overloads of
/// dialog services should do this automatically so previously-written
/// call sites continue to work unchanged.
/// </summary>
/// <example>
/// SDK-side helper that produces a domain-aware dialog model:
/// <code>
/// public DialogMessage BuildApplyProfileDialog(string profileName, string targetPath) =>
///     DialogMessage.Builder()
///         .Text("Apply profile '").Bold(profileName).Text("' to ")
///         .Path(targetPath)
///         .Text("? See ").Hyperlink("the docs", "https://docs.claude.com/profiles").Text(".")
///         .Build();
/// </code>
/// </example>
public sealed class DialogMessage
{
    /// <summary>The ordered list of inline runs that compose the message.</summary>
    public IReadOnlyList<DialogSegment> Segments { get; }

    public DialogMessage(IReadOnlyList<DialogSegment> segments)
    {
        Segments = segments ?? throw new ArgumentNullException(nameof(segments));
    }

    /// <summary>Wrap a plain string as a single <see cref="DialogSegmentKind.Text"/> segment.</summary>
    public static DialogMessage Plain(string text)
    {
        return new DialogMessage([new DialogSegment(DialogSegmentKind.Text, text ?? string.Empty)]);
    }

    /// <summary>Start a fluent builder for a multi-segment message.</summary>
    public static DialogMessageBuilder Builder()
    {
        return new DialogMessageBuilder();
    }
}

/// <summary>
/// Fluent builder for <see cref="DialogMessage"/>.  Each method appends
/// one segment; <see cref="Build"/> snapshots the segment list into the
/// resulting <see cref="DialogMessage"/>.
/// </summary>
public sealed class DialogMessageBuilder
{
    private readonly List<DialogSegment> _segments = [];

    /// <summary>Append plain prose.</summary>
    public DialogMessageBuilder Text(string? s)
    {
        _segments.Add(new DialogSegment(DialogSegmentKind.Text, s ?? string.Empty));
        return this;
    }

    /// <summary>Append bold-emphasised prose.</summary>
    public DialogMessageBuilder Bold(string? s)
    {
        _segments.Add(new DialogSegment(DialogSegmentKind.Bold, s ?? string.Empty));
        return this;
    }

    /// <summary>
    /// Append a file-system path.  The host renders monospace + accent and
    /// makes the segment click-to-copy with a small clipboard affordance.
    /// </summary>
    public DialogMessageBuilder Path(string? path)
    {
        _segments.Add(new DialogSegment(DialogSegmentKind.Path, path ?? string.Empty));
        return this;
    }

    /// <summary>
    /// Append a hyperlink.  Click opens <paramref name="url"/> in the
    /// user's default browser; the visible label is <paramref name="label"/>.
    /// </summary>
    public DialogMessageBuilder Hyperlink(string? label, string? url)
    {
        _segments.Add(new DialogSegment(
            DialogSegmentKind.Hyperlink,
            label ?? string.Empty,
            url));
        return this;
    }

    /// <summary>
    /// Append a code / JSON / pre-formatted block.  The host renders the
    /// segment in a monospace family with whitespace preserved.  Suitable
    /// for embedding raw JSON snippets, schema-validation error bodies,
    /// command lines, etc. into dialog prose.
    /// </summary>
    public DialogMessageBuilder Code(string? s)
    {
        _segments.Add(new DialogSegment(DialogSegmentKind.Code, s ?? string.Empty));
        return this;
    }

    /// <summary>Snapshot the current segments into an immutable <see cref="DialogMessage"/>.</summary>
    public DialogMessage Build()
    {
        return new DialogMessage([.. _segments]);
    }
}