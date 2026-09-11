namespace Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

/// <summary>
/// A subdirectory of a product's home that a backup does not archive.
/// </summary>
/// <remarks>
/// <b>These were eight <c>if</c> statements comparing hardcoded <c>~/.claude</c> directory
/// names</b> — <c>statsig</c>, <c>shell-snapshots</c>, <c>local</c> and the rest — inside a
/// product-neutral engine. Every one is a statement about one product's layout, so they belong to
/// the product.
/// </remarks>
/// <param name="Name">Directory name, compared case-insensitively.</param>
/// <param name="Reason">
/// Why it is skipped. Documentation, and the answer when a user asks why something is missing from
/// their archive.
/// </param>
/// <param name="IncludedInFullBackup">
/// <see langword="true"/> when a Full backup archives this directory anyway;
/// <see langword="false"/> (the default) when no mode does.
/// <para>
/// ⚠ <b>A bool rather than a mode, and the reason is layering, not laziness.</b> The mode enum
/// lives in <c>AgentForge.Core.Backup</c> and this assembly is BCL-only by design. A string mode
/// name would restore generality at the cost of compile-time safety — a typo would silently mean
/// "never included", which is the failure this whole conversion exists to remove.
/// </para>
/// <para>
/// It also loses nothing today: there are exactly two cases. The sharing-targeted mode is defined
/// as <i>the same file scope as the standard mode</i>, so it never includes something the standard
/// mode skips, and a third gate has nowhere to come from. If one ever does, widen this
/// deliberately rather than by adding a second bool.
/// </para>
/// </param>
public sealed record ProductSkippedSubdir(
    string Name,
    string Reason,
    bool IncludedInFullBackup = false);
