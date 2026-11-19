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
