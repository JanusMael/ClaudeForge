// Default English strings for the library-side PropertyEditorWrapper chrome.
//
// The library wrapper is consumed as a fallback by external users of
// LayeredEditors.Avalonia who don't supply their own; ClaudeForge supplies
// its own fully-localised wrapper at src/ClaudeForge/Controls/PropertyEditorWrapper.axaml
// so this default surface rarely renders in the host app — but the strings
// still need to be overridable so the library is genuinely reusable.
//
// Pattern: a static `Resolver` Func returns the localised text for a key.
// The default resolver returns the English literal.  Consumers wire a
// host-specific resolver at app-startup (BEFORE any wrapper XAML loads) to
// pull from their own resx — see ClaudeForge.Program.WireWrapperLocalization.
//
// Why a Func rather than a resx-on-the-library:
//   - Adding a satellite-assembly resx to a library is non-trivial and
//     creates a parallel localisation surface to maintain (App resx +
//     library resx in lockstep) — most strings would just duplicate the
//     App resx keys.  (the library still needs a default string surface).
//   - Until that consolidation lands, this lightweight hook keeps the
//     library wrapper localisable without adding a parallel resx surface.

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Localization;

/// <summary>
/// Chrome-string surface for the library-side
/// <see cref="Controls.PropertyEditorWrapper"/>.  All strings default to
/// English literals; hosts override by assigning <see cref="Resolver"/> at
/// startup before any wrapper XAML is parsed.
/// </summary>
public static class WrapperStrings
{
    /// <summary>
    /// Resolves a wrapper-chrome key to its display text.  Default returns
    /// the English literal; hosts wire to their own resx by assigning a
    /// delegate that maps each key to the host's localised string.
    /// </summary>
    /// <remarks>
    /// Must be assigned BEFORE any <see cref="Controls.PropertyEditorWrapper"/>
    /// XAML is parsed — <c>{x:Static}</c> markup-extension dereferences happen
    /// at parse time and cache the value, so post-load reassignment has no
    /// effect on already-rendered wrappers.
    /// </remarks>
    public static Func<string, string> Resolver { get; set; } = DefaultResolver;

    private static string DefaultResolver(string key)
    {
        return key switch
        {
            nameof(TipResetToInherited) => "Remove this setting at the current scope (inherit from lower scope)",
            nameof(TipReadOnly) => "Read only",
            nameof(TipUndocumented) => "Undocumented setting — not in official Claude documentation",
            nameof(TipShowSuggestions) => "Show suggestions",
            nameof(TipNewSetting) => "New setting — added since your last session",
            nameof(LabelOverridden) => "(overridden)",
            nameof(LabelReset) => "Reset",
            var _ => key,
        };
    }

    /// <summary>Tooltip for the "Reset to inherited value" button.</summary>
    public static string TipResetToInherited => Resolver(nameof(TipResetToInherited));

    /// <summary>Tooltip for the 🔒 read-only / managed-scope lock icon.</summary>
    public static string TipReadOnly => Resolver(nameof(TipReadOnly));

    /// <summary>Tooltip for the 🕵 "not in official Claude docs" indicator.</summary>
    public static string TipUndocumented => Resolver(nameof(TipUndocumented));

    /// <summary>Tooltip for the free-form enum's "▾ show suggestions" chevron.</summary>
    public static string TipShowSuggestions => Resolver(nameof(TipShowSuggestions));

    /// <summary>Tooltip for the ✨ NEW-since-last-session badge.</summary>
    public static string TipNewSetting => Resolver(nameof(TipNewSetting));

    /// <summary>"(overridden)" label shown next to the scope chiclet when ≥2 scopes have data.</summary>
    public static string LabelOverridden => Resolver(nameof(LabelOverridden));

    /// <summary>Label for the Reset-to-inherited button.</summary>
    public static string LabelReset => Resolver(nameof(LabelReset));

    /// <summary>
    /// Restore <see cref="Resolver"/> to the default-English implementation.
    /// Required test-cleanup hook so a per-test resolver override doesn't
    /// bleed into sibling tests — same convention as
    /// <c>DebugFlags.ResetForTesting()</c> documented in CLAUDE.md.
    /// </summary>
    internal static void ResetForTesting()
    {
        Resolver = DefaultResolver;
    }
}