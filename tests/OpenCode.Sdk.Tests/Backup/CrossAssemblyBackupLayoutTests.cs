using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.OpenCode.Sdk;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests.Backup;

/// <summary>
/// Proves the seam the backup layout was moved onto <see cref="ProductDescriptor"/> to create: a
/// product declared in THIS assembly can supply its own archive sections and skip rules.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>This is the constraint that forced the design, stated as a test.</b> The backup engine's
/// archive paths, its home skip rules, and the restore engine's section list were each converted
/// from a decision tree into a table in <c>AgentForge.Core</c> — and every one of those tables then
/// had to name Claude's descriptors, because <c>OpenCodeProducts</c> lives here and
/// <c>AgentForge.Core</c> must never reference it (<c>AssemblyLayeringTests</c> enforces that).
/// Hanging the data off the descriptor is what lets the dependency point the right way.
/// </para>
/// <para>
/// ⚠ <b>This does NOT claim OpenCode backup works.</b> <c>OpenCodeProducts</c> deliberately declares
/// no layout yet: archiving an arbitrary product root is separate <c>BackupEngine</c> work, and a
/// descriptor advertising sections nothing writes would be a name-level claim of the exact kind
/// this plan keeps having to retract. What is asserted here is that the mechanism accepts a layout
/// authored outside <c>AgentForge.Core</c> — no more.
/// </para>
/// </remarks>
[TestClass]
public sealed class CrossAssemblyBackupLayoutTests
{
    [TestMethod]
    public void AProductInThisAssembly_CanDeclareItsOwnBackupLayout()
    {
        // Shaped from the real measurements in docs/opencode-install-probe.json: OpenCode's
        // footprint spans unrelated XDG roots, and `plugins/` is user-authored and irreplaceable.
        ProductDescriptor withLayout = OpenCodeProducts.Config with
        {
            BackupLayout = new ProductBackupLayout(
                Sections:
                [
                    ProductArchiveSection.File("opencode.json", () => "/config/opencode.json",
                        "Restoring opencode.json…"),
                    ProductArchiveSection.Directory("plugins", () => "/config/plugins",
                        "Restoring plugins…"),
                ],
                SkippedSubdirs:
                [
                    new("node_modules", "Regenerable dependencies — 99.97% of the config root."),
                ]),
        };

        Assert.AreEqual(2, withLayout.Backup.Sections.Count);
        Assert.AreEqual("OpenCode", withLayout.ArchiveFolder,
            "The archive folder is unchanged by adding a layout — it is persisted, the layout is not.");

        ProductSkippedSubdir rule = withLayout.Backup.SkippedSubdirs.Single();
        Assert.AreEqual("node_modules", rule.Name);
        Assert.IsFalse(rule.IncludedInFullBackup,
            "node_modules regenerates from package.json; no mode should archive 52 MB of it.");
    }

    [TestMethod]
    public void TheShippedOpenCodeProducts_DeclareNoLayoutYet()
    {
        // ⚠ Asserted so the absence stays deliberate. When OpenCode backup is implemented this test
        // is the thing that fails and asks for the claim to be re-read, rather than a layout
        // quietly appearing beside an engine that cannot act on it.
        foreach (ProductDescriptor product in OpenCodeProducts.All)
        {
            Assert.IsNull(product.BackupLayout,
                $"{product.Id} declares a backup layout, but archiving an arbitrary product root "
                + "is not implemented — a declared-but-unused layout is a name-level claim.");
        }
    }
}
