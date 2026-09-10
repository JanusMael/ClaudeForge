using Avalonia.Controls;
using Avalonia.Platform;
using Serilog;

namespace Bennewitz.Ninja.OpenCodeForge;

/// <summary>
/// The shared <see cref="WindowIcon"/> for this app's windows, loaded once from the embedded
/// PNG.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>Deliberately not the sibling app's design.</b> ClaudeForge's <c>AppIcon</c> renders two
/// SVGs through <c>SKSvg</c> at first access, which buys it resolution independence at the cost
/// of a <c>Svg.Skia</c> + <c>Svg.Custom</c> + <c>Svg.Controls.Skia.Avalonia</c> package closure
/// and the <c>TrimmerRootAssembly</c> entries those packages need — because they resolve types
/// by string and a trimmed publish would otherwise break them at runtime. This app renders no
/// SVG anywhere else, so paying that for one window icon would be the most expensive bitmap in
/// the repository. A PNG rendered ahead of time needs no rasteriser.
/// </para>
/// <para>
/// ⚠ <b>The artwork is a placeholder.</b> See <c>Resources/OpenCodeForge.svg</c> for what has to
/// be regenerated when it is replaced — the PNG this class loads and the <c>.ico</c> the apphost
/// embeds are both built from it by hand, and nothing rebuilds them automatically.
/// </para>
/// <para>
/// ⓘ <b>On Wayland this is close to a no-op</b>, and that is not a defect here. The protocol
/// exposes no per-window icon API: compositors read the surface's <c>app_id</c> (Avalonia derives
/// it from the assembly name, so <c>OpenCodeForge</c>), find the matching <c>.desktop</c> entry,
/// and resolve its <c>Icon=</c> against the icon theme. That path is what
/// <c>assets/linux/linux-setup.sh</c> installs. X11, Windows and macOS all honour this icon.
/// </para>
/// </remarks>
internal static class AppIcon
{
    private static readonly Uri IconUri = new("avares://OpenCodeForge/Resources/OpenCodeForge.png");

    private static WindowIcon? _instance;
    private static bool _loaded;

    /// <summary>
    /// The application window icon, or <see langword="null"/> when the asset could not be read.
    /// </summary>
    /// <remarks>
    /// Loaded once and reused. <see langword="null"/> is a supported outcome rather than a
    /// throw: a window with no icon is a cosmetic loss, and failing startup over one would turn
    /// a missing resource into an app that does not run.
    /// </remarks>
    internal static WindowIcon? Instance
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                _instance = Load();
            }

            return _instance;
        }
    }

    private static WindowIcon? Load()
    {
        try
        {
            using Stream stream = AssetLoader.Open(IconUri);
            return new WindowIcon(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException or IOException)
        {
            // Narrow, and each one is a real way this fails: the AvaloniaResource entry was
            // dropped from the csproj (FileNotFound), the bytes are not a decodable image
            // (Argument), or the read itself failed (IO). Anything else is not something a
            // caller can act on, so it is left to propagate.
            Log.Warning(ex, "Could not load the window icon from {Uri}", IconUri);
            return null;
        }
    }

    /// <summary>Reset the cache so a test can observe a fresh load.</summary>
    internal static void ResetForTesting()
    {
        _loaded = false;
        _instance = null;
    }
}
