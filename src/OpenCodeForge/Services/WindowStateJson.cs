using System.Text.Json.Serialization;

namespace Bennewitz.Ninja.OpenCodeForge.Services;

/// <summary>
/// Source-generated serializer for <see cref="WindowState"/>.
/// </summary>
/// <remarks>
/// ⚠ Source generation rather than reflection deliberately. Reflection-based
/// <c>JsonSerializer</c> calls produce <c>IL2026</c>, which turns into <c>NETSDK1144</c> and breaks
/// the trimmed Release publish — a failure the Debug test suite cannot see. That exact mistake
/// broke this repo's Release build for three phases.
/// </remarks>
[JsonSerializable(typeof(WindowState))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class WindowStateJson : JsonSerializerContext;
