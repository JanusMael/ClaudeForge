using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests;

/// <summary>
/// A descriptor for loading one of Claude's bundled files under any URL.
/// </summary>
/// <remarks>
/// These tests check Claude's data, not a product's layout, so they name a URL and a file and get
/// a descriptor that answers both, reading the files Claude's descriptors read. The descriptor's
/// id is derived from the file name, so two descriptors for two files are two products.
/// </remarks>
internal static class BundledProduct
{
    /// <summary>The files Claude's descriptors bundle.</summary>
    internal static IBundledFiles ClaudeFiles =>
        SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty).BundledFiles!;

    /// <summary>A descriptor for <paramref name="schemaFileName"/> fetched from <paramref name="url"/>.</summary>
    internal static ProductDescriptor For(string url, string schemaFileName) =>
        new("test-" + schemaFileName, schemaFileName, url, schemaFileName, "Test")
        {
            BundledFiles = ClaudeFiles,
        };

    /// <summary>A descriptor for <paramref name="schemaFileName"/>, with a URL nothing serves.</summary>
    internal static ProductDescriptor Named(string schemaFileName) =>
        For("https://example.invalid/" + schemaFileName, schemaFileName);
}
