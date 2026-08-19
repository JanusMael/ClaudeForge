using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.OpenCode.Sdk;

/// <summary>
/// The two products this SDK speaks for: OpenCode's main config and its TUI config.
/// </summary>
/// <remarks>
/// <para>
/// Declared here rather than on <c>SchemaRegistry</c>, where Claude's two live. That
/// placement is a documented compromise from Phase 4 — the URLs and file names were already
/// hardcoded throughout <c>AgentForge.Core</c>, so concentrating them there made the
/// eventual split one thing to move instead of five branches to find. There is no such
/// history for OpenCode, so its descriptors start where they belong: in the product's own
/// assembly, leaving the neutral core with no OpenCode vocabulary at all.
/// </para>
/// <para>
/// They are two <i>products</i>, not one product with two files, because they are exactly
/// what <see cref="ProductDescriptor"/> describes: separate schemas, separate config files,
/// zero key overlap between them.
/// </para>
/// </remarks>
public static class OpenCodeProducts
{
    /// <summary>
    /// OpenCode's main configuration — <c>opencode.json</c> / <c>opencode.jsonc</c>.
    /// </summary>
    /// <remarks>
    /// The bundled schema is upstream's with four external <c>models.dev</c> <c>$ref</c>s
    /// stripped; see <c>BundledOpenCodeSchemaTests</c> for why, and for the guard that makes
    /// a refresh which forgets fail the build.
    /// </remarks>
    public static readonly ProductDescriptor Config =
        new("opencode", "OpenCode", "https://opencode.ai/config.json", "opencode-config.json",
            ArchiveFolder: "OpenCode");

    /// <summary>
    /// OpenCode's terminal-UI configuration — <c>tui.json</c>. Theme, 184 keybind actions,
    /// cursor, mouse and scroll behaviour. No key overlap with <see cref="Config"/>.
    /// </summary>
    public static readonly ProductDescriptor Tui =
        new("opencode-tui", "OpenCode TUI", "https://opencode.ai/tui.json", "opencode-tui.json",
            ArchiveFolder: "OpenCodeTui");

    /// <summary>Both products, in the order a host should present them.</summary>
    public static IReadOnlyList<ProductDescriptor> All { get; } = [Config, Tui];
}
