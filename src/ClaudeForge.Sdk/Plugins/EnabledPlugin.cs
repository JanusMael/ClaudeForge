namespace Bennewitz.Ninja.ClaudeForge.Sdk.Plugins;

/// <summary>
/// One plugin registration in the <c>enabledPlugins</c> block.
/// </summary>
/// <param name="PluginRef">
/// Plugin identifier in the form <c>marketplace-name/plugin-name</c>.
/// </param>
/// <param name="Enabled">
/// <see langword="true"/> to enable the plugin; <see langword="false"/> to
/// keep the registration but suppress activation.
/// </param>
public sealed record EnabledPlugin(
    string PluginRef,
    bool Enabled);