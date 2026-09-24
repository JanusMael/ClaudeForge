using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// Pins the backup layout now carried on <see cref="ProductDescriptor"/>.
/// </summary>
/// <remarks>
/// The engines read these rows instead of holding their own tables, so the rows ARE the behaviour.
/// A rule silently dropped here changes what ships in every user's archive, and a section with a
/// wrong sub-path restores nothing while reporting success — neither throws, so both need a test
/// rather than a reading.
/// </remarks>
public sealed class ProductBackupLayoutTests
{
    [Fact]
    public void ClaudeCode_SkipRules_AreTheSevenTheEngineUsedToHardcode()
    {
        // ⚠ These were eight `if`s in BackupEngine.ShouldSkipHomeSubdir. If one goes missing the
        // engine starts archiving it — an app cache, a binary install dir, or the backup folder
        // itself, which would make backups nest.
        string[] expected =
            ["backups", "projects", "cache", "downloads", "statsig", "shell-snapshots", "local"];

        Assert.Equal(
            expected,
            SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty).Backup.SkippedSubdirs.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void ClaudeCode_OnlyProjects_SurvivesIntoAFullBackup()
    {
        // The mirror of FootprintCatalogTests.OnlySessionTranscripts_IsExcludedFromTheStandardBackup.
        // Those two assertions are the same fact seen from the backup side and the Memory-page side;
        // if they ever disagree, the page's "In Standard backup?" badge is lying to the user.
        ProductSkippedSubdir[] fullOnly =
        [
            .. SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty).Backup.SkippedSubdirs.Where(r => r.IncludedInFullBackup)
        ];

        Assert.Single(fullOnly);
        Assert.Equal("projects", fullOnly[0].Name);
    }

    [Fact]
    public void EverySkipRule_ExplainsItself()
    {
        // The reason is what a maintainer reads when a user asks why something is missing from
        // their archive. A blank one makes the rule unexplainable without git archaeology.
        foreach (ProductSkippedSubdir rule in SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty).Backup.SkippedSubdirs)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Reason), $"Rule '{rule.Name}' has no reason.");
        }
    }

    [Fact]
    public void TheArchiveSections_MatchTheFoldersTheWriterProduces()
    {
        // Claude Code contributes claude.json + the claude-dir subtree; Desktop contributes its
        // config, its profiles directory and the active-profile pointer. These sub-paths are what
        // a restore looks for inside the archive, and the frozen fixture in
        // BackupArchiveCompatibilityTests proves they are what a shipped build actually wrote.
        Assert.Equal(
            new[] { "claude.json", "claude-dir" },
            SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty).Backup.Sections.Select(s => string.Join('/', s.SubPath)).ToArray());

        Assert.Equal(
            new[] { "claude_desktop_config.json", "profiles", ".desktop-current" },
            SchemaRegistry.ClaudeDesktopProduct.Backup.Sections.Select(s => string.Join('/', s.SubPath)).ToArray());
    }

    [Fact]
    public void TheOnlyDirectorySections_AreTheOnesThatAreActuallyDirectories()
    {
        // ⛔ IsDirectory is the field whose failure is silent: a single file restored as a
        // directory finds nothing, restores nothing, and still reports success.
        Assert.True(Section(SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty), "claude-dir").IsDirectory);
        Assert.True(Section(SchemaRegistry.ClaudeDesktopProduct, "profiles").IsDirectory);

        Assert.False(Section(SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty), "claude.json").IsDirectory);
        Assert.False(Section(SchemaRegistry.ClaudeDesktopProduct, ".desktop-current").IsDirectory);
    }

    [Fact]
    public void Destinations_ResolveAtCallTime_NotAtTypeInitialisation()
    {
        // ⛔ The reason Destination is Func<string> rather than string. The descriptors are
        // `static readonly`, so a resolved path would be frozen to whichever profile was current
        // when SchemaRegistry's initialiser ran — in a sequential suite sharing a process, another
        // test's sandbox. The failure writes real files into a real home directory.
        string sandbox = Path.Combine(Path.GetTempPath(), "pbl-" + Guid.NewGuid().ToString("N"));
        PlatformPaths.TestUserProfileOverride = sandbox;
        try
        {
            string resolved = Section(SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty), "claude-dir").Destination();
            MessageAssert.StartsWith(sandbox, resolved,
                "The destination must follow the profile override that is current when it is CALLED.");
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = null;
        }
    }

    [Fact]
    public void AProductWithNoLayout_ContributesNothingRatherThanThrowing()
    {
        // A product can be schema-editable long before it is backup-able, so the engines must
        // treat a missing layout as "nothing to do" rather than as a crash on the first such
        // product they meet.
        ProductDescriptor bare = new("x", "X", "bundled://x", "x.json", "X");

        Assert.Null(bare.BackupLayout);
        Assert.Empty(bare.Backup.Sections);
        Assert.Empty(bare.Backup.SkippedSubdirs);
    }

    private static ProductArchiveSection Section(ProductDescriptor product, string subPath) =>
        product.Backup.Sections.Single(s => string.Join('/', s.SubPath) == subPath);
}
