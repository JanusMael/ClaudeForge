using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.OpenCode.Sdk;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests.Backup;

/// <summary>
/// OpenCode's backup layout — the first product outside <c>AgentForge.Core</c> to declare one.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>This suite is also the proof that the seam works.</b> The backup engine's archive paths,
/// its home skip rules and the restore engine's section list were each converted from a decision
/// tree into data, and every one then had to name Claude's descriptors — because
/// <c>OpenCodeProducts</c> lives here and <c>AgentForge.Core</c> must never reference it
/// (<c>AssemblyLayeringTests</c> enforces that). The layout hanging off the descriptor is what lets
/// the dependency point the right way.
/// </para>
/// <para>
/// ⚠ <b>An earlier version of this file asserted the OPPOSITE</b> — that
/// <c>OpenCodeProducts</c> declared no layout, so the absence stayed deliberate while the engine
/// could not yet act on one. That test failed the moment this layout landed, which is what it was
/// for: it forced the claim to be re-read rather than letting a layout appear quietly beside an
/// engine that would ignore it.
/// </para>
/// </remarks>
[TestClass]
public sealed class CrossAssemblyBackupLayoutTests
{
    private static ProductBackupLayout Layout => OpenCodeProducts.Config.Backup;

    [TestMethod]
    public void TheConfigRoot_IsArchivedWholeSoItsGitignoreIsHonoured()
    {
        // ⭐ A plain Directory section, NOT a Home walk with a skip list. ZipArchiveWriter reads the
        // .gitignore in the directory it is handed, and OpenCode maintains one there naming exactly
        // the regenerable files. Honouring it is self-maintaining; transcribing its entries into a
        // skip list is what made two earlier plan drafts wrong — the real file has five entries,
        // and the drafts listed three.
        ProductArchiveSection config = Section("config");

        Assert.IsTrue(config.IsDirectory);
        Assert.IsFalse(config.IsProductHome,
            "A Home walk would consult SkippedSubdirs and bypass the .gitignore that does this job.");
        Assert.AreEqual(0, Layout.SkippedSubdirs.Count,
            "The .gitignore is the skip list. A hardcoded one here would drift from it.");
    }

    [TestMethod]
    public void TheDatabase_AndBothSidecars_AreDeclaredTogether()
    {
        // ⛔ All three or none. opencode.db runs journal_mode=wal, so a copy without its -wal is a
        // stale snapshot by construction. Declaring only the .db would produce an archive that
        // restores a database missing its most recent transactions — silently.
        string[] expected = ["data/opencode.db", "data/opencode.db-wal", "data/opencode.db-shm"];

        CollectionAssert.IsSubsetOf(
            expected,
            Layout.Sections.Select(s => string.Join('/', s.SubPath)).ToArray());
    }

    [TestMethod]
    public void EveryDatabaseSection_RequiresTheCredentialOptIn()
    {
        // ⛔ account and control_account carry access_token + refresh_token, credential carries
        // `value`, session_share carries `secret` — pinned by OpenCodeSecretColumns, with
        // OpenCodeDatabaseSchemaTests reddening if upstream drifts. Sanitized cannot strip SQLite
        // rows, so an un-gated database section would put plaintext tokens into an archive whose
        // entire purpose is being shareable.
        foreach (ProductArchiveSection section in Layout.Sections)
        {
            string path = string.Join('/', section.SubPath);
            if (!path.StartsWith("data/opencode.db", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.IsTrue(section.RequiresCredentialOptIn,
                $"{path} is credential-bearing and must never be archived without an explicit opt-in.");
        }
    }

    [TestMethod]
    public void TheConfigSection_IsNotGatedOnCredentials()
    {
        // The mirror of the test above: gating the config root on the credential opt-in would make
        // an ordinary backup silently empty, which is the failure mode that looks like success.
        Assert.IsFalse(Section("config").RequiresCredentialOptIn);
    }

    [TestMethod]
    public void AuthJson_IsNotDeclaredAtAll()
    {
        // ⛔ Deliberately absent rather than opt-in. It is missing from an install that has never
        // signed in, which is NOT evidence it has gone away — so it stays excluded outright. It
        // also lives in the data root, outside the config directory the layout archives, so nothing
        // picks it up by accident.
        Assert.IsNull(Layout.CredentialFileName);

        Assert.IsFalse(
            Layout.Sections.Any(s => string.Join('/', s.SubPath).Contains("auth", StringComparison.Ordinal)),
            "auth.json must not be archived, by opt-in or otherwise.");
    }

    [TestMethod]
    public void TheTuiProduct_DeclaresNoLayoutOfItsOwn()
    {
        // ⚠ Not an oversight: tui.json lives in the config root, which the Config product already
        // archives whole. A second section pointing at the same file would archive it twice under
        // two prefixes and restore it twice.
        Assert.IsNull(OpenCodeProducts.Tui.BackupLayout);
    }

    [TestMethod]
    public void TheArchiveFolder_IsUnchangedByGainingALayout()
    {
        // ArchiveFolder is persisted into every archive; the layout is not. Adding one must not
        // disturb it, or archives written before this change stop being found.
        Assert.AreEqual("OpenCode", OpenCodeProducts.Config.ArchiveFolder);
        Assert.AreEqual("OpenCodeTui", OpenCodeProducts.Tui.ArchiveFolder);
    }

    [TestMethod]
    public void Destinations_ResolveAtCallTime()
    {
        // ⛔ OpenCodeProducts.Config is `static readonly`, so a resolved path would freeze to
        // whichever profile was current when this type initialised.
        string sandbox = Path.Combine(Path.GetTempPath(), "ocbl-" + Guid.NewGuid().ToString("N"));
        PlatformPaths.TestUserProfileOverride = sandbox;
        try
        {
            StringAssert.StartsWith(Section("config").Destination(), sandbox);
            StringAssert.StartsWith(Section("data/opencode.db").Destination(), sandbox);
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = null;
        }
    }

    private static ProductArchiveSection Section(string subPath) =>
        Layout.Sections.Single(s => string.Join('/', s.SubPath) == subPath);
}
