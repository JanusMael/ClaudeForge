using System.Globalization;

namespace Bennewitz.Ninja.ClaudeForge.Services;

/// <summary>
/// Manages the application culture used by the .resx resource manager and by
/// Semi.Avalonia's locale bundle loader.
///
/// Call <see cref="ApplyCulture"/> as the very first line of <c>Program.Main()</c>
/// (before <c>BuildAvaloniaApp()</c>) and again in
/// <c>App.OnFrameworkInitializationCompleted()</c> as belt-and-suspenders, because
/// Semi.Avalonia lazy-loads its locale bundle on first control creation and reads
/// <c>CultureInfo.CurrentUICulture</c> at that moment.
/// </summary>
public static class LocalizationService
{
    /// <summary>
    /// Resolves and applies the application culture to all four culture slots on the
    /// current thread and as the process-wide default for future threads.
    ///
    /// Resolution order:
    ///   1. <paramref name="cultureName"/> if non-null (explicit override, e.g. from CLI)
    ///   2. OS <c>CultureInfo.CurrentUICulture</c>
    ///   3. "en-US" fallback if the resolved name is invalid
    /// </summary>
    /// <remarks>
    /// A persisted-language-preference slot was previously stubbed in via
    /// <c>LoadStoredCulture()</c> returning <c>null</c>; the helper has been removed
    /// because nothing called it. When a language-selector UI is added it should
    /// load the stored preference and slot it between (1) and (2) here.
    /// </remarks>
    public static void ApplyCulture(string? cultureName = null)
    {
        // Step 1: explicit arg → OS default
        cultureName ??= CultureInfo.CurrentUICulture.Name;

        // Step 3: validate — if Semi.Avalonia (or .NET) doesn't know this culture,
        // fall back gracefully to en-US rather than crashing.
        CultureInfo culture = TryCreate(cultureName) ?? new CultureInfo("en-US");

        // Apply to ALL slots so that Semi.Avalonia's lazy-load and the .resx
        // ResourceManager both see the same culture regardless of which thread
        // reads them first.
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    /// <summary>Returns the name of the currently active UI culture.</summary>
    public static string CurrentLanguage =>
        CultureInfo.CurrentUICulture.Name;

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static CultureInfo? TryCreate(string name)
    {
        try
        {
            return new CultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}