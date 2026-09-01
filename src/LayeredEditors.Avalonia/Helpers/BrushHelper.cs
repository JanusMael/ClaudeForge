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
        if (Application.Current is { } app
            && app.TryGetResource(key, app.ActualThemeVariant, out object? value)
            && value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallbackHex));
    }
}