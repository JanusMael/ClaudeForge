using System.Reflection;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Resources;

/// <summary>
/// Guards the one dependency the compiler cannot see: embedded-resource logical names are
/// built from the project's <c>RootNamespace</c>, so anything that renames the assembly
/// silently orphans every resource lookup unless the prefix moves with it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ResourceHelper"/> derives the prefix from <see cref="System.Type.Namespace"/>
/// so the two move together automatically — but that only holds while the C# namespace and
/// the MSBuild <c>RootNamespace</c> agree. They are set independently and nothing else
/// checks it. These tests are that check.
/// </para>
/// <para>
/// Without them the failure is invisible: <c>BackupEngine.BundleSchemas</c> would match zero
/// resources, produce an archive with no <c>Schemas/</c> folder, and <c>RestoreEngine</c>
/// would read that as "archive predates schema bundling" and skip validation entirely. A
/// backup tool that quietly stops validating is exactly the kind of bug that is only found
/// when someone needs the backup.
/// </para>
/// </remarks>
[TestClass]
public sealed class ResourceNamePrefixTests
{
    private static string[] ManifestNames => typeof(ResourceHelper).Assembly.GetManifestResourceNames();

    [TestMethod]
    public void SchemasPrefix_MatchesAtLeastOneRealManifestResource()
    {
        string[] matches = ManifestNames
                           .Where(n => n.StartsWith(ResourceHelper.SchemasPrefix, StringComparison.Ordinal))
                           .ToArray();

        Assert.IsTrue(
            matches.Length > 0,
            $"No embedded resource starts with '{ResourceHelper.SchemasPrefix}'. The derived "
            + "prefix no longer matches the assembly's RootNamespace, so BackupEngine.BundleSchemas "
            + "would bundle ZERO schemas and RestoreEngine would silently skip validation.\n"
            + "Actual manifest resources:\n  " + string.Join("\n  ", ManifestNames));
    }

    [TestMethod]
    public void AssetName_ResolvesTheBundledClaudeCodeSchema()
    {
        string name = ResourceHelper.AssetName("Schemas", "claude-code-settings.json");

        using Stream? stream = typeof(ResourceHelper).Assembly.GetManifestResourceStream(name);

        Assert.IsNotNull(
            stream,
            $"'{name}' did not resolve. Every bundled schema, the model catalog, and the enum "
            + "descriptions are read through this path, so a mismatch here breaks all of them.\n"
            + "Actual manifest resources:\n  " + string.Join("\n  ", ManifestNames));
    }

    /// <summary>
    /// The C# namespace and the MSBuild <c>RootNamespace</c> are configured in different
    /// files and nothing links them. Assert they still agree, because
    /// <see cref="ResourceHelper.RootNamespace"/> derives from the former while the resource
    /// names derive from the latter.
    /// </summary>
    [TestMethod]
    public void DerivedRootNamespace_AgreesWithTheActualResourceNames()
    {
        string[] assetResources = ManifestNames
                                  .Where(n => n.Contains(".Assets.", StringComparison.Ordinal))
                                  .ToArray();

        Assert.IsTrue(assetResources.Length > 0,
            "Expected at least one embedded resource under Assets/. Manifest:\n  "
            + string.Join("\n  ", ManifestNames));

        foreach (string resource in assetResources)
        {
            Assert.StartsWith(
                $"{ResourceHelper.RootNamespace}.Assets.",
                resource,
                StringComparison.Ordinal,
                $"Resource '{resource}' does not start with the derived root namespace "
                + $"'{ResourceHelper.RootNamespace}'. The C# namespace and the project's "
                + "RootNamespace have diverged — fix one or the other, do not paper over it "
                + "by hardcoding a prefix.");
        }
    }
}
