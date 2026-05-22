namespace Bennewitz.Ninja.ClaudeForge.Sdk.Marketplaces;

/// <summary>
/// Discriminator for <see cref="MarketplaceEntry.SourceValue"/>'s
/// interpretation.
/// </summary>
public enum MarketplaceSourceKind
{
    /// <summary>Direct HTTP/HTTPS URL.</summary>
    Url,

    /// <summary>Git repository URL.</summary>
    Git,

    /// <summary>GitHub repository in <c>owner/repo</c> form.</summary>
    Github,

    /// <summary>npm package name.</summary>
    Npm,

    /// <summary>Path to a single local file.</summary>
    LocalFile,

    /// <summary>Path to a local directory.</summary>
    LocalDirectory,
}