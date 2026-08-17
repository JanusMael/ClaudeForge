namespace Bennewitz.Ninja.AgentForge.Jsonc;

/// <summary>
/// A replacement of one span of the original text. Insertions are zero-length spans;
/// deletions are empty replacements.
/// </summary>
/// <param name="Start">Index of the first replaced character.</param>
/// <param name="Length">Number of characters replaced. Zero for a pure insertion.</param>
/// <param name="NewText">Replacement text. Empty for a pure deletion.</param>
public readonly record struct TextEdit(int Start, int Length, string NewText)
{
    /// <summary>Index one past the last replaced character.</summary>
    public int End => Start + Length;

    /// <summary>
    /// Apply <paramref name="edits"/> to <paramref name="source"/> and return the result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applies back to front so each edit's offsets stay valid against the original text —
    /// the alternative, tracking a running delta, is the classic place this kind of code
    /// goes wrong.
    /// </para>
    /// <para>
    /// Overlapping edits throw rather than silently producing mangled output. Two edits
    /// fighting over the same span means the caller built an incoherent change set, and
    /// the honest response to that is to refuse, not to pick a winner.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">An edit lies outside the source.</exception>
    /// <exception cref="InvalidOperationException">Two edits overlap.</exception>
    public static string Apply(string source, IEnumerable<TextEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(edits);

        List<TextEdit> ordered = [.. edits];
        if (ordered.Count == 0)
        {
            return source;
        }

        foreach (TextEdit edit in ordered)
        {
            if (edit.Start < 0 || edit.Length < 0 || edit.End > source.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(edits),
                    $"Edit [{edit.Start}, {edit.End}) is outside the source of length {source.Length}.");
            }
        }

        // Descending by start; for equal starts, the longer span first so a
        // zero-length insertion at the same offset is detected as overlapping.
        ordered.Sort(static (a, b) => a.Start != b.Start
                         ? b.Start.CompareTo(a.Start)
                         : b.Length.CompareTo(a.Length));

        for (int i = 1; i < ordered.Count; i++)
        {
            TextEdit later = ordered[i - 1];
            TextEdit earlier = ordered[i];
            if (earlier.End > later.Start)
            {
                throw new InvalidOperationException(
                    $"Overlapping edits: [{earlier.Start}, {earlier.End}) and "
                    + $"[{later.Start}, {later.End}).");
            }
        }

        System.Text.StringBuilder sb = new(source);
        foreach (TextEdit edit in ordered)
        {
            sb.Remove(edit.Start, edit.Length);
            sb.Insert(edit.Start, edit.NewText);
        }

        return sb.ToString();
    }
}
