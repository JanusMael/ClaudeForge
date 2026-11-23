using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Testing;

namespace Bennewitz.Ninja.AgentForge.Core.Tests;

/// <summary>
/// Descriptors for exercising the schema registry's load chain: a neutral sample bundle, and
/// OpenCode's schemas as frozen fixtures.
/// </summary>
/// <remarks>
/// <para>
/// The registry takes a descriptor and reads through <see cref="ProductDescriptor.BundledFiles"/>.
/// These tests exercise the load chain, not a product, so they name a URL and a file and get a
/// descriptor that answers both.
/// </para>
/// <para>
/// ⭐ <b>The sample is not Claude's data, and it has the shapes the load-chain tests lean on.</b>
/// Its overlay is the only source of <c>model</c>'s <c>default</c> and <c>examples</c>, which is
/// what promotes <c>model</c> to an enum. So a tree in which <c>model</c> is an enum proves the
/// overlay reached it, whichever source won. Tests of Claude's own schema and overlay live in
/// <c>ClaudeForge.Sdk.Claude.Tests</c>.
/// </para>
/// </remarks>
internal static class BundledProduct
{
    /// <summary>The sample's schema file name.</summary>
    internal const string SampleSchemaFileName = "sample-settings.json";

    /// <summary>A URL for the sample that nothing serves.</summary>
    internal const string SampleUrl = "https://example.invalid/sample-settings.json";

    /// <summary>The sample's bundled schema: <c>model</c> a bare string, as an upstream would ship it.</summary>
    internal const string SampleSchemaJson = """
        {
          "$schema": "http://json-schema.org/draft-07/schema#",
          "type": "object",
          "properties": {
            "model": { "type": "string", "description": "The model to use." },
            "theme": { "type": "string", "enum": ["dark", "light"] },
            "verbose": { "type": "boolean" }
          }
        }
        """;

    /// <summary>The sample's overlay: <c>model</c>'s <c>default</c> and <c>examples</c>, and nothing else.</summary>
    internal const string SampleOverlayJson =
        """{ "properties": { "model": { "default": "sample-large", "examples": ["sample-large", "sample-small"] } } }""";

    /// <summary>A second schema in the sample, with no overlay, for tests that need two files.</summary>
    internal const string SampleOtherSchemaFileName = "sample-other.json";

    /// <summary>The sample's files, as a product would ship them.</summary>
    internal static InMemoryBundledFiles SampleFiles { get; } = new(new Dictionary<string, string>
    {
        [SampleSchemaFileName] = SampleSchemaJson,
        ["sample-settings.overlay.json"] = SampleOverlayJson,
        [SampleOtherSchemaFileName] = SampleSchemaJson,
    });

    /// <summary>The sample, fetched from <paramref name="url"/>.</summary>
    internal static ProductDescriptor Sample(string url = SampleUrl) =>
        new("sample", "Sample", url, SampleSchemaFileName, "Sample")
        {
            BundledFiles = SampleFiles,
        };

    /// <summary>The sample, with no upstream: its schema exists only as the bundled copy.</summary>
    internal static ProductDescriptor SampleWithNoUpstream { get; } =
        new("sample-bundled-only", "Sample, bundled only", "bundled://sample", SampleSchemaFileName, "SampleBundled")
        {
            BundledFiles = SampleFiles,
        };

    /// <summary>
    /// A descriptor for <paramref name="schemaFileName"/> fetched from <paramref name="url"/>, read
    /// from the sample's files.
    /// </summary>
    /// <remarks>For a file the sample does not carry, so the bundled source finds nothing.</remarks>
    internal static ProductDescriptor For(string url, string schemaFileName) =>
        new("test-" + schemaFileName, schemaFileName, url, schemaFileName, "Test")
        {
            BundledFiles = SampleFiles,
        };

    /// <summary>
    /// OpenCode's schemas, embedded in this test assembly as frozen real-world shapes: the root
    /// <c>$ref</c> of <c>opencode-config.json</c> and the overlay of <c>opencode-tui.json</c>.
    /// </summary>
    /// <remarks>
    /// The prefix is derived from this type's namespace, which is the project's root namespace,
    /// so a rename cannot leave it matching nothing; <see cref="EmbeddedResourceFiles"/> would
    /// refuse it on first use if it did.
    /// </remarks>
    internal static IBundledFiles OpenCodeFixtures { get; } = new EmbeddedResourceFiles(
        typeof(BundledProduct).Assembly, typeof(BundledProduct).Namespace + ".Fixtures.OpenCode.");

    /// <summary>The raw bytes of one of <see cref="OpenCodeFixtures"/>, with no overlay applied.</summary>
    internal static byte[] ReadOpenCodeFixture(string fileName) =>
        OpenCodeFixtures.TryRead(fileName)
        ?? throw new InvalidOperationException($"'{fileName}' is not embedded under Fixtures/OpenCode/.");

    /// <summary>
    /// A descriptor for one of <see cref="OpenCodeFixtures"/>, with a URL nothing serves.
    /// </summary>
    internal static ProductDescriptor Named(string schemaFileName) =>
        new("test-" + schemaFileName, schemaFileName, "https://example.invalid/" + schemaFileName, schemaFileName, "Test")
        {
            BundledFiles = OpenCodeFixtures,
        };
}
