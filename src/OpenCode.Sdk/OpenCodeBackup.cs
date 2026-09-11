using Bennewitz.Ninja.AgentForge.Core.Backup;

namespace Bennewitz.Ninja.OpenCode.Sdk;

/// <summary>
/// The backup engine OpenCode's clients use — one that knows how to RESTORE OpenCode archives, not
/// just write them.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b><c>BackupEngine.Default</c> is the wrong engine here, and the failure is silent.</b> A
/// backup takes its products from <c>BackupRequest.Products</c>, so the default engine happily
/// WRITES an archive full of <c>OpenCode/</c> entries. A restore is driven by the archive instead,
/// and resolves folder names through the engine's own restorable-product list — which on the
/// default engine is Claude Code and Claude Desktop. The result is a one-way backup: the archive
/// exists, the restore reports success, and not one file comes back.
/// </para>
/// <para>
/// ⚠ <b>Both products, not just the one a given client edits.</b> The TUI client and the config
/// client each back up their own product, but a user restoring picks an ARCHIVE, and an archive may
/// carry either. Narrowing this to <c>[Product]</c> per client would make each client unable to
/// restore the other's archives while still listing them as restorable.
/// </para>
/// </remarks>
internal static class OpenCodeBackup
{
    /// <summary>
    /// Shared engine, mirroring <see cref="BackupEngine.Default"/>'s single-instance shape. The
    /// engine holds no per-operation state, so one instance is safe to share.
    /// </summary>
    internal static readonly BackupEngine Engine = new(restorableProducts: OpenCodeProducts.All);
}
