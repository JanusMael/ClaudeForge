using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Core.Updates;
using Bennewitz.Ninja.ClaudeForge.Services;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="UpdateBannerViewModel"/>'s decision logic.
/// Each test isolates persisted state via
/// <see cref="PlatformPaths.TestUserProfileOverride"/> so the real
/// <c>~/.claude/cache/ClaudeForge-gui-state.json</c> is never touched.
///
/// <para>
/// Three contracts:
/// </para>
/// <list type="number">
///   <item>ApplyResult routes UpdateAvailable → IsVisible=true with
///         tag + URL populated.</item>
///   <item>ApplyResult suppresses the banner when the tag is in the
///         persisted dismissed-versions list (per-version dismiss).</item>
///   <item>Dismiss adds the current tag to the list, persists it,
///         and hides the banner.  Idempotent — second Dismiss with
///         the same tag does NOT duplicate-add.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class UpdateBannerViewModelTests
{
    private string _sandbox = null!;

    [TestInitialize]
    public void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_updatebanner_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        if (Directory.Exists(_sandbox))
        {
            try { Directory.Delete(_sandbox, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = ex; }
        }
    }

    // ── Default state ───────────────────────────────────────────────────

    [TestMethod]
    public void Default_State_HasBannerHidden()
    {
        UpdateBannerViewModel vm = new();

        Assert.IsFalse(vm.IsVisible, "Banner must default to hidden.");
        Assert.IsNull(vm.LatestTagName);
        Assert.IsNull(vm.ReleaseUrl);
        Assert.IsFalse(vm.HasReleaseUrl);
    }

    // ── ApplyResult contracts ───────────────────────────────────────────

    [TestMethod]
    public void ApplyResult_NoUpdate_LeavesBannerHidden()
    {
        UpdateBannerViewModel vm = new();
        vm.ApplyResult(UpdateCheckResult.NoUpdate());

        Assert.IsFalse(vm.IsVisible);
        Assert.IsNull(vm.LatestTagName);
    }

    [TestMethod]
    public void ApplyResult_UpdateAvailable_NotDismissed_SurfacesBanner()
    {
        UpdateBannerViewModel vm = new();
        vm.ApplyResult(UpdateCheckResult.UpdateAvailable(
            "v2.0.0",
            new Version(2, 0, 0),
            "https://github.com/x/y/releases/tag/v2.0.0"));

        Assert.IsTrue(vm.IsVisible,
            "Banner must surface when a tagged update is available and not previously dismissed.");
        Assert.AreEqual("v2.0.0", vm.LatestTagName);
        Assert.AreEqual("https://github.com/x/y/releases/tag/v2.0.0", vm.ReleaseUrl);
        Assert.IsTrue(vm.HasReleaseUrl,
            "HasReleaseUrl must reflect the populated URL — bound to the View's HyperlinkButton IsVisible.");
    }

    [TestMethod]
    public void ApplyResult_UpdateAvailable_NoReleaseUrl_SurfacesBannerWithoutLinkButton()
    {
        UpdateBannerViewModel vm = new();
        vm.ApplyResult(UpdateCheckResult.UpdateAvailable(
            "v2.0.0",
            new Version(2, 0, 0),
            releaseUrl: null));

        Assert.IsTrue(vm.IsVisible,
            "Missing html_url must not block the banner — the user still needs to know about the update.");
        Assert.IsNull(vm.ReleaseUrl);
        Assert.IsFalse(vm.HasReleaseUrl,
            "HasReleaseUrl=false drives the HyperlinkButton's IsVisible binding — button hides cleanly.");
    }

    [TestMethod]
    public void ApplyResult_TagAlreadyDismissed_SuppressesBanner()
    {
        // Seed the persisted dismiss list with the tag we're about to apply.
        WindowState state = new() { DismissedUpdateVersions = ["v2.0.0"] };
        WindowStateService.Save(state);

        UpdateBannerViewModel vm = new();
        vm.ApplyResult(UpdateCheckResult.UpdateAvailable(
            "v2.0.0",
            new Version(2, 0, 0),
            "https://github.com/x/y/releases/tag/v2.0.0"));

        Assert.IsFalse(vm.IsVisible,
            "Banner must stay hidden when the tag is in the persisted dismiss list — " +
            "per-version dismiss contract.");
    }

    [TestMethod]
    public void ApplyResult_NewerTagThanDismissed_StillSurfaces()
    {
        // User dismissed v1.0.0 earlier; v2.0.0 is now available.
        // The dismiss is irrelevant — v2.0.0 should surface.
        WindowState state = new() { DismissedUpdateVersions = ["v1.0.0"] };
        WindowStateService.Save(state);

        UpdateBannerViewModel vm = new();
        vm.ApplyResult(UpdateCheckResult.UpdateAvailable(
            "v2.0.0",
            new Version(2, 0, 0),
            "https://github.com/x/y/releases/tag/v2.0.0"));

        Assert.IsTrue(vm.IsVisible,
            "A NEWER tag than the dismissed one must surface — dismiss is per-version, not 'dismiss forever'.");
        Assert.AreEqual("v2.0.0", vm.LatestTagName);
    }

    [TestMethod]
    public void ApplyResult_EmptyTagName_LeavesBannerHidden()
    {
        // Defensive: an UpdateAvailable result with empty tag (shouldn't
        // happen via the production checker, but the result factory
        // doesn't enforce a non-empty tag) must not crash.
        UpdateBannerViewModel vm = new();
        vm.ApplyResult(new UpdateCheckResult(
            IsUpdateAvailable: true,
            LatestTagName: "",
            LatestVersion: new Version(2, 0, 0),
            ReleaseUrl: "https://x"));

        Assert.IsFalse(vm.IsVisible,
            "Empty tag must not surface a banner — we'd have no value to render in the title.");
    }

    // ── Dismiss contracts ───────────────────────────────────────────────

    [TestMethod]
    public void Dismiss_HidesBannerAndPersistsTag()
    {
        UpdateBannerViewModel vm = new();
        vm.ApplyResult(UpdateCheckResult.UpdateAvailable(
            "v2.0.0",
            new Version(2, 0, 0),
            "https://github.com/x/y/releases/tag/v2.0.0"));
        Assert.IsTrue(vm.IsVisible, "Setup: banner must be visible before dismiss.");

        vm.DismissCommand.Execute(null);

        Assert.IsFalse(vm.IsVisible, "Dismiss must hide the banner.");

        WindowState persisted = WindowStateService.Load();
        CollectionAssert.Contains(persisted.DismissedUpdateVersions, "v2.0.0",
            "Dismissed tag MUST be persisted so the next launch suppresses the banner for this version.");
    }

    [TestMethod]
    public void Dismiss_TagAlreadyInList_DoesNotDuplicate()
    {
        // Seed with the tag already dismissed (could happen via an
        // out-of-band write between sessions).
        WindowState state = new() { DismissedUpdateVersions = ["v2.0.0"] };
        WindowStateService.Save(state);

        UpdateBannerViewModel vm = new();
        vm.ApplyResult(UpdateCheckResult.UpdateAvailable(
            "v2.0.0",
            new Version(2, 0, 0),
            "https://github.com/x/y/releases/tag/v2.0.0"));
        // (banner stays hidden — the seed already dismissed it.  But we
        // can still poke the command — defensive surface.)

        vm.DismissCommand.Execute(null);

        WindowState persisted = WindowStateService.Load();
        Assert.AreEqual(1, persisted.DismissedUpdateVersions.Count(t => t == "v2.0.0"),
            "Dismiss must be idempotent — the tag must appear exactly once in the list.");
    }

    [TestMethod]
    public void Dismiss_WithNullTagName_JustHidesBanner_NoPersistenceChange()
    {
        // Defensive: banner is normally hidden when LatestTagName is
        // null, but the command can still be invoked programmatically.
        UpdateBannerViewModel vm = new() { IsVisible = true, LatestTagName = null };
        WindowStateService.Save(new WindowState());  // baseline: empty list

        vm.DismissCommand.Execute(null);

        Assert.IsFalse(vm.IsVisible);
        WindowState persisted = WindowStateService.Load();
        Assert.AreEqual(0, persisted.DismissedUpdateVersions.Count,
            "Dismissing with no tag must not pollute the persisted list with empty / null entries.");
    }
}
