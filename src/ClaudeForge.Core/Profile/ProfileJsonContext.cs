using System.Text.Json.Serialization;

namespace Bennewitz.Ninja.ClaudeForge.Core.Profile;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for
/// <see cref="ExportedProfile"/>.  Required because Release builds
/// publish trimmed and reflection-based serialisation silently drops
/// trimmed setters / getters — see
/// <c>BackupJsonContext</c> for the same pattern.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ExportedProfile))]
internal sealed partial class ProfileJsonContext : JsonSerializerContext;