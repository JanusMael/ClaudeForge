using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Backup;

/// <summary>
/// The shared engine the Claude backup fixtures use: Claude Code and Claude Desktop, bound to
/// <see cref="ClaudeEnvironment.Empty"/>.
/// </summary>
/// <remarks>
/// <para>
/// The tests here check Claude's layout through the engine: what its home skips, Desktop's
/// profiles, the credentials file, per-project directories, and the frozen v1 archive. The
/// engine's own behaviour is tested on the neutral test product in <c>AgentForge.Core.Tests</c>,
/// whose <c>TestBackupEngine</c> has the same shape.
/// </para>
/// <para>
/// ⚠ <b>The environment does not matter here.</b> These fixtures sandbox the profile through
/// <c>PlatformPaths.TestUserProfileOverride</c>, which wins over the environment by design. A
/// fixture that wants a RELOCATED home must construct its own engine with a real
/// <see cref="ClaudeEnvironment"/>.
/// </para>
/// <para>
/// ⛔ <b>Test-only, never in a product assembly.</b> The source-scan guard for home bypasses
/// treats a bare engine in production code as a defect.
/// </para>
/// </remarks>
internal static class TestBackupEngine
{
    /// <summary>Claude Code and Claude Desktop.</summary>
    internal static IReadOnlyList<ProductDescriptor> ClaudeProducts { get; } =
        [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty), SchemaRegistry.ClaudeDesktopProduct];

    /// <summary>A shared engine over the default environment, for fixtures that do not relocate it.</summary>
    internal static BackupEngine Default { get; } = new(ClaudeEnvironment.Empty, ClaudeProducts);
}
