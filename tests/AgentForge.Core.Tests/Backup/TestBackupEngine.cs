using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Testing;

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
    /// <summary>
    /// The two neutral test products, rooted at the profile
    /// <c>PlatformPaths.TestUserProfileOverride</c> redirects and read each time a path is needed.
    /// </summary>
    /// <remarks>
    /// Claude's two descriptors until <c>plans/00008</c> step 6b-2; the tests of Claude's layout
    /// moved to <c>ClaudeForge.Sdk.Claude.Tests</c>, which keeps a <c>TestBackupEngine</c> of its
    /// own over them.
    /// </remarks>
    internal static IReadOnlyList<ProductDescriptor> Products { get; } =
    [
        NeutralTestProduct.For(() => PlatformPaths.UserProfile),
        NeutralTestProduct.Second(() => PlatformPaths.UserProfile),
    ];

    /// <summary>A shared engine over the default environment, for fixtures that do not relocate it.</summary>
    internal static BackupEngine Default { get; } = new(Products);
}
