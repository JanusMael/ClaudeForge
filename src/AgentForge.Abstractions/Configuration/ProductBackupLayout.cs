namespace Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

/// <summary>
/// What a product contributes to a backup archive, and what a backup leaves out.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because three separate tables in <c>AgentForge.Core</c> had to name Claude's
/// descriptors.</b> The backup engine's archive paths, its <c>~/.claude</c> skip rules, and the
/// restore engine's section list were each converted from a decision tree into data — and each one
/// then could not accept a second product's rows, because that product's descriptor lives in an
/// assembly <c>AgentForge.Core</c> must not reference. Hanging the data off the descriptor is what
/// lets the product supply it from its own assembly.
/// </para>
/// <para>
/// ⚠ <b>Deliberately no <c>BackupMode</c> here.</b> That enum lives in <c>AgentForge.Core.Backup</c>
/// and this assembly is BCL-only by design, so importing it would invert the layering.
/// <see cref="ProductSkippedSubdir.IncludedInFullBackup"/> expresses the only gate that actually
/// exists instead — see its remarks.
/// </para>
/// </remarks>
/// <param name="Sections">
/// The restorable sections this product writes into an archive, in the order a restore applies
/// them. Order is user-visible: each reports a progress step.
/// </param>
/// <param name="SkippedSubdirs">
/// Subdirectories of the product's home that a backup does not archive.
/// </param>
public sealed record ProductBackupLayout(
    IReadOnlyList<ProductArchiveSection> Sections,
    IReadOnlyList<ProductSkippedSubdir> SkippedSubdirs)
{
    /// <summary>A layout that contributes nothing — the default for a product with no backup support.</summary>
    public static ProductBackupLayout Empty { get; } = new([], []);
}
