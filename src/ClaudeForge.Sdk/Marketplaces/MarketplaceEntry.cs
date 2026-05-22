using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Marketplaces;

/// <summary>
/// A marketplace registration. <see cref="SourceValue"/>'s meaning is
/// determined by <see cref="SourceKind"/> — for <see cref="MarketplaceSourceKind.Url"/>
/// or <see cref="MarketplaceSourceKind.Git"/> it is the URL; for
/// <see cref="MarketplaceSourceKind.Github"/> it is the <c>owner/repo</c>
/// reference; for <see cref="MarketplaceSourceKind.Npm"/> it is the package
/// name; and for the local-file / local-directory kinds it is the path.
/// </summary>
public sealed record MarketplaceEntry(
    string Name,
    MarketplaceSourceKind SourceKind,
    string SourceValue)
{
    /// <summary>
    /// Outer-level marketplace fields the SDK does not currently model
    /// (e.g. <c>description</c>). Captured verbatim during read and
    /// re-emitted unchanged during write so round-trips do not silently
    /// drop user data. Mirrors <c>McpServer.PreservedFields</c>.
    /// </summary>
    /// <remarks>
    /// Marked <c>internal</c> to keep <see cref="JsonObject"/> out of the
    /// public SDK surface (locked by <c>NoPublicApi_LeaksSystemTextJsonNodes</c>).
    /// </remarks>
    internal JsonObject? PreservedFields { get; init; }

    /// <summary>
    /// Inner <c>source</c>-object fields the SDK does not currently model
    /// (e.g. branch, ref, commit-sha when a future schema adds them).
    /// </summary>
    internal JsonObject? PreservedSourceFields { get; init; }
}