using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Helpers;

/// <summary>
/// Resolves a named brush resource from the application resource dictionary,
/// falling back to a hardcoded hex value when the key is not present.
/// <para>
/// This lets host applications override any <c>LE.*</c> color token defined in
/// <c>EditorColors.axaml</c> without forking the library — supply a resource with
/// the same key before the first render and it will be picked up automatically.
/// </para>
/// </summary>
internal static class BrushHelper
{
    /// <summary>
    /// Returns the brush registered under <paramref name="key"/> in
    /// <see cref="Application.Current"/>'s resources, or a new
    /// <see cref="SolidColorBrush"/> parsed from <paramref name="fallbackHex"/>
    /// when the key is absent or the value is not an <see cref="IBrush"/>.
    /// </summary>
    internal static IBrush Resolve(string key, string fallbackHex)
    {
        if (Application.Current?.TryGetResource(key, null, out object? value) == true
            && value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallbackHex));
    }

    /// <summary>
    /// Variant-aware counterpart of <see cref="Resolve"/>, for a key declared inside
    /// <c>ResourceDictionary.ThemeDictionaries</c> rather than flat.
    /// </summary>
    /// <remarks>
    /// ⛔⛔ <b><see cref="Resolve"/> passes a null theme variant, and that silently loses a
    /// themed key.</b> The <c>LE.*</c> tokens are declared flat — one value, documented as
    /// theme-neutral — so a null variant finds them. Every <c>App*Brush</c> token is declared
    /// per variant under <c>ThemeDictionaries</c>, and looking one up with a null variant
    /// returns nothing: the caller then takes the fallback hex, which is a single literal and
    /// therefore exactly the hardcoded-colour problem the token was introduced to remove.
    /// The failure is invisible — a plausible colour appears and no warning is logged — so the
    /// two lookups are kept as separate methods rather than one with an optional parameter
    /// somebody can forget to pass.
    /// </remarks>
    internal static IBrush ResolveThemed(string key, string fallbackHex)
    {
        if (Application.Current is not { } app)
        {
            return new SolidColorBrush(Color.Parse(fallbackHex));
        }

        EnsureHooked(app);

        if (LiveBrushes.TryGetValue(key, out SolidColorBrush? live))
        {
            // ⚠ Re-read on every resolve, not only on variant change. The cache exists to keep
            // the brush INSTANCE stable so already-rendered elements can be re-coloured; it is
            // not a colour cache. Caching the colour too made the value stale whenever the
            // resource dictionaries changed without the variant changing - which is not a
            // production path, but it broke AppSeverityThemedLookupTests in the full suite while
            // passing in isolation. An order-dependent failure is a warning worth heeding.
            live.Color = ColorFor(app, key, Fallbacks[key]);
            return live;
        }

        // A non-solid resource (gradient, image) cannot be re-coloured in place, so it is handed
        // back as the resource itself and behaves exactly as before. No such key exists today.
        if (app.TryGetResource(key, app.ActualThemeVariant, out object? value)
            && value is IBrush and not ISolidColorBrush)
        {
            return (IBrush)value;
        }

        SolidColorBrush tracked = new(ColorFor(app, key, fallbackHex));
        LiveBrushes[key] = tracked;
        Fallbacks[key] = fallbackHex;
        return tracked;
    }

    /// <summary>
    /// A translucent version of a themed brush: the same colour at <paramref name="alpha"/>, for
    /// use as a tint behind content.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>Derived rather than declared, deliberately.</b> A tint per severity per theme per app
    /// would be eight new palette entries, and this palette's own comments record the rule that no
    /// unreviewed colour should enter it. Compositing the existing, already-vetted colour over
    /// whatever surface is behind produces the right tint for every severity and both themes with
    /// no new colour at all.
    /// </para>
    /// <para>
    /// ⛔ <b>Tracked like <see cref="ResolveThemed"/>, and for exactly the same reason.</b> A
    /// snapshot brush was a real, user-visible bug — see the theme-tracking note below. A tint
    /// minted fresh per Convert would reintroduce it in a form that is harder to spot, because a
    /// stale 10% wash reads as a slightly-off background rather than as a wrong colour.
    /// </para>
    /// <para>
    /// ⚠ <b>Alpha is bounded above by the BORDER that sits on the tint</b>, not by taste. The
    /// banner draws its border in the same severity colour the tint is made from, so raising alpha
    /// pulls the two together: measured on white, Caution's border against its own tint is 3.23:1
    /// at 8%, 3.15:1 at 10%, 3.07:1 at 12% and <b>2.96:1 at 15%</b> — under the 3.0:1 floor.
    /// <c>SeverityTintStaysLegibleTests</c> holds that ceiling.
    /// </para>
    /// </remarks>
    internal static IBrush ResolveThemedTint(string key, string fallbackHex, double alpha)
    {
        if (Application.Current is not { } app)
        {
            return new SolidColorBrush(WithAlpha(Color.Parse(fallbackHex), alpha));
        }

        EnsureHooked(app);

        string cacheKey = string.Create(
            CultureInfo.InvariantCulture, $"{key}@{alpha:F3}");

        if (LiveTints.TryGetValue(cacheKey, out TrackedTint? live))
        {
            live.Brush.Color = WithAlpha(ColorFor(app, live.ResourceKey, live.FallbackHex), alpha);
            return live.Brush;
        }

        SolidColorBrush tracked = new(WithAlpha(ColorFor(app, key, fallbackHex), alpha));
        LiveTints[cacheKey] = new TrackedTint(tracked, key, fallbackHex, alpha);
        return tracked;
    }

    /// <summary>The colour with its alpha replaced. RGB is untouched, so the hue cannot drift.</summary>
    internal static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0d, 1d) * 255), color.R, color.G, color.B);

    // ── Theme tracking ───────────────────────────────────────────────────────
    //
    // ⛔⛔ THE BRUSH USED TO BE A SNAPSHOT, AND THAT WAS A REAL, USER-VISIBLE BUG.
    // This method reads ActualThemeVariant at CONVERT time, and an IValueConverter only re-runs
    // when its binding SOURCE changes. A severity does not change because the theme did, so every
    // element kept whichever variant's brush was current when it was last materialised: rows
    // rebuilt by navigation or virtualised scrolling picked up the new palette, rows that were not
    // kept the old one, and one screen showed both at once. Reported 2026-09-14 as "glyphs are
    // brighter on reopen, dark after switching to light, and sometimes light then later dark
    // inside the same theme".
    //
    // ⭐ The fix is to return ONE shared, mutable brush per key and re-colour it when the variant
    // changes. SolidColorBrush.Color is a change-notifying Avalonia property, so assigning it
    // re-renders every element already using that brush — no binding re-evaluation, no markup
    // change, and it covers every present and future caller of ResolveThemed rather than the 17
    // binding sites that exist today.
    //
    // ⚠ The brushes are therefore SHARED AND MUTABLE. Do not mutate one from a view; treat what
    // this returns as read-only. Confined to the UI thread, which is where both Convert and the
    // theme-change notification run.
    private static readonly Dictionary<string, SolidColorBrush> LiveBrushes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Fallbacks = new(StringComparer.Ordinal);
    private static bool _hooked;

    /// <summary>
    /// Tints are tracked separately rather than folded into <see cref="LiveBrushes"/>: one key can
    /// be wanted at several alphas, and the refresh has to know the RESOURCE key, which is no
    /// longer the cache key once alpha is part of it. Kept additive so the opaque path — the one
    /// carrying the fix described above — is not restructured to accommodate this.
    /// </summary>
    private sealed record TrackedTint(
        SolidColorBrush Brush, string ResourceKey, string FallbackHex, double Alpha);

    private static readonly Dictionary<string, TrackedTint> LiveTints = new(StringComparer.Ordinal);

    private static void EnsureHooked(Application app)
    {
        if (_hooked)
        {
            return;
        }

        _hooked = true;
        app.ActualThemeVariantChanged += (_, _) => RefreshTrackedBrushes();
    }

    private static Color ColorFor(Application app, string key, string fallbackHex)
    {
        return app.TryGetResource(key, app.ActualThemeVariant, out object? value)
               && value is ISolidColorBrush solid
            ? solid.Color
            : Color.Parse(fallbackHex);
    }

    private static void RefreshTrackedBrushes()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        foreach (KeyValuePair<string, SolidColorBrush> pair in LiveBrushes)
        {
            pair.Value.Color = ColorFor(app, pair.Key, Fallbacks[pair.Key]);
        }

        foreach (TrackedTint tint in LiveTints.Values)
        {
            tint.Brush.Color = WithAlpha(
                ColorFor(app, tint.ResourceKey, tint.FallbackHex), tint.Alpha);
        }
    }

    /// <summary>Drops the tracked-brush cache. Tests only — see the remarks above.</summary>
    internal static void ResetForTesting()
    {
        LiveBrushes.Clear();
        Fallbacks.Clear();
        LiveTints.Clear();
        _hooked = false;
    }
}