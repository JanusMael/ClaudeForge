using System.IO.Compression;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.OpenCode.Sdk;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests.Backup;

/// <summary>
/// What a backup captures when <c>$OPENCODE_CONFIG_DIR</c> moves the config that loads.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>Written because the answer was "nothing", and the page said "Backup saved".</b> Measured
/// 2026-09-12: with the variable pointed at a directory holding <c>opencode.json</c> and
/// <c>tui.json</c>, a backup of <see cref="OpenCodeProducts.Config"/> produced an archive whose
/// complete contents were <c>Schemas/opencode-config.json</c> and <c>manifest.json</c> — and
/// reported success. The section's destination was <c>DefaultGlobalDirectory()</c> while every
/// read path in the SDK resolves through <c>GlobalDirectory(env)</c>.
/// </para>
/// <para>
/// ⚠ <b>Why <see cref="OpenCodeBackupRoundTripTests"/> could not see it, and why this class exists
/// separately.</b> That class redirects the HOME directory through
/// <c>PlatformPaths.TestUserProfileOverride</c> and then writes into
/// <c>DefaultGlobalDirectory()</c>. It never sets the variable, so the default and redirected roots
/// are the same folder for its whole run and the divergence has nowhere to appear. A test that
/// does not set the variable cannot fail this way no matter how thorough it is about everything
/// else.
/// </para>
/// <para>
/// ⭐ Both roots are live when they differ — the variable redirects the config that LOADS, the
/// default root stays live for plugin discovery — so the archive carries both, under
/// <c>config/</c> and <c>config-default/</c>. The second is gated at backup time so the unset case
/// does not write every file twice; see <c>ProductArchiveSection.IncludeWhen</c> for why the gate
/// is never consulted on restore.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeRedirectedConfigBackupTests
{
    private string _fakeHome = string.Empty;
    private string _defaultRoot = string.Empty;
    private string _redirected = string.Empty;

    /// <summary>The engine the app wires, not one configured the way this test would like.</summary>
    private static BackupEngine Engine => OpenCodeBackup.Engine;

    [TestInitialize]
    public void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(), "ocredir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fakeHome);
        BackupEngine.InvalidateListCache();
        PlatformPaths.TestUserProfileOverride = _fakeHome;

        _defaultRoot = OpenCodePaths.DefaultGlobalDirectory();
        _redirected = Path.Combine(_fakeHome, "elsewhere", "opencode");
        Directory.CreateDirectory(_defaultRoot);
        Directory.CreateDirectory(_redirected);
        Directory.CreateDirectory(OpenCodePaths.DataDirectory());
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", null);
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_fakeHome))
            {
                Directory.Delete(_fakeHome, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    /// <summary>
    /// ⛔ The regression. A redirected config is archived, and comes back.
    /// </summary>
    [TestMethod]
    public async Task ARedirectedConfig_IsArchivedAndRestores()
    {
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", _redirected);

        // Premise, asserted rather than assumed: the SDK agrees this is the config that loads.
        // Without it a passing test could merely mean the variable was ignored on both sides.
        Assert.AreEqual(
            Path.Combine(_redirected, "opencode.json"),
            OpenCodePaths.GlobalConfigPath(OpenCodeEnvironment.FromProcess()),
            "Premise failed: the SDK does not treat the redirected path as the live config.");

        Write(Path.Combine(_redirected, "opencode.json"), """{"model":"anthropic/claude-opus-5"}""");
        Write(Path.Combine(_redirected, "tui.json"), """{"theme":"opencode"}""");
        Write(Path.Combine(_redirected, "plugins", "gk-hooks.js"), "export const hooks = {};");

        string dest = Path.Combine(_fakeHome, "backup-redirected.zip");
        BackupResult create = await Engine.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Products = [OpenCodeProducts.Config],
        });
        Assert.IsTrue(create.Succeeded, create.Message);

        List<string> entries = Entries(dest);
        CollectionAssert.Contains(entries, "OpenCode/config/opencode.json");
        CollectionAssert.Contains(entries, "OpenCode/config/tui.json");
        CollectionAssert.Contains(entries, "OpenCode/config/plugins/gk-hooks.js");

        // ⛔ The round trip, not the write. An archive that cannot restore is the failure mode the
        // engine invariant exists for, and entry assertions alone cannot see it.
        Directory.Delete(_redirected, recursive: true);
        Directory.CreateDirectory(_redirected);

        RestoreResult restore = await Engine.RestoreAsync(Single(dest));
        Assert.IsTrue(restore.Succeeded, restore.Message);

        Assert.IsTrue(File.Exists(Path.Combine(_redirected, "opencode.json")),
            "opencode.json did not come back to the redirected root.");
        Assert.IsTrue(File.Exists(Path.Combine(_redirected, "tui.json")),
            "tui.json did not come back to the redirected root.");
        Assert.IsTrue(File.Exists(Path.Combine(_redirected, "plugins", "gk-hooks.js")),
            "The plugins subtree did not come back.");
    }

    /// <summary>
    /// The default root travels too, because plugin discovery keeps reading it.
    /// </summary>
    [TestMethod]
    public async Task WhenRedirected_TheDefaultRootIsAlsoArchived_UnderItsOwnFolder()
    {
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", _redirected);

        Write(Path.Combine(_redirected, "opencode.json"), """{"model":"x/y"}""");
        Write(Path.Combine(_defaultRoot, "plugins", "left-behind.js"), "// still loaded");

        string dest = Path.Combine(_fakeHome, "backup-both.zip");
        BackupResult create = await Engine.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Products = [OpenCodeProducts.Config],
        });
        Assert.IsTrue(create.Succeeded, create.Message);

        List<string> entries = Entries(dest);
        CollectionAssert.Contains(entries, "OpenCode/config/opencode.json");
        CollectionAssert.Contains(entries, "OpenCode/config-default/plugins/left-behind.js");
    }

    /// <summary>
    /// ⛔ With no redirect, the two roots are one directory and nothing is archived twice.
    /// </summary>
    /// <remarks>
    /// The failure this guards is not an error — it is a correct-looking archive carrying two
    /// copies of every config file under two names, which then restores each of them twice.
    /// </remarks>
    [TestMethod]
    public async Task WithNoRedirect_NothingIsArchivedTwice()
    {
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", null);

        Assert.AreEqual(
            _defaultRoot,
            OpenCodePaths.GlobalDirectory(OpenCodeEnvironment.FromProcess()),
            "Premise failed: with no variable set, the live root must BE the default root.");

        Write(Path.Combine(_defaultRoot, "opencode.json"), """{"model":"x/y"}""");

        string dest = Path.Combine(_fakeHome, "backup-plain.zip");
        BackupResult create = await Engine.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Products = [OpenCodeProducts.Config],
        });
        Assert.IsTrue(create.Succeeded, create.Message);

        List<string> entries = Entries(dest);
        CollectionAssert.Contains(entries, "OpenCode/config/opencode.json");
        Assert.AreEqual(0, entries.Count(e => e.StartsWith("OpenCode/config-default/", StringComparison.Ordinal)),
            "The default root was archived a second time under config-default/, duplicating every "
            + "file in the archive: " + string.Join(", ", entries));
    }

    /// <summary>
    /// A trailing separator on the variable is still the same directory.
    /// </summary>
    /// <remarks>
    /// The gate compares normalised full paths precisely so a user's <c>~/.config/opencode/</c>
    /// does not read as a different root and duplicate the whole archive.
    /// </remarks>
    [TestMethod]
    public async Task ARedirectSpelledDifferentlyButPointingHome_StillDoesNotDuplicate()
    {
        Environment.SetEnvironmentVariable(
            "OPENCODE_CONFIG_DIR", _defaultRoot + Path.DirectorySeparatorChar);

        Write(Path.Combine(_defaultRoot, "opencode.json"), """{"model":"x/y"}""");

        string dest = Path.Combine(_fakeHome, "backup-trailing.zip");
        BackupResult create = await Engine.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Products = [OpenCodeProducts.Config],
        });
        Assert.IsTrue(create.Succeeded, create.Message);

        List<string> entries = Entries(dest);
        Assert.AreEqual(0, entries.Count(e => e.StartsWith("OpenCode/config-default/", StringComparison.Ordinal)),
            "A trailing separator made one directory look like two: " + string.Join(", ", entries));
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static List<string> Entries(string zipPath)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        return [.. zip.Entries.Select(e => e.FullName)];
    }

    private BackupEntry Single(string archivePath)
    {
        BackupEntry? entry = Engine.TryReadEntry(archivePath);
        Assert.IsNotNull(entry, "The archive must be readable as a backup entry.");
        return entry!;
    }
}
