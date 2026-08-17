namespace Bennewitz.Ninja.AgentForge.Core;

/// <summary>
/// Single source of truth for the logical names of resources embedded in this assembly.
/// </summary>
/// <remarks>
/// <para>
/// An embedded resource's logical name is <c>&lt;RootNamespace&gt;.&lt;folder path&gt;.&lt;file&gt;</c>,
/// and <c>RootNamespace</c> tracks the assembly name. Every lookup therefore depends on a
/// string that changes whenever the assembly is renamed — but the compiler cannot see that
/// dependency, so a rename breaks it at *runtime*, silently.
/// </para>
/// <para>
/// The worst instance: <c>BackupEngine.BundleSchemas</c> filters manifest resources by this
/// prefix. If the prefix stops matching, archives bundle <b>zero</b> schemas, and
/// <c>RestoreEngine</c> then treats the missing <c>Schemas/</c> folder as "archive predates
/// bundling" and <b>silently skips validation</b>. Nothing throws; backups just quietly stop
/// being validatable.
/// </para>
/// <para>
/// So the prefix is <b>derived</b> from <see cref="System.Type.Namespace"/> rather than
/// hardcoded, and every consumer goes through this type instead of re-concatenating its own
/// copy. <c>ResourceNamePrefixTests</c> additionally asserts that the derived value still
/// matches real manifest resources, which turns a <c>RootNamespace</c>-vs-namespace
/// divergence into a loud, targeted failure.
/// </para>
/// </remarks>
internal static class ResourceHelper
{
    /// <summary>
    /// This assembly's root namespace (e.g. <c>Bennewitz.Ninja.AgentForge.Core</c>),
    /// taken from a type that lives at the root rather than written out by hand.
    /// </summary>
    public static readonly string RootNamespace =
        typeof(ResourceHelper).Namespace
        ?? throw new InvalidOperationException(
            "ResourceHelper must live in the assembly's root namespace — embedded-resource "
            + "logical names are derived from it.");

    /// <summary>
    /// Logical-name prefix shared by every resource under <c>Assets/Schemas/</c>,
    /// including the trailing dot. Used to filter <c>GetManifestResourceNames()</c>.
    /// </summary>
    public static readonly string SchemasPrefix = $"{RootNamespace}.Assets.Schemas.";

    /// <summary>
    /// The logical name of the resource at <c>Assets/&lt;subNamespace&gt;/&lt;fileName&gt;</c>.
    /// </summary>
    public static string AssetName(string subNamespace, string fileName) =>
        $"{RootNamespace}.Assets.{subNamespace}.{fileName}";
}
