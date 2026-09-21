using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Hooks;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Marketplaces;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Models;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Plugins;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

/// <summary>
/// <see cref="IAgentConfigClient"/> plus the accessors for config surfaces that
/// exist only because Claude Code / Claude Desktop define them. Two concrete
/// implementations: <see cref="ClaudeCodeClient"/> and
/// <see cref="ClaudeDesktopClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// These five accessors were on <see cref="IAgentConfigClient"/> until the SDK was
/// split. Each is Claude-shaped in a way that does not survive translation to
/// another agent tool, so generalizing them would have meant inventing a union
/// type nobody wanted:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <see cref="Hooks"/> — the <c>hooks</c> event/matcher/command surface. Tools
///   whose extension model is code rather than config have no equivalent config key.
///   </description></item>
///   <item><description>
///   <see cref="Marketplaces"/> and <see cref="Plugins"/> — Claude's marketplace
///   registry and its <c>plugin@marketplace</c> reference form.
///   </description></item>
///   <item><description>
///   <see cref="Models"/> — the model ↔ effort-level ↔ auto-mode relationships,
///   including the nearest-analog coercion rule. The relationships are the product
///   knowledge; the ids alone are not.
///   </description></item>
///   <item><description>
///   <see cref="Permissions"/> — Allow/Deny/Ask lists over Claude's rule
///   <i>syntax</i> (<c>Bash(git commit:*)</c>, <c>mcp__server__tool</c>,
///   <c>WebFetch(domain:...)</c>) and its first-match evaluation order.
///   </description></item>
/// </list>
/// <para>
/// All threading, cancellation, and disposal contracts are inherited unchanged —
/// see <see cref="IAgentConfigClient"/>'s class remarks. Accessor mutations
/// serialize on the same workspace lock as everything else.
/// </para>
/// </remarks>
public interface IClaudeConfigClient : IAgentConfigClient
{
    /// <summary>Permissions accessor — Allow/Deny/Ask lists and DefaultMode.</summary>
    IPermissionsAccessor Permissions { get; }

    /// <summary>Hooks accessor — pre/post tool-use hooks.</summary>
    IHooksAccessor Hooks { get; }

    /// <summary>Marketplaces accessor — typed marketplace entries.</summary>
    IMarketplacesAccessor Marketplaces { get; }

    /// <summary>Enabled plugins accessor.</summary>
    IEnabledPluginsAccessor Plugins { get; }

    /// <summary>
    /// Model-catalog accessor — the allowed <c>model</c> / <c>effortLevel</c> /
    /// <c>permissions.defaultMode</c> values and their inter-relationships
    /// (which effort levels a model supports, whether a model supports auto
    /// mode, the nearest-analog coercion rule). Backed by the bundled
    /// <c>model-catalog.json</c>; read-only and Avalonia-free so non-GUI
    /// consumers can use it.
    /// </summary>
    IModelCatalogAccessor Models { get; }
}
