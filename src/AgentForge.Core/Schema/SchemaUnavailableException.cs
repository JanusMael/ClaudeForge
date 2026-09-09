namespace Bennewitz.Ninja.AgentForge.Core.Schema;

/// <summary>
/// No source could supply a schema: the network did not answer and there is no bundled copy.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>This replaced a silent <c>"{}"</c> fallback, and that was the point.</b> An empty
/// JSON Schema permits <em>everything</em>, so returning one does not degrade validation — it
/// removes it, while every surface keeps reporting success. A document nothing has checked
/// would save cleanly, and the only symptom would be the editor rendering raw JSON where typed
/// controls belong.
/// </para>
/// <para>
/// Throwing is safe because the hosts already degrade a failed section visibly:
/// <c>MainWindowViewModel.InitializeAsync</c> opens each section independently and reports the
/// failures in its status line, so one unloadable schema costs that section rather than the
/// app. A section whose schema cannot be loaded genuinely is broken.
/// </para>
/// <para>
/// ⓘ Unreachable for every product this repo currently ships — all four have a bundled
/// resource, so step 3 of the chain always answers. It exists so that adding a product with a
/// URL and no bundled copy fails loudly instead of quietly validating nothing.
/// </para>
/// </remarks>
public sealed class SchemaUnavailableException : Exception
{
    /// <summary>Construct with the URL and bundled file name that were tried.</summary>
    public SchemaUnavailableException(string url, string cacheFileName)
        : base($"No source could supply the schema '{cacheFileName}'. The network did not "
               + $"answer for '{url}' and no bundled resource of that name exists. Refusing to "
               + "fall back to an empty schema, which would permit every value and make "
               + "save-validation report success without checking anything.")
    {
        Url = url;
        CacheFileName = cacheFileName;
    }

    /// <summary>The schema URL that was attempted.</summary>
    public string Url { get; }

    /// <summary>The bundled resource name that was attempted.</summary>
    public string CacheFileName { get; }
}
