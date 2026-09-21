using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.ClaudeForge.Localization;

namespace Bennewitz.Ninja.ClaudeForge.Adapters;

/// <summary>
/// Supplies this app's words to the neutral settings group editor.
/// </summary>
/// <remarks>
/// The keys stay in <c>src/ClaudeForge/Localization</c>, which is the directory the
/// localization parity tests walk to. That is the whole point of passing text as data: a
/// <c>.resx</c> in any other directory is invisible to those four contracts, so moving these
/// keys to sit beside the neutral code would silently un-translate them.
/// </remarks>
public static class ClaudeSettingsGroupText
{
    /// <summary>
    /// Build the text bundle. A method rather than a cached static: the resource lookups follow
    /// the current UI culture, and the language can change while the app is running.
    /// </summary>
    public static SettingsGroupText Create() => new()
    {
        TabProperties = Strings.HeaderTabProperties,
        TabEffective = Strings.HeaderTabEffective,

        // ⚠ These two were hardcoded English inline in the group editor before it became
        // neutral, while the two tabs beside them were resource-backed. They are still English —
        // moving them here did not translate them, it only made the gap visible. Adding
        // HeaderTabJsonAll / HeaderTabJsonActive to Strings.resx (and its nine locale siblings)
        // is the fix; the parity tests will then hold them like every other key.
        TabJsonAll = "JSON (all)",
        TabJsonActive = "JSON (active)",
    };
}
