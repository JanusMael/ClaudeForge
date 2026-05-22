using System.Text.Json.Serialization;

namespace Bennewitz.Ninja.ClaudeForge.Services;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for types serialised in the App layer.
/// Using source generation instead of reflection-based serialization ensures compatibility
/// with <c>PublishTrimmed=true</c> (Release builds).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WindowState))]
internal sealed partial class AppJsonContext : JsonSerializerContext;