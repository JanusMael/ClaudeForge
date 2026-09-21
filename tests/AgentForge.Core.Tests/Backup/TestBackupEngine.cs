using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// The shared engine these backup fixtures use, bound to <see cref="ClaudeEnvironment.Empty"/>.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Replaces the deleted <c>BackupEngine.Default</c>, and deliberately only for tests.</b>
/// That static was removed from the product because it was the last way to obtain an engine
/// pointed at the default home: a composition root that reached for it would archive the wrong
/// directory for anyone who had set <c>CLAUDE_CONFIG_DIR</c>, with nothing reporting it.
/// </para>
/// <para>
/// ⚠ <b>Here the environment genuinely does not matter, and that is why this is safe.</b> These
/// fixtures sandbox the profile through <c>PlatformPaths.TestUserProfileOverride</c>, which wins
/// over the environment by design — so every path they touch is the scratch directory either way.
/// A fixture that wants to exercise a RELOCATED home must construct its own engine with a real
/// <see cref="ClaudeEnvironment"/> rather than reaching for this one.
/// </para>
/// <para>
/// ⛔ <b>Do not move this into the product, and do not add one to a product assembly.</b> The
/// source-scan guard for home bypasses treats a bare engine in production code as a defect; this
/// type exists on the test side of that line and nowhere else.
/// </para>
/// </remarks>
internal static class TestBackupEngine
{
    /// <summary>A shared engine over the default environment, for fixtures that do not relocate it.</summary>
    internal static BackupEngine Default { get; } = new(ClaudeEnvironment.Empty);
}
