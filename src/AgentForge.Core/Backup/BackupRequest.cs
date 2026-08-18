using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.AgentForge.Core.Backup;

/// <summary>
/// Input record for <c>BackupEngine.CreateAsync</c>.
/// Immutable by convention — callers build an instance and hand it off.
/// </summary>
public sealed record BackupRequest
{
    /// <summary>Absolute path to the <c>.zip</c> the engine should write.</summary>
    public required string DestinationZipPath { get; init; }

    /// <summary><see cref="BackupMode.SettingsOnly"/> (default) or <see cref="BackupMode.Full"/>.</summary>
    public BackupMode Mode { get; init; } = BackupMode.SettingsOnly;

    /// <summary>Which products to include in the archive, in the order they are bundled.</summary>
    /// <remarks>
    /// <para>
    /// Replaced <c>bool IncludeClaudeCode</c> / <c>bool IncludeClaudeDesktop</c>. Those two
    /// flags meant the request could describe exactly two products, and
    /// <see cref="BackupManifest.Clients"/> — which has always been a list — had to be
    /// reconstructed from them.
    /// </para>
    /// <para>
    /// <b>Required, not defaulted.</b> The pair it replaced both defaulted to
    /// <see langword="true"/>, so omitting them quietly backed up everything. For a set,
    /// the equivalent default would be a Claude-specific literal inside a
    /// product-neutral record — and getting it wrong means backing up the wrong products
    /// with no error. Every caller states what it wants.
    /// </para>
    /// </remarks>
    public required IReadOnlyList<ProductDescriptor> Products { get; init; }

    /// <summary>Whether <paramref name="product"/> is one of the requested products.</summary>
    /// <remarks>Compares on <see cref="ProductDescriptor.Id"/>, so a caller holding a
    /// separately-constructed descriptor for the same product still matches.</remarks>
    public bool Includes(ProductDescriptor product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return Products.Any(p => string.Equals(p.Id, product.Id, StringComparison.Ordinal));
    }

    /// <summary>
    /// True ⇒ also bundle <c>~/.claude/.credentials.json</c> (Windows/Linux only).
    /// macOS stores credentials in Keychain — the file is absent there and this flag
    /// has no effect.
    /// </summary>
    public bool IncludeCredentials { get; init; }

    /// <summary>
    /// Project root paths explicitly supplied by the caller. Merged at runtime with
    /// paths auto-discovered via <c>AdditionalDirectoriesResolver</c>.
    /// </summary>
    public IReadOnlyList<string> ExplicitProjectDirs { get; init; } = Array.Empty<string>();

    /// <summary>
    /// After a successful backup, prune older <c>backup-*.zip</c> files in the same
    /// directory keeping only the N most recent. <c>0</c> (default) disables retention.
    /// </summary>
    public int KeepLast { get; init; }
}