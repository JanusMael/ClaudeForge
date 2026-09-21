namespace Bennewitz.Ninja.AgentForge.Core.Schema;

/// <summary>
/// A schema still carried an external <c>$ref</c> after the strip, so it cannot be used.
/// </summary>
/// <remarks>
/// <para>
/// The strip that removes <c>models.dev</c>-style external references is <b>line-based</b>: it
/// deletes lines whose only content is an <c>http(s)</c> <c>$ref</c>. That is deliberate — the
/// alternative, parsing and re-serialising, reformats the whole document and turns every
/// refresh into an unreviewable diff. The cost is that a <c>$ref</c> sharing a line with its
/// sibling keys survives.
/// </para>
/// <para>
/// ⛔ <b>Upstream formats one key per line, which is the only reason that has never
/// mattered.</b> "Correct because of somebody else's whitespace" is exactly the kind of
/// assumption that should fail loudly rather than silently, because the consequence is not
/// cosmetic: an external <c>$ref</c> reaching the editor makes schema evaluation throw through
/// <c>ValidateWorkspaceAsync</c> → <c>SaveAsync</c> for any config that sets a model, and the
/// restore path's evaluate guard does not catch that exception type.
/// </para>
/// <para>
/// A <b>fetched</b> copy in this state is recoverable — the loader falls back to the bundled
/// resource, which the refresh script has already stripped. A <b>bundled</b> copy in this state
/// is a build-time defect and propagates, because there is nothing safer to fall back to and
/// <c>BundledOpenCodeSchemaTests</c> should have caught it first.
/// </para>
/// </remarks>
public sealed class UnstrippableSchemaRefException : Exception
{
    /// <summary>Construct for the schema file name that could not be stripped.</summary>
    public UnstrippableSchemaRefException(string cacheFileName)
        : base($"The schema '{cacheFileName}' still declares an external \"$ref\" after "
               + "stripping. The strip removes only lines whose sole content is the reference, "
               + "so this one shares a line with its siblings. Refusing it: an external $ref "
               + "makes schema evaluation throw on save for any document that reaches it.")
    {
        CacheFileName = cacheFileName;
    }

    /// <summary>The schema file name that was refused.</summary>
    public string CacheFileName { get; }
}
