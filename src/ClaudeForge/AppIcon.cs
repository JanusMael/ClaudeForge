using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;
using SkiaSharp;
using Svg.Skia;

namespace Bennewitz.Ninja.ClaudeForge;

/// <summary>
/// Provides a shared <see cref="WindowIcon"/> and <see cref="Bitmap"/> rendered
/// from the embedded SVG assets.  All surfaces are rendered once on first access
/// and reused across all application windows and dialogs.
/// </summary>
/// <remarks>
/// Two SVG assets are used:
/// <list type="bullet">
///   <item><c>Resources/ClaudeForge.svg</c> — full-detail icon.  Rendered at 256×256
///         for <see cref="Instance"/> (window titlebar icon) and <see cref="BitmapInstance"/>
///         (large in-app uses).  Also used as the fallback for the small size.</item>
///   <item><c>Resources/ClaudeForge-small.svg</c> — simplified icon optimised for small
///         sizes.  Rendered at 16×16 for <see cref="SmallBitmapInstance"/> (popups, compact
///         UI).  If this asset is absent or fails to render, the small slot falls back to
///         `ClaudeForge.svg` rendered at 16×16.</item>
/// </list>
/// <para>
/// <strong>Per-platform window-icon resolution.</strong>
/// </para>
/// <list type="bullet">
///   <item>
///     <strong>Windows.</strong> <see cref="Window.Icon"/> is honoured directly by
///     the WM, dock, and taskbar.  Setting it from this class is sufficient.
///   </item>
///   <item>
///     <strong>macOS.</strong> Avalonia maps <see cref="Window.Icon"/> to the per-
///     window icon used in the title bar's proxy region.  The application-level
///     dock icon comes from the bundled <c>.icns</c> file (out of scope for this
///     class — added at packaging time).
///   </item>
///   <item>
///     <strong>X11.</strong> Avalonia translates <see cref="Window.Icon"/> into an
///     <c>_NET_WM_ICON</c> property on the window, which most X11 WMs read for the
///     titlebar / Alt-Tab / taskbar.  Some lightweight WMs also need a
///     <c>.desktop</c> file (matched via <c>WM_CLASS</c>) for the application-
///     menu icon — but the WINDOW icon itself works without one.
///   </item>
///   <item>
///     <strong>Wayland.</strong> <see cref="Window.Icon"/> is effectively a no-op:
///     the Wayland protocol intentionally does not expose a per-window icon API.
///     The compositor (KWin / Mutter / COSMIC / Sway) instead reads
///     <c>app_id</c> (set by Avalonia from the assembly name → <c>"ClaudeForge"</c>),
///     looks up a matching <c>.desktop</c> file in <c>$XDG_DATA_DIRS/applications/</c>,
///     and uses its <c>Icon=</c> field to find an icon in the system icon theme.
///     Without an installed <c>.desktop</c> file the compositor falls back to a
///     generic placeholder — which is the symptom CachyOS / COSMIC reported on
///     2026-05-07.  See <c>assets/linux/claudeforge.desktop</c> for the install
///     template and <c>docs/LINUX-DESKTOP-INTEGRATION.md</c> for the procedure.
///   </item>
/// </list>
/// </remarks>
internal static class AppIcon
{
    private static readonly Uri LargeUri = new("avares://ClaudeForge/Resources/ClaudeForge.svg");
    private static readonly Uri SmallUri = new("avares://ClaudeForge/Resources/ClaudeForge-small.svg");

    private static WindowIcon? _instance;
    private static WindowIcon? _smallInstance;
    private static Bitmap? _bitmapInstance;
    private static Bitmap? _smallBitmapInstance;
    private static bool _loaded;

    /// <summary>
    /// Returns the application <see cref="WindowIcon"/> rendered at 256×256 from
    /// <c>ClaudeForge.svg</c>.  Returns <c>null</c> if rendering fails (e.g. during
    /// unit-test runs without an Avalonia platform loaded).
    /// </summary>
    public static WindowIcon? Instance
    {
        get
        {
            EnsureLoaded();
            return _instance;
        }
    }

    /// <summary>
    /// Returns a 256×256 <see cref="Bitmap"/> from <c>ClaudeForge.svg</c>, suitable for
    /// use in AXAML <c>&lt;Image&gt;</c> elements.  <c>null</c> if rendering fails.
    /// </summary>
    public static Bitmap? BitmapInstance
    {
        get
        {
            EnsureLoaded();
            return _bitmapInstance;
        }
    }

    /// <summary>
    /// Returns a <see cref="WindowIcon"/> rendered at 64×64 from
    /// <c>ClaudeForge-small.svg</c> (with fallback to <c>ClaudeForge.svg</c>
    /// at 64×64 when the small asset is absent or fails).  Intended for
    /// dialog titlebars, where the OS scales the icon down to ~16-32px
    /// and the simpler small-icon design reads more clearly than the
    /// detailed 256-px master scaled by the same amount.  Returns
    /// <see langword="null"/> only when both renders fail.
    /// <para>
    /// Use this for any <see cref="Window"/> that's a DIALOG (modal popup
    /// from the main window).  Use <see cref="Instance"/> for the main
    /// application window itself, where the larger detailed render is
    /// preferred for taskbar / dock surfaces.
    /// </para>
    /// </summary>
    public static WindowIcon? SmallInstance
    {
        get
        {
            EnsureLoaded();
            return _smallInstance ?? _instance;
        }
    }

    /// <summary>
    /// Returns a 16×16 <see cref="Bitmap"/> from <c>ClaudeForge-small.svg</c> (if present),
    /// otherwise from <c>ClaudeForge.svg</c> rendered at 16×16.  Intended for popup icons,
    /// compact titlebar contexts, and other small-icon slots.  Falls back to
    /// <see cref="BitmapInstance"/> if both small renders fail.  <c>null</c> only when the
    /// large render also fails.
    /// </summary>
    public static Bitmap? SmallBitmapInstance
    {
        get
        {
            EnsureLoaded();
            return _smallBitmapInstance ?? _bitmapInstance;
        }
    }

    // ---------------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------------

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true; // set before TryLoad so re-entrant calls see the flag

        // log the Linux display server identity once on
        // first AppIcon access so user bug reports self-identify whether
        // a "no icon" symptom is X11 (Window.Icon honoured, fixable in
        // this code) or Wayland (Window.Icon ignored by protocol, requires
        // a .desktop file install — see docs/LINUX-DESKTOP-INTEGRATION.md).
        // $XDG_SESSION_TYPE is the systemd-logind canonical answer:
        // "x11" / "wayland" / "tty" / "mir" / "" (unknown).  Mac and
        // Windows don't set it; Linux always does on a graphical login.
        if (OperatingSystem.IsLinux())
        {
            string sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "unset";
            string? waylandHint = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            Log.Information(
                "[AppIcon] Linux session: XDG_SESSION_TYPE={Type} WAYLAND_DISPLAY={Display}; " +
                "Window.Icon is honoured on X11 only — Wayland needs an installed .desktop " +
                "file (see docs/LINUX-DESKTOP-INTEGRATION.md)",
                sessionType,
                string.IsNullOrEmpty(waylandHint) ? "(unset)" : waylandHint);
        }

        // ---- Large SVG (ClaudeForge.svg) → WindowIcon + 256px Bitmap ----
        SKPicture? largePicture = null;
        try
        {
            using Stream stream = AssetLoader.Open(LargeUri);
            using SKSvg svg = SKSvg.CreateFromStream(stream);
            largePicture = svg.Picture;

            if (largePicture is not null)
            {
                SKRect bounds = largePicture.CullRect;
                byte[]? png256 = RenderToPng(largePicture, bounds, targetSize: 256);
                if (png256 is not null)
                {
                    _bitmapInstance = new Bitmap(new MemoryStream(png256));
                    using MemoryStream ms = new(png256);
                    _instance = new WindowIcon(ms);
                    Log.Information("[AppIcon] Loaded {Size}-byte PNG from {Asset}", png256.Length, LargeUri);
                }
                else
                {
                    Log.Warning("[AppIcon] RenderToPng returned null for {Asset}", LargeUri);
                }
            }
            else
            {
                Log.Warning("[AppIcon] SKSvg.Picture was null for {Asset}", LargeUri);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: the icon is purely cosmetic; never let it crash the app.
            // BUT do log the failure so the next iteration knows what's happening.
            // Linux smoke reported "app does not appear to have an
            // icon".  Likely the asset load threw silently (Avalonia.Platform's
            // AssetLoader can fail on Linux if the assembly resources weren't
            // bundled correctly under PublishTrimmed).  This log surfaces it.
            Log.Warning(ex, "[AppIcon] Failed to load large SVG icon");
        }

        // ---- Small SVG (ClaudeForge-small.svg) → 16px Bitmap + 64px WindowIcon ----
        // Two render targets:
        //   - 16×16 Bitmap: inline next to dialog titles, list-row prefix icons, etc.
        //   - 64×64 WindowIcon: dialog titlebar / Alt-Tab.  64 leaves the WM
        //     headroom to scale down to whatever titlebar size it prefers
        //     (typically 16-32 on Windows, ~22 on Linux/macOS) without
        //     aliasing.  Above 64 doesn't help the rendered titlebar size
        //     and inflates the in-memory PNG buffer.
        //
        // lifetime fix — both renders happen INSIDE the SKSvg's
        // using-scope, then we hand back PNG byte arrays whose lifetime
        // is independent of the SKPicture/SKSvg.  An earlier shape
        // returned svg.Picture out of a using-block, which crashed with
        // 0xC0000005 in sk_picture_get_cull_rect when the caller
        // accessed CullRect on a disposed native picture handle.
        (Bitmap? smallBitmap16, WindowIcon? smallIcon64) = TryRenderSmallTargets(largePicture);
        _smallBitmapInstance = smallBitmap16;
        _smallInstance = smallIcon64;
    }

    /// <summary>
    /// Loads <c>ClaudeForge-small.svg</c> (with the already-loaded large
    /// picture as fallback) and renders BOTH small targets — the 16-px
    /// Bitmap and the 64-px WindowIcon — within a single SKSvg lifetime
    /// scope so the underlying SKPicture stays alive across both renders.
    /// Returns <c>(null, null)</c> on any failure path; the caller's
    /// public getters fall back to the corresponding large render
    /// (or null if THAT also failed earlier).
    /// </summary>
    /// <param name="largeFallback">
    /// The already-loaded large SKPicture, used when the small asset
    /// fails to open / parse.  Caller's responsibility to keep alive
    /// for the duration of this call.
    /// </param>
    private static (Bitmap? Bitmap16, WindowIcon? Icon64) TryRenderSmallTargets(SKPicture? largeFallback)
    {
        // Try the dedicated small asset first.  We render INSIDE this
        // method's `using` scope so SKSvg/SKPicture stay valid for both
        // RenderToPng calls below.  PNG byte buffers are independent of
        // the picture lifetime — they're handed off to Bitmap/WindowIcon
        // ctors that copy the pixel data, so the buffer can be freed
        // when the using block exits.
        try
        {
            using Stream stream = AssetLoader.Open(SmallUri);
            using SKSvg svg = SKSvg.CreateFromStream(stream);
            if (svg.Picture is { } picture)
            {
                return RenderTwoTargets(picture);
            }
            // svg loaded but Picture is null — fall through to fallback.
        }
        catch (Exception ex) when (ex is FileNotFoundException
                                       or InvalidOperationException
                                       or IOException)
        {
            // Small asset missing or unreadable — log once, fall through.
            Log.Debug(ex, "[AppIcon] Small SVG load failed; falling back to large render");
        }

        // Fallback: render the large picture at the small target sizes.
        if (largeFallback is null)
        {
            return (null, null);
        }

        try
        {
            return RenderTwoTargets(largeFallback);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppIcon] Large-fallback small render failed");
            return (null, null);
        }
    }

    /// <summary>
    /// Helper: render <paramref name="picture"/> twice, at 16 and 64 px,
    /// returning a Bitmap and a WindowIcon respectively.  Picture lifetime
    /// must extend across the call (the caller's <c>using</c> scope owns
    /// it).  Either target can come back null if its individual
    /// <see cref="RenderToPng"/> fails; the other can still succeed.
    /// </summary>
    private static (Bitmap? Bitmap16, WindowIcon? Icon64) RenderTwoTargets(SKPicture picture)
    {
        SKRect bounds = picture.CullRect;
        byte[]? png16 = RenderToPng(picture, bounds, targetSize: 16);
        byte[]? png64 = RenderToPng(picture, bounds, targetSize: 64);

        Bitmap? bitmap16 = null;
        WindowIcon? icon64 = null;

        if (png16 is not null)
        {
            bitmap16 = new Bitmap(new MemoryStream(png16));
        }

        if (png64 is not null)
        {
            using MemoryStream ms = new(png64);
            icon64 = new WindowIcon(ms);
        }

        return (bitmap16, icon64);
    }

    /// <summary>
    /// Renders <paramref name="picture"/> into a square PNG of
    /// <paramref name="targetSize"/> pixels, preserving aspect ratio with transparent fill.
    /// Returns <c>null</c> on any Skia failure.
    /// </summary>
    private static byte[]? RenderToPng(SKPicture picture, SKRect bounds, int targetSize)
    {
        try
        {
            float scaleX = targetSize / bounds.Width;
            float scaleY = targetSize / bounds.Height;
            float scale = Math.Min(scaleX, scaleY);

            using SKBitmap? skBitmap = picture.ToBitmap(
                background: SKColors.Transparent,
                scaleX: scale,
                scaleY: scale,
                skColorType: SKColorType.Rgba8888,
                skAlphaType: SKAlphaType.Premul,
                skColorSpace: SKColorSpace.CreateSrgb());

            if (skBitmap is null)
            {
                return null;
            }

            using SKImage? image = SKImage.FromBitmap(skBitmap);
            using SKData? data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        catch
        {
            return null;
        }
    }
}