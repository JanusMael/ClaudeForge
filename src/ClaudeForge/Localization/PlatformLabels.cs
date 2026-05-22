using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Localization;

/// <summary>
/// Runtime-resolved localized labels that depend on the current host
/// platform.  Picks the right resx variant at access time so AXAML's
/// <c>{x:Static}</c> binding still works (the platform doesn't change
/// during a process lifetime, so the value is effectively constant once
/// the app launches — but we don't want different binaries per platform).
/// <para>
/// Use this when a single English term doesn't translate cleanly to all
/// three desktop platforms — most commonly file-manager nomenclature
/// ("Explorer" on Windows, "Finder" on macOS, generic "file manager"
/// on Linux).
/// </para>
/// </summary>
public static class PlatformLabels
{
    /// <summary>
    /// "Reveal in Explorer" / "Reveal in Finder" / "Show in Files" — label
    /// for the button that opens the OS file manager at a path.  Default
    /// (unknown platform) falls back to the Linux phrasing because it
    /// doesn't name a vendor-specific app.
    /// </summary>
    public static string Reveal => PlatformInfo.Current switch
    {
        { IsWindows: true } => Strings.ButtonRevealWindows,
        { IsMacOS: true } => Strings.ButtonRevealMac,
        var _ => Strings.ButtonRevealLinux,
    };

    /// <summary>Tooltip variant of <see cref="Reveal"/>.</summary>
    public static string RevealTooltip => PlatformInfo.Current switch
    {
        { IsWindows: true } => Strings.TipButtonRevealWindows,
        { IsMacOS: true } => Strings.TipButtonRevealMac,
        var _ => Strings.TipButtonRevealLinux,
    };

    /// <summary>
    /// "Open in your text editor — click 'Reveal in Explorer'" hint with
    /// the platform-correct file-manager noun substituted in.
    /// </summary>
    public static string MemoryViewerEditHint => PlatformInfo.Current switch
    {
        { IsWindows: true } => Strings.TextMemoryViewerEditHintWindows,
        { IsMacOS: true } => Strings.TextMemoryViewerEditHintMac,
        var _ => Strings.TextMemoryViewerEditHintLinux,
    };
}