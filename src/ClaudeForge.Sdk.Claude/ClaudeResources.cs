namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

/// <summary>
/// The logical names of the files embedded under this assembly's <c>Assets/</c>: Claude's schemas,
/// enum descriptions and model catalog.
/// </summary>
/// <remarks>
/// An embedded resource's logical name is <c>&lt;RootNamespace&gt;.&lt;folder path&gt;.&lt;file&gt;</c>,
/// and the compiler cannot see a lookup's dependency on it. So the prefix is <b>derived</b> from
/// this type's namespace rather than written out, and a rename of the root namespace that the
/// resources did not follow fails loudly in <c>EmbeddedResourceFiles</c>' constructor and in
/// <c>ClaudeResourceNameTests</c>, instead of every read quietly answering <see langword="null"/>.
/// </remarks>
internal static class ClaudeResources
{
    /// <summary>This assembly's root namespace, taken from a type that lives at the root.</summary>
    public static readonly string RootNamespace =
        typeof(ClaudeResources).Namespace
        ?? throw new InvalidOperationException(
            "ClaudeResources must live in the assembly's root namespace — embedded-resource "
            + "logical names are derived from it.");

    /// <summary>The prefix of every resource under <c>Assets/Schemas/</c>, with the trailing dot.</summary>
    public static readonly string SchemasPrefix = $"{RootNamespace}.Assets.Schemas.";

    /// <summary>The prefix of every resource under <c>Assets/Descriptions/</c>, with the trailing dot.</summary>
    public static readonly string DescriptionsPrefix = $"{RootNamespace}.Assets.Descriptions.";

    /// <summary>The prefix of every resource under <c>Assets/ModelCatalog/</c>, with the trailing dot.</summary>
    public static readonly string ModelCatalogPrefix = $"{RootNamespace}.Assets.ModelCatalog.";

    /// <summary>
    /// The bytes of <c>Assets/ModelCatalog/&lt;fileName&gt;</c>, or <see langword="null"/> when it is
    /// not embedded.
    /// </summary>
    public static byte[]? TryReadModelCatalog(string fileName)
    {
        using Stream? stream = typeof(ClaudeResources).Assembly.GetManifestResourceStream(ModelCatalogPrefix + fileName);
        if (stream is null)
        {
            return null;
        }

        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
