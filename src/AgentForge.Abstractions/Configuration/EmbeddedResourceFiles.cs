using System.Reflection;

namespace Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

/// <summary>
/// <see cref="IBundledFiles"/> over the manifest resources of one assembly: a file name is looked
/// up under each resource-name prefix in turn.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>A prefix that matches no resource throws HERE, at construction.</b> An embedded
/// resource's logical name is <c>&lt;RootNamespace&gt;.&lt;folder path&gt;.&lt;file&gt;</c>, and the
/// compiler cannot see a lookup's dependency on it. A prefix that stops matching after a rename
/// would make every read answer <see langword="null"/>, and for a backup that is silent: the archive
/// carries ZERO schemas, and a restore reads the missing <c>Schemas/</c> folder as "made before
/// schemas were bundled" and skips validation. Failing when the product's descriptor is built turns
/// that into an exception on the first launch and in every test that touches the product.
/// </para>
/// <para>
/// Several prefixes, because a product keeps its schemas and its enum descriptions in separate
/// folders, and the folder is part of the resource name.
/// </para>
/// </remarks>
public sealed class EmbeddedResourceFiles : IBundledFiles
{
    private readonly Assembly _assembly;
    private readonly string[] _prefixes;

    /// <param name="assembly">The assembly whose manifest holds the files.</param>
    /// <param name="resourcePrefixes">
    /// Logical-name prefixes, each ending in a dot, tried in order; for example
    /// <c>"Contoso.Product.Assets.Schemas."</c>. Derive them from a type's namespace rather than
    /// writing them out, so they follow a rename of the assembly's root namespace.
    /// </param>
    /// <exception cref="ArgumentException">
    /// No prefix was given, or one of them matches no resource in <paramref name="assembly"/>.
    /// </exception>
    public EmbeddedResourceFiles(Assembly assembly, params string[] resourcePrefixes)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(resourcePrefixes);
        if (resourcePrefixes.Length == 0)
        {
            throw new ArgumentException("At least one resource-name prefix is required.", nameof(resourcePrefixes));
        }

        string[] names = assembly.GetManifestResourceNames();
        foreach (string prefix in resourcePrefixes)
        {
            if (!names.Any(name => name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    $"No manifest resource in {assembly.GetName().Name} starts with \"{prefix}\". "
                    + "The prefix is part of every bundled file's name, so with it wrong every read would "
                    + "find nothing, and a backup would carry no schemas without saying so.",
                    nameof(resourcePrefixes));
            }
        }

        _assembly = assembly;
        _prefixes = [.. resourcePrefixes];
    }

    /// <inheritdoc/>
    public byte[]? TryRead(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        foreach (string prefix in _prefixes)
        {
            using Stream? stream = _assembly.GetManifestResourceStream(prefix + fileName);
            if (stream is null)
            {
                continue;
            }

            using MemoryStream copy = new();
            stream.CopyTo(copy);
            return copy.ToArray();
        }

        return null;
    }
}
