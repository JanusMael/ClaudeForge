using System.Text.RegularExpressions;
using Bennewitz.Ninja.AgentForge.Core.Updates;
using Bennewitz.Ninja.OpenCodeForge.Services;
using Bennewitz.Ninja.OpenCodeForge.ViewModels;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// The app-update check is wired to the tag shape this app actually publishes, and its persisted
/// preferences survive a state file written before they existed.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>The failure this exists for produces no error anywhere.</b> An update check whose tag
/// prefix disagrees with the release workflow's trigger simply never finds a release — for the
/// life of the installed copy. It looks exactly like "there are no updates", which is the same
/// thing a healthy check says most of the time.
/// </para>
/// <para>
/// ⚠ <b>Sequential by necessity.</b> These drive <c>WindowStateService</c>, which is keyed on
/// the process-wide <c>OPENCODE_CONFIG_DIR</c> environment variable. Two of them running
/// concurrently would read each other's sandbox.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class AppUpdateWiringTests
{
    private string _sandbox = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "ocf-upd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", _sandbox);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", null);
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file still held open by the OS is not a test failure.
        }
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "tests")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root by walking up from '{AppContext.BaseDirectory}'.");
    }

    // ── The cross-file contract ─────────────────────────────────────────────

    /// <summary>
    /// The tag prefix in code, in the publish table, and in the release workflow all agree.
    /// </summary>
    /// <remarks>
    /// Three files, one fact, and nothing but this test connects them: a C# constant, a
    /// PowerShell table row, and a YAML tag trigger. Change any one and the app either publishes
    /// under a tag it does not recognise or looks for one nothing publishes.
    /// </remarks>
    [TestMethod]
    public void TagPrefixAgreesWithThePublishTableAndTheReleaseWorkflow()
    {
        string repoRoot = FindRepoRoot();

        string table = File.ReadAllText(Path.Combine(
            repoRoot, "src", "publish", "PublishApps.ps1"));
        string workflow = File.ReadAllText(Path.Combine(
            repoRoot, ".github", "workflows", "release-opencodeforge.yml"));

        // The row is identified by its Name, then its TagPrefix read out of the same block.
        Match row = Regex.Match(
            table,
            @"Name\s*=\s*'OpenCodeForge'.*?TagPrefix\s*=\s*'(?<prefix>[^']*)'",
            RegexOptions.Singleline);

        Assert.IsTrue(
            row.Success,
            "Could not find OpenCodeForge's TagPrefix in src/publish/PublishApps.ps1. Either the "
            + "row moved or this regex no longer reads it — and then this test guards nothing.");

        Assert.AreEqual(
            AppUpdateService.TagPrefix,
            row.Groups["prefix"].Value,
            "AppUpdateService.TagPrefix and the publish table disagree. The table builds the "
            + "release URL the winget manifest points at; the constant is what the installed app "
            + "searches for. They must be the same string.");

        Assert.IsTrue(
            workflow.Contains($"'{AppUpdateService.TagPrefix}v", StringComparison.Ordinal),
            $"release-opencodeforge.yml has no tag trigger starting '{AppUpdateService.TagPrefix}v'. "
            + "Releases would publish under a tag this app's update check does not recognise, "
            + "and the check would report 'no updates' forever with no error anywhere.");
    }

    /// <summary>
    /// The scheme built from that prefix claims this app's tags and rejects the sibling's.
    /// </summary>
    [TestMethod]
    public void TheSchemeClaimsThisAppsTagsAndNotTheSiblings()
    {
        ReleaseTagScheme scheme = new(AppUpdateService.TagPrefix);

        Assert.IsTrue(
            scheme.TryParseVersion($"{AppUpdateService.TagPrefix}v2026.4.100", out Version? mine),
            "The scheme did not recognise this app's own tag shape.");
        Assert.AreEqual(new Version(2026, 4, 100), mine);

        Assert.IsFalse(
            scheme.TryParseVersion("v2026.3.810", out _),
            "The scheme claimed an UNPREFIXED tag, which belongs to the sibling app. Its "
            + "releases would be offered to this app's users as this app's download.");
    }

    // ── The persisted format ────────────────────────────────────────────────

    /// <summary>
    /// A state file written before the update preferences existed still loads, with the
    /// documented defaults.
    /// </summary>
    /// <remarks>
    /// ⭐ This is the compatibility direction that actually happens: a user updates the app and
    /// their existing state file has neither field. The check must default to ON (an absent
    /// preference is not an opt-out) and the dismissed list to empty (it could not have
    /// dismissed anything).
    /// </remarks>
    [TestMethod]
    public void AStateFileWithoutTheUpdateFieldsLoadsWithTheDocumentedDefaults()
    {
        string cacheDir = Path.Combine(_sandbox, "cache");
        Directory.CreateDirectory(cacheDir);
        File.WriteAllText(
            Path.Combine(cacheDir, "OpenCodeForge-gui-state.json"),
            """{"Width":1000,"Height":700,"IsMaximized":false}""");

        WindowState state = WindowStateService.Load();

        Assert.AreEqual(1000, state.Width, "The pre-existing geometry must survive.");
        Assert.IsTrue(
            state.CheckForUpdatesOnLaunch,
            "An absent CheckForUpdatesOnLaunch must default to true. A user who has never seen "
            + "the toggle has not opted out of anything.");
        Assert.AreEqual(
            0,
            state.Dismissed.Count,
            "An absent DismissedUpdateVersions must read as empty, not null — every caller "
            + "enumerates it.");
    }

    /// <summary>Both new fields round-trip through a save and load.</summary>
    [TestMethod]
    public void TheUpdatePreferencesRoundTrip()
    {
        WindowStateService.Save(WindowState.Default with
        {
            CheckForUpdatesOnLaunch = false,
            DismissedUpdateVersions = ["opencodeforge-v2026.4.100"],
        });

        WindowState reloaded = WindowStateService.Load();

        Assert.IsFalse(reloaded.CheckForUpdatesOnLaunch, "The opt-out did not persist.");
        CollectionAssert.AreEqual(
            new[] { "opencodeforge-v2026.4.100" },
            reloaded.Dismissed.ToArray(),
            "The dismissed-tag list did not persist.");
    }

    // ── The banner ──────────────────────────────────────────────────────────

    /// <summary>An available release the user has not dismissed shows the banner.</summary>
    [TestMethod]
    public void AnUndismissedReleaseShowsTheBanner()
    {
        UpdateBannerViewModel banner = new();

        banner.ApplyResult(UpdateCheckResult.UpdateAvailable(
            "opencodeforge-v2026.4.100", new Version(2026, 4, 100), "https://example.invalid/r"));

        Assert.IsTrue(banner.IsVisible, "The banner should surface for an undismissed release.");
        Assert.AreEqual("opencodeforge-v2026.4.100", banner.LatestTagName);
        Assert.IsTrue(banner.HasReleaseUrl, "A result carrying a URL should offer the link.");
    }

    /// <summary>A release already in the dismissed list stays hidden.</summary>
    [TestMethod]
    public void ADismissedReleaseStaysHidden()
    {
        WindowStateService.Save(WindowState.Default with
        {
            DismissedUpdateVersions = ["opencodeforge-v2026.4.100"],
        });

        UpdateBannerViewModel banner = new();
        banner.ApplyResult(UpdateCheckResult.UpdateAvailable(
            "opencodeforge-v2026.4.100", new Version(2026, 4, 100), null));

        Assert.IsFalse(
            banner.IsVisible,
            "A tag in the dismissed list must not resurface the banner on a later launch.");
    }

    /// <summary>
    /// Dismissing persists the tag, and a NEWER release still surfaces afterwards.
    /// </summary>
    /// <remarks>
    /// The second half is the contract that makes dismissal safe: it is per version, so
    /// dismissing once does not silence the app forever.
    /// </remarks>
    [TestMethod]
    public void DismissingIsPerVersionAndDoesNotSilenceLaterReleases()
    {
        UpdateBannerViewModel banner = new();
        banner.ApplyResult(UpdateCheckResult.UpdateAvailable(
            "opencodeforge-v2026.4.100", new Version(2026, 4, 100), null));
        Assert.IsTrue(banner.IsVisible, "Premise: the banner must be showing before it can be dismissed.");

        banner.DismissCommand.Execute(null);

        Assert.IsFalse(banner.IsVisible, "Dismiss should hide the banner.");
        CollectionAssert.Contains(
            WindowStateService.Load().Dismissed.ToArray(),
            "opencodeforge-v2026.4.100",
            "Dismiss must persist immediately — a crash before the next launch would otherwise "
            + "lose the choice.");

        // …and a later release is a different tag, so it is not suppressed.
        UpdateBannerViewModel next = new();
        next.ApplyResult(UpdateCheckResult.UpdateAvailable(
            "opencodeforge-v2026.5.101", new Version(2026, 5, 101), null));

        Assert.IsTrue(
            next.IsVisible,
            "Dismissing one version must not suppress a newer one — otherwise a single dismiss "
            + "silences the app permanently.");
    }

    /// <summary>A result with no update available hides the banner.</summary>
    [TestMethod]
    public void NoUpdateHidesTheBanner()
    {
        UpdateBannerViewModel banner = new();
        banner.ApplyResult(UpdateCheckResult.UpdateAvailable(
            "opencodeforge-v2026.4.100", new Version(2026, 4, 100), null));
        Assert.IsTrue(banner.IsVisible, "Premise: the banner must be showing first.");

        banner.ApplyResult(UpdateCheckResult.NoUpdate());

        Assert.IsFalse(banner.IsVisible, "A NoUpdate result must take the banner down again.");
    }
}
