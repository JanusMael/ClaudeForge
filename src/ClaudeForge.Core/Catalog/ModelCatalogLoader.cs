using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Core.Catalog;

/// <summary>
/// Loads the bundled model catalog. Layering mirrors the bundled-schema path in
/// <see cref="SchemaRegistry"/> minus the live-upstream tiers: memory cache →
/// bundled base + <c>.overlay.json</c> (RFC 7396) → fail-open empty catalog. The
/// catalog has no network source (it ships with the build), so there is no disk
/// cache or HTTPS fetch.
/// </summary>
public static class ModelCatalogLoader
{
    private const string CatalogFile = "model-catalog.json";
    private const string OverlayFile = "model-catalog.overlay.json";
    private const string SubNamespace = "ModelCatalog";

    private static readonly object Gate = new();
    // volatile for the lock-free fast-path read in Load(); matches the sibling
    // memoized caches in this assembly (PlatformPaths, ClaudeDesktopVersionProbe).
    private static volatile ModelCatalog? _cached;

    /// <summary>
    /// Returns the merged catalog, memoized after first success. Never throws —
    /// a missing or malformed resource yields <see cref="ModelCatalog.Empty"/>.
    /// </summary>
    public static ModelCatalog Load()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        lock (Gate)
        {
            return _cached ??= LoadUncached();
        }
    }

    /// <summary>Test seam: parse a catalog straight from JSON text (bypasses the bundled resource + cache).</summary>
    internal static ModelCatalog Parse(string json)
    {
        ModelCatalogDto? dto = JsonSerializer.Deserialize(json, CoreJsonContext.Default.ModelCatalogDto);
        return dto is null ? ModelCatalog.Empty : ModelCatalog.FromDto(dto);
    }

    private static ModelCatalog LoadUncached()
    {
        byte[]? bytes = TryReadMerged();
        if (bytes is null)
        {
            Log.Warning("[ModelCatalog] Bundled {File} not found; using empty catalog.", CatalogFile);
            return ModelCatalog.Empty;
        }

        try
        {
            ModelCatalogDto? dto = JsonSerializer.Deserialize(bytes, CoreJsonContext.Default.ModelCatalogDto);
            return dto is null ? ModelCatalog.Empty : ModelCatalog.FromDto(dto);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // JsonException = malformed JSON; ArgumentException = a structurally-valid
            // doc that still violates a domain invariant (e.g. duplicate effort id ->
            // ToDictionary throws). Both must honor the "never throws" contract.
            Log.Warning(ex, "[ModelCatalog] {File} could not be loaded; using empty catalog.", CatalogFile);
            return ModelCatalog.Empty;
        }
    }

    /// <summary>
    /// Read the bundled base catalog and apply its sibling overlay (RFC 7396),
    /// reusing <see cref="SchemaRegistry.ApplyMergePatch"/>. Returns base bytes if
    /// no overlay exists, and falls open to base bytes if the overlay is malformed.
    /// </summary>
    private static byte[]? TryReadMerged()
    {
        byte[]? baseBytes = BundledResource.TryRead(SubNamespace, CatalogFile);
        if (baseBytes is null)
        {
            return null;
        }

        byte[]? overlayBytes = BundledResource.TryRead(SubNamespace, OverlayFile);
        if (overlayBytes is null)
        {
            return baseBytes;
        }

        try
        {
            JsonNode? baseNode = JsonNode.Parse(baseBytes);
            JsonNode? overlayNode = JsonNode.Parse(overlayBytes);
            JsonNode? merged = SchemaRegistry.ApplyMergePatch(baseNode, overlayNode);
            string json = merged?.ToJsonString() ?? Encoding.UTF8.GetString(baseBytes);
            return Encoding.UTF8.GetBytes(json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            Log.Warning(ex, "[ModelCatalog] Overlay {Overlay} could not be applied; using base unchanged.", OverlayFile);
            return baseBytes;
        }
    }
}
