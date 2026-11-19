using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Resources;

/// <summary>
/// Guards the dependency the compiler cannot see for Claude's bundled data: embedded-resource
/// logical names are built from the project's <c>RootNamespace</c>, and
/// <see cref="ClaudeResources"/> derives its prefixes from the C# namespace. Core's
/// <c>ResourceNamePrefixTests</c> is the same check for what Core still embeds.
/// </summary>
public sealed class ClaudeResourceNameTests
{
    private static string[] ManifestNames => typeof(ClaudeResources).Assembly.GetManifestResourceNames();

    public static TheoryData<string, string> Prefixes => new()
    {
        { nameof(ClaudeResources.SchemasPrefix), ClaudeResources.SchemasPrefix },
        { nameof(ClaudeResources.DescriptionsPrefix), ClaudeResources.DescriptionsPrefix },
        { nameof(ClaudeResources.ModelCatalogPrefix), ClaudeResources.ModelCatalogPrefix },
    };

    [Theory]
    [MemberData(nameof(Prefixes))]
    public void EachPrefix_MatchesAtLeastOneRealManifestResource(string name, string prefix)
    {
        Assert.True(
            ManifestNames.Any(n => n.StartsWith(prefix, StringComparison.Ordinal)),
            $"No embedded resource starts with {name} '{prefix}'. Every read through it would answer "
            + "null, and a backup would carry no schemas without saying so.\nActual manifest resources:\n  "
            + string.Join("\n  ", ManifestNames));
    }

    [Fact]
    public void BundledFiles_ReadsFromBothFolders_AndAnswersNullForAFileThatIsNotThere()
    {
        Assert.NotNull(ClaudeProducts.BundledFiles.TryRead("claude-code-settings.json"));
        Assert.NotNull(ClaudeProducts.BundledFiles.TryRead("claude-desktop-config.json"));
        // From the second prefix: the descriptions live in a folder of their own.
        Assert.NotNull(ClaudeProducts.BundledFiles.TryRead("claude-code-settings.enumdescriptions.json"));
        Assert.Null(ClaudeProducts.BundledFiles.TryRead("no-such-file.json"));
    }

    [Fact]
    public void TheModelCatalog_AndItsSchema_AreEmbedded()
    {
        Assert.NotNull(ClaudeResources.TryReadModelCatalog("model-catalog.json"));
        Assert.NotNull(ClaudeResources.TryReadModelCatalog("model-catalog.schema.json"));
        Assert.Null(ClaudeResources.TryReadModelCatalog("no-such-file.json"));
    }

    [Fact]
    public void DerivedRootNamespace_AgreesWithTheActualResourceNames()
    {
        string[] assetResources = [.. ManifestNames.Where(n => n.Contains(".Assets.", StringComparison.Ordinal))];

        Assert.NotEmpty(assetResources);
        foreach (string resource in assetResources)
        {
            MessageAssert.StartsWith(
                $"{ClaudeResources.RootNamespace}.Assets.",
                resource,
                StringComparison.Ordinal,
                $"Resource '{resource}' does not start with the derived root namespace "
                + $"'{ClaudeResources.RootNamespace}'. The C# namespace and the project's RootNamespace "
                + "have diverged — fix one or the other, do not paper over it by hardcoding a prefix.");
        }
    }

    /// <summary>
    /// plans/00008 step 6b-3 moved Claude's data out of <c>AgentForge.Core</c>. A copy left behind,
    /// or added back, would be read by nothing and drift from the one that is.
    /// </summary>
    [Fact]
    public void AgentForgeCore_EmbedsNoClaudeData()
    {
        string[] claude =
        [
            .. typeof(SchemaRegistry).Assembly.GetManifestResourceNames()
                .Where(n => n.Contains(".claude-", StringComparison.Ordinal)
                            || n.Contains(".model-catalog", StringComparison.Ordinal)),
        ];

        Assert.True(claude.Length == 0, "Claude data still embedded in AgentForge.Core:\n  " + string.Join("\n  ", claude));
    }
}
