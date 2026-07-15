namespace Bennewitz.Ninja.ClaudeForge.Sdk.Plugins;

/// <summary>
/// One plugin registration in the <c>enabledPlugins</c> block.
/// </summary>
/// <param name="PluginRef">
/// Plugin identifier in the form <c>marketplace-name/plugin-name</c>.
/// </param>
/// <param name="Enabled">
/// <see langword="true"/> to enable the plugin; <see langword="false"/> to
/// keep the registration but suppress activation. Always <see langword="true"/>
/// when <see cref="Components"/> is non-null.
/// </param>
/// <param name="Components">
/// The schema also permits an array-of-strings value (enable specific plugin
/// components). When the on-disk value is such an array, this carries its string
/// items and <see cref="Enabled"/> is <see langword="true"/>; <see langword="null"/>
/// for the common plain-boolean form. Round-trips through
/// <see cref="IEnabledPluginsAccessor.Set"/>.
/// </param>
public sealed record EnabledPlugin(
    string PluginRef,
    bool Enabled,
    IReadOnlyList<string>? Components = null);
