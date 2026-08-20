using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.OpenCodeForge.Localization;

namespace Bennewitz.Ninja.OpenCodeForge.Adapters;

/// <summary>
/// Supplies this app's words to the neutral settings group editor.
/// </summary>
/// <remarks>
/// A method rather than a cached static: the lookups follow the current UI culture, and the
/// language can change while the app is running.
/// </remarks>
public static class OpenCodeSettingsGroupText
{
    /// <summary>Build the text bundle.</summary>
    public static SettingsGroupText Create() => new()
    {
        TabProperties = Strings.HeaderTabProperties,
        TabEffective = Strings.HeaderTabEffective,
        TabJsonAll = Strings.HeaderTabJsonAll,
        TabJsonActive = Strings.HeaderTabJsonActive,
    };
}
