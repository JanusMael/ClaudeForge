using System.Text.RegularExpressions;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.AgentForge.Core.Updates;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Essentials;
using Bennewitz.Ninja.OpenCode.Avalonia.Essentials;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Adapters;
using Bennewitz.Ninja.OpenCodeForge.Localization;
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

    /// <summary>Supplied by MSTest; the Essentials test needs its cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "ocf-upd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", _sandbox);
        Environment.SetEnvironmentVariable("OPENCODE_DISABLE_PROJECT_CONFIG", "1");
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", null);
        Environment.SetEnvironmentVariable("OPENCODE_DISABLE_PROJECT_CONFIG", null);
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

    /// <summary>
    /// Saving the window's geometry on close must not disturb the preferences stored beside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ <b>This is a real defect that shipped, and it defeated both new preferences at once.</b>
    /// The window's <c>Closing</c> handler built a FRESH three-argument
    /// <see cref="WindowState"/> from the geometry it had — so the two update fields fell back to
    /// their constructor defaults on every close: the opt-out silently re-enabled itself, and the
    /// dismissed-tag list emptied, which made the banner return on every launch no matter how
    /// often it was dismissed.
    /// </para>
    /// <para>
    /// ⚠ <b>Nothing else could have caught it.</b> Every other test here drives
    /// <see cref="WindowStateService"/> directly, where the round-trip is honest. The bug lived
    /// entirely in a caller that constructed the record itself — which is why the geometry save
    /// is now a METHOD that reads-then-updates, rather than something each caller assembles.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void SavingGeometryPreservesTheUpdatePreferences()
    {
        WindowStateService.Save(WindowState.Default with
        {
            CheckForUpdatesOnLaunch = false,
            DismissedUpdateVersions = ["opencodeforge-v2026.4.100"],
        });

        // What the window does when it closes.
        WindowStateService.SaveGeometry(width: 1024, height: 768, isMaximized: true);

        WindowState reloaded = WindowStateService.Load();

        Assert.AreEqual(1024, reloaded.Width, "Premise: the new geometry must actually be saved.");
        Assert.IsTrue(reloaded.IsMaximized, "Premise: the maximized flag must be saved.");

        Assert.IsFalse(
            reloaded.CheckForUpdatesOnLaunch,
            "Closing the window re-enabled the update check. The opt-out would survive only "
            + "until the user quit.");
        CollectionAssert.AreEqual(
            new[] { "opencodeforge-v2026.4.100" },
            reloaded.Dismissed.ToArray(),
            "Closing the window cleared the dismissed-tag list, so the banner returns on every "
            + "launch however many times it is dismissed.");
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

    // ── The disposal contract ───────────────────────────────────────────────

    /// <summary>
    /// The view-model is disposable, and the window disposes it on close.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ <b>This is the whole reason the periodic re-check could not just be bolted on.</b> That
    /// loop is a detached task whose only stop signal is a token the view-model owns. If
    /// <c>MainWindow</c> stops calling <see cref="IDisposable.Dispose"/> — a one-line deletion —
    /// the task outlives its window, keeps waking every four hours, and keeps marshalling to a
    /// dispatcher for a window that is gone. Nothing else in this suite would notice: the app
    /// still builds, still launches, still passes every other test.
    /// </para>
    /// <para>
    /// ⚠ <b>The second half is a SOURCE SCAN, deliberately.</b> This project cannot instantiate
    /// its own views headlessly — the shared harness is stripped of the application resource
    /// dictionaries — so there is no way to close a real window and observe the effect. Reading
    /// the call out of the view's source is the available evidence, and it is the evidence that
    /// matters, because the failure mode is the call being absent.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheViewModelIsDisposableAndTheWindowDisposesIt()
    {
        Assert.IsTrue(
            typeof(ViewModels.MainWindowViewModel).IsAssignableTo(typeof(IDisposable)),
            "MainWindowViewModel is no longer IDisposable, so the periodic update re-check has "
            + "nothing to stop it.");

        string source = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "OpenCodeForge", "Views", "MainWindow.axaml.cs"));

        Assert.IsTrue(
            source.Contains("vm.Dispose()", StringComparison.Ordinal),
            "MainWindow.axaml.cs no longer disposes its view-model. The background update "
            + "re-check would keep running after the window closed.");

        Assert.IsTrue(
            source.Contains("Closing +=", StringComparison.Ordinal),
            "MainWindow.axaml.cs has no Closing handler, so nothing runs at shutdown at all.");
    }

    /// <summary>Disposing twice is safe — some shutdown paths raise Closing more than once.</summary>
    [TestMethod]
    public void DisposeIsIdempotent()
    {
        ViewModels.MainWindowViewModel vm = new(
            new HostedSection(OpenCodeProducts.Config, new OpenCodeClient(),
                OpenCodePageLayout.Config, () => Strings.SectionOpenCode));

        // The claim is "does not throw", and MSTest has no Assert.DoesNotThrow — so the throw is
        // caught and turned into an explicit failure rather than left to surface as an error.
        try
        {
            vm.Dispose();
            vm.Dispose();
        }
        catch (Exception ex)
        {
            Assert.Fail(
                "Dispose must tolerate being called more than once — some shutdown paths raise "
                + $"Closing twice, and the second call would then take the app down: {ex}");
        }
    }

    /// <summary>
    /// The geometry save the window performs on close does not disturb the update preferences —
    /// asserted here as well as in <see cref="SavingGeometryPreservesTheUpdatePreferences"/>
    /// because the window's handler is the caller that got it wrong.
    /// </summary>
    [TestMethod]
    public void TheWindowSavesGeometryByNameRatherThanRebuildingTheRecord()
    {
        string path = Path.Combine(
            FindRepoRoot(), "src", "OpenCodeForge", "Views", "MainWindow.axaml.cs");

        // ⚠ Comment lines are stripped before the negative check. The comment explaining WHY
        // this must not happen necessarily quotes the shape it is warning about, so scanning the
        // raw text made the file fail its own guard. Full-line comments only — enough here, and
        // it does not pretend to parse C#.
        string code = string.Join(
            '\n',
            File.ReadAllLines(path)
                .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.IsTrue(
            code.Contains("WindowStateService.SaveGeometry(", StringComparison.Ordinal),
            "MainWindow no longer saves geometry through SaveGeometry.");

        Assert.IsFalse(
            code.Contains("new SavedWindowState(", StringComparison.Ordinal),
            "MainWindow constructs a SavedWindowState again. Building the record from geometry "
            + "alone drops every other field to its default — which silently re-enabled the "
            + "update check and emptied the dismissed-banner list on every close.");
    }

    /// <summary>
    /// The app actually puts the update opt-out on the Essentials page, and it round-trips the
    /// real persisted preference.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The card mechanism working proves nothing on its own</b> — that is tested in
    /// OpenCode.Avalonia, against a fake preference. What this asserts is that the app SUPPLIES
    /// one: delete the <c>appPreferences</c> argument and the mechanism tests all still pass while
    /// the setting disappears from the UI entirely.
    /// </remarks>
    [TestMethod]
    public async Task TheUpdateOptOutIsOnTheEssentialsPageAndRoundTrips()
    {
        ViewModels.MainWindowViewModel vm = new(
            new HostedSection(OpenCodeProducts.Config, new OpenCodeClient(),
                OpenCodePageLayout.Config, () => Strings.SectionOpenCode));

        await vm.InitializeAsync(TestContext.CancellationTokenSource.Token);

        object? editor = vm.Navigation
            .FirstOrDefault(n => n.NodeId == ViewModels.MainWindowViewModel.EssentialsNodeId)?
            .Editor;

        OpenCodeEssentialsViewModel? essentials = editor as OpenCodeEssentialsViewModel;
        Assert.IsNotNull(essentials, "The Essentials node is not an OpenCodeEssentialsViewModel.");

        EssentialsCardViewModel? card = essentials.GetCardById(
            ViewModels.MainWindowViewModel.EssentialsCheckForUpdatesCardId);

        Assert.IsNotNull(
            card,
            "The Essentials page has no update opt-out card, so the preference exists with no way "
            + "to change it.");

        Assert.AreEqual(
            true,
            card.BoolValue,
            "Premise: a fresh state file defaults the check to ON, and the card must show that.");

        // Toggle it off through the card, exactly as the user would.
        card.BoolValue = false;
        await card.WriteAsync();

        Assert.IsFalse(
            WindowStateService.Load().CheckForUpdatesOnLaunch,
            "Toggling the card did not persist. The setting would revert the moment the page was "
            + "re-read.");

        vm.Dispose();
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
