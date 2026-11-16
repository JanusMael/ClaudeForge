using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests;

/// <summary>
/// A descriptor for loading one of the schemas this assembly still bundles, under any URL.
/// </summary>
/// <remarks>
/// The registry used to take a URL and a file name and read the file from its own manifest; since
/// plans/00008 step 6 it takes a descriptor and reads through
/// <see cref="ProductDescriptor.BundledFiles"/>. These tests exercise the load chain, not a product,
/// so they keep naming a URL and a file and get a descriptor that answers both.
/// </remarks>
internal static class BundledProduct
{
    /// <summary>A descriptor for <paramref name="schemaFileName"/> fetched from <paramref name="url"/>.</summary>
    internal static ProductDescriptor For(string url, string schemaFileName) =>
        new("test-" + schemaFileName, schemaFileName, url, schemaFileName, "Test")
        {
            BundledFiles = SchemaRegistry.CoreBundledFiles,
        };

    /// <summary>A descriptor for <paramref name="schemaFileName"/>, with a URL nothing serves.</summary>
    internal static ProductDescriptor Named(string schemaFileName) =>
        For("https://example.invalid/" + schemaFileName, schemaFileName);
}
