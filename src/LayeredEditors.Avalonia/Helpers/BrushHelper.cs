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
}