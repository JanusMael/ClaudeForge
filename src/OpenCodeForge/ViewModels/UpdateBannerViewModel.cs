using Bennewitz.Ninja.AgentForge.Core.Updates;
using Bennewitz.Ninja.OpenCodeForge.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Bennewitz.Ninja.OpenCodeForge.ViewModels;

/// <summary>
/// Backs the "Update available" banner: decides whether to surface itself given an
/// <see cref="UpdateCheckResult"/> and the persisted dismissed-tag list, and persists a dismiss.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dismiss is PER VERSION, not forever.</b> Dismissing writes the release's tag to
/// <see cref="WindowState.DismissedUpdateVersions"/>. Later launches still run the check and
/// still get that tag back, but the banner stays quiet — until a tag that is not in the list
/// arrives, at which point it fires again and can be dismissed separately. A permanent "never
/// tell me" is the Essentials-style opt-out in the About dialog, which is a different control
/// with a different meaning.
/// </para>
/// <para>
/// ⚠ <b>Tag comparison is byte-exact.</b> GitHub tags are case-sensitive identifiers and this
/// app never canonicalises them, so <c>opencodeforge-v1.2.3</c> and <c>OpenCodeForge-V1.2.3</c>
/// are different releases as far as the dismiss list is concerned. That is the conservative
/// direction: the failure mode is showing a banner twice, not suppressing a real one.
/// </para>
/// <para>
/// ⓘ <b>Parallel to ClaudeForge's rather than shared with it</b>, for the same reason the
/// schema-check summariser is: it binds to this app's resx and reads this app's
/// <see cref="WindowStateService"/>, both of which are per-product by design. The part that is
/// genuinely shared — deciding whether a release is newer, and the check plumbing — lives in
/// <see cref="AppUpdateCoordinator"/>.
/// </para>
/// </remarks>
public partial class UpdateBannerViewModel : ObservableObject
{
    /// <summary>Whether the banner is showing. False until <see cref="ApplyResult"/> says so.</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>The available release's tag, e.g. <c>opencodeforge-v2026.4.100</c>.</summary>
    [ObservableProperty]
    private string? _latestTagName;

    /// <summary>That release's page, when the check supplied one.</summary>
    [ObservableProperty]
    private string? _releaseUrl;

    /// <summary>Whether there is a release page worth offering a link to.</summary>
    public bool HasReleaseUrl => !string.IsNullOrWhiteSpace(ReleaseUrl);

    /// <summary>Raised after a dismiss, so the host can stop re-checking this session.</summary>
    public event EventHandler? Dismissed;

    partial void OnReleaseUrlChanged(string? value) => OnPropertyChanged(nameof(HasReleaseUrl));

    /// <summary>
    /// Show or hide the banner for <paramref name="result"/>.
    /// </summary>
    /// <remarks>
    /// Re-reads the dismissed list on every call rather than caching it, so a dismiss performed
    /// by another instance of the app is honoured. Safe to call more than once.
    /// </remarks>
    public void ApplyResult(UpdateCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.IsUpdateAvailable || string.IsNullOrWhiteSpace(result.LatestTagName))
        {
            IsVisible = false;
            return;
        }

        WindowState state = WindowStateService.Load();
        if (state.Dismissed.Contains(result.LatestTagName, StringComparer.Ordinal))
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

    /// <summary>Open the release page for the version the banner is announcing.</summary>
    /// <remarks>
    /// ⚠ On this view-model rather than the window's, so the banner's XAML can bind
    /// <c>{Binding OpenReleaseCommand}</c> against its own DataContext. Reaching the host with
    /// an ancestor binding would resolve by reflection and is what the repository bans
    /// <c>$parent[...]</c> for — it trips IL2026 under <c>PublishTrimmed</c>.
    /// </remarks>
    [RelayCommand]
    private void OpenRelease()
    {
        if (!string.IsNullOrWhiteSpace(ReleaseUrl))
        {
            UrlOpener.Open(ReleaseUrl);
        }
    }

    /// <summary>
    /// Record the current tag as dismissed and hide the banner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Persists immediately, so a crash between the click and the next launch still honours the
    /// choice. Idempotent — a tag already in the list is not added twice.
    /// </para>
    /// <para>
    /// ⚠ Builds a NEW list and saves a <c>with</c>-updated record rather than mutating the one
    /// it loaded: <see cref="WindowState"/> is a record whose collection is exposed as
    /// <see cref="IReadOnlyList{T}"/>, so there is nothing to mutate — which is the point. The
    /// sibling app's equivalent mutates a live list, and this shape is the one to copy.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private void Dismiss()
    {
        if (string.IsNullOrWhiteSpace(LatestTagName))
        {
            // Defensive: the banner is hidden without a tag, but the command can be reached by
            // a key binding or a programmatic call that never consulted IsVisible.
            IsVisible = false;
            return;
        }

        string tag = LatestTagName;
        WindowState state = WindowStateService.Load();

        if (!state.Dismissed.Contains(tag, StringComparer.Ordinal))
        {
            WindowStateService.Save(state with
            {
                DismissedUpdateVersions = [.. state.Dismissed, tag],
            });
            Log.Information("[UpdateCheck] User dismissed the update banner for {Tag}; persisted.", tag);
        }

        IsVisible = false;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}
