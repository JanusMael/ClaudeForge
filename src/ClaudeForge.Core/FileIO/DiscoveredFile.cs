using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Core.FileIO;

/// <summary>
/// Describes a config file discovered on disk (whether or not it currently exists).
/// </summary>
public sealed record DiscoveredFile(
    ConfigScope Scope,
    ConfigFileType FileType,
    string FilePath,
    bool Exists,
    bool IsReadOnly,
    string? ProfileName = null);