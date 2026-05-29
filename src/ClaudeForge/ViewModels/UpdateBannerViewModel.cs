using Bennewitz.Ninja.ClaudeForge.Core.Updates;
using Bennewitz.Ninja.ClaudeForge.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// Backing view-model for the "Update available" banner.  Receives an
/// <see cref="UpdateCheckResult"/> from <see cref="AppUpdateService"/>
/// (live or simulated), decides whether to surface itself given the
/// persisted dismissed-versions list, and exposes the Dismiss command
/// + a <see cref="ReleaseUrl"/> the banner XAML binds to its
/// "View release" <c>HyperlinkButton</c>'s <c>NavigateUri</c>.
///
/// <para>
/// <b>Per-version dismiss contract:</b> dismissing adds the raw GitHub
/// tag string to <see cref="WindowState.DismissedUpdateVersions"/> on
/// disk.  Subsequent launches still RUN the check and still get the
/// same tag back — but the banner stays suppressed unless / until a
/// strictly newer release arrives.  When a newer tag does arrive, the
/// older dismiss is irrelevant: the banner fires again, the user can
/// dismiss the new tag separately, etc.
/// </para>
///
/// <para>
/// <b>One-instance-per-MainWindow:</b> this VM is held as a property
/// on <see cref="MainWindowViewModel"/>.  The banner control in
/// <c>MainWindow.axaml</c> binds its DataContext to this VM (not to
/// MainWindowViewModel) so the XAML can bind to
/// <see cref="IsVisible"/> / <see cref="LatestTagName"/> directly
/// without a path-prefix every time.
/// </para>
///
/// <para>
/// <b>"View release" affordance:</b> uses Avalonia's
/// <c>HyperlinkButton</c> with <c>NavigateUri</c>, which handles
/// browser-launch natively across platforms.  No command and no
/// shell-launcher dependency needed in the VM — matches the existing
/// docs-link pattern in <c>InstallBanner.axaml</c>.
/// </para>
/// </summary>
public partial class UpdateBannerViewModel : ObservableObject
{
    /// <summary>
    /// Whether the banner is currently shown.  Bound to
    /// <c>IsVisible="{Binding IsVisible}"</c> on the root Border of the
    /// banner control.  Defaults <see langword="false"/> — the banner
    /// only appears after <see cref="ApplyResult"/> is called with a
    /// genuine update available + not previously dismissed.
    /// </summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>
    /// Raw GitHub tag string for the available release (e.g.
    /// <c>"v2026.5.524"</c>).  Used by the banner's title text
    /// (<c>"Update available: {0}"</c>) and by
    /// <see cref="DismissCommand"/> to persist the dismiss.  Null when
    /// no banner is showing.
    /// </summary>
    [ObservableProperty]
    private string? _latestTagName;

    /// <summary>
    /// URL of the release on GitHub, as returned by the API's
    /// <c>html_url</c> field.  Bound to the "View release"
    /// <c>HyperlinkButton</c>'s <c>NavigateUri</c>.  Null is tolerated
    /// (defensive — the API has occasionally elided this field on some
    /// release types) and surfaces via
    /// <see cref="HasReleaseUrl"/> for the HyperlinkButton's
    /// <c>IsVisible</c> binding.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReleaseUrl))]
    private string? _releaseUrl;

    /// <summary>
    /// Derived: <see langword="true"/> when <see cref="ReleaseUrl"/>
    /// is non-empty.  The "View release" HyperlinkButton's
    /// <c>IsVisible</c> binds to this so the button hides cleanly
    /// rather than landing on a no-op click when the API didn't supply
    /// a URL.
    /// </summary>
    public bool HasReleaseUrl => !string.IsNullOrWhiteSpace(ReleaseUrl);

    /// <summary>
    /// Decide whether to surface the banner given a fresh result from
    /// <see cref="AppUpdateService.CheckOncePerLaunchAsync"/>.  Reads
    /// the persisted dismiss list from disk every call so the latest
    /// state is honoured (the user could in principle have dismissed
    /// a tag on a sibling launch / instance).
    ///
    /// <para>
    /// Call exactly once per launch from the post-load codepath in
    /// <see cref="MainWindowViewModel"/>.  Subsequent calls within the
    /// same process are gracefully idempotent (the latch in
    /// <see cref="AppUpdateService"/> short-circuits before reaching
    /// us), but we don't rely on that — Apply itself is also safe to
    /// re-run.
    /// </para>
    /// </summary>
    public void ApplyResult(UpdateCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.IsUpdateAvailable || string.IsNullOrWhiteSpace(result.LatestTagName))
        {
            IsVisible = false;
            return;
        }

        // Per-version dismiss check.  Tag comparison is byte-exact —
        // GitHub tags are case-sensitive identifiers and we never
        // canonicalise them.
        WindowState state = WindowStateService.Load();
        if (state.DismissedUpdateVersions.Contains(result.LatestTagName))
        {
            Log.Information(
                "[UpdateCheck] {Tag} is in the dismissed list; banner suppressed.",
                result.LatestTagName);
            IsVisible = false;
            return;
        }

        LatestTagName = result.LatestTagName;
        ReleaseUrl = result.ReleaseUrl;
        IsVisible = true;
    }

    /// <summary>
    /// Add the current <see cref="LatestTagName"/> to the persisted
    /// dismissed-versions list and hide the banner.  Idempotent — if
    /// the tag is somehow already in the list we don't add it twice.
    /// Persists immediately so a crash between dismiss and next-launch
    /// honours the user's choice.
    /// </summary>
    [RelayCommand]
    private void Dismiss()
    {
        if (string.IsNullOrWhiteSpace(LatestTagName))
        {
            // Defensive: should never happen (the banner is hidden
            // when LatestTagName is null) but the command can be
            // wired to a key binding or programmatic call that
            // bypasses IsVisible.
            IsVisible = false;
            return;
        }

        string tag = LatestTagName;
        WindowState state = WindowStateService.Load();
        if (!state.DismissedUpdateVersions.Contains(tag))
        {
            state.DismissedUpdateVersions.Add(tag);
            WindowStateService.Save(state);
            Log.Information(
                "[UpdateCheck] User dismissed update banner for {Tag}; persisted.",
                tag);
        }

        IsVisible = false;
    }
}
