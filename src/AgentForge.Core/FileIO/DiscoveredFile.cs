using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Core.FileIO;

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