using Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Settings;

namespace Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Tests.TestSupport;

/// <summary>
/// The settings page's tab labels as a neutral host would supply them: each is its own property
/// name, so a test can tell which slot the page used.
/// </summary>
internal static class TestSettingsGroupText
{
    internal static SettingsGroupText Create() => new()
    {
        TabProperties = nameof(SettingsGroupText.TabProperties),
        TabEffective = nameof(SettingsGroupText.TabEffective),
        TabJsonAll = nameof(SettingsGroupText.TabJsonAll),
        TabJsonActive = nameof(SettingsGroupText.TabJsonActive),
    };
}
