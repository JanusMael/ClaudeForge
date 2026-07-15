using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Json.Schema;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Core.Schema;

/// <summary>
/// Manages loading and caching of JSON schemas.
/// Loading priority: memory cache → disk cache → HTTP fetch → bundled fallback.
/// </summary>
public sealed class SchemaRegistry : IDisposable
{
    public const string ClaudeCodeSettingsSchemaUrl = "https://json.schemastore.org/claude-code-settings.json";

    private readonly HttpClient _http;

    // ConcurrentDictionary: GetSchemaAsync is called from multiple async call sites
    // (including background tasks); a plain Dictionary is not thread-safe for concurrent
    // reads + writes and would cause intermittent data races.
    private readonly ConcurrentDictionary<string, JsonSchema> _memoryCache = new();

    public SchemaRegistry(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>
    /// Get the Claude Code settings schema root node.
    /// Uses the standard loading chain: memory → disk → HTTP → bundled fallback.
    /// </summary>
    public async Task<JsonSchemaNode> GetClaudeCodeSettingsNodeAsync(CancellationToken ct = default)
    {
        JsonSchema schema = await GetSchemaAsync(ClaudeCodeSettingsSchemaUrl, "claude-code-settings.json", ct);
        // Root is non-null for any successfully parsed schema; the fallback ParseSchema("{}")
        // may return null Root, which would indicate a library contract break — throw explicitly.
        return schema.Root
               ?? throw new InvalidOperationException("Loaded schema had a null root node.");
    }

    /// <summary>
    /// Get the Claude Desktop config schema root node.
    /// </summary>
    public async Task<JsonSchemaNode> GetClaudeDesktopConfigNodeAsync(CancellationToken ct = default)
    {
        JsonSchema schema = await GetSchemaAsync("bundled://claude-desktop-config", "claude-desktop-config.json", ct);
        return schema.Root
               ?? throw new InvalidOperationException("Loaded schema had a null root node.");
    }

    /// <summary>Shared empty result for <see cref="GetEnumDescriptions"/>.</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> EmptyEnumDescriptions =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(0);

    /// <summary>
    /// Per-property enum/example value descriptions (a map value→description), keyed by the
    /// dot-path <see cref="SchemaTreeBuilder"/> uses (e.g. <c>"model"</c>,
    /// <c>"permissions.defaultMode"</c>). Powers per-item tooltips on the value picker.
    /// </summary>
    /// <remarks>
    /// Sourced from a dedicated resource under <c>Assets/Descriptions/</c> (e.g.
    /// <c>claude-code-settings.enumdescriptions.json</c>) — deliberately NOT inside the JSON
    /// Schema/overlay and NOT under <c>Assets/Schemas/</c>. Two reasons: JsonSchema.Net
    /// strict-rejects unknown keywords for this dialect (a custom keyword in the schema crashes
    /// <see cref="ParseSchema"/> and the <c>RestoreEngine</c> validation path), and
    /// <c>BackupEngine.BundleSchemas</c> bundles everything under <c>Assets/Schemas/</c> into
    /// backups where <c>RestoreEngine</c> would then try to parse it as a schema. Read here via
    /// <see cref="System.Text.Json"/>, never through JsonSchema.Net. Resource shape:
    /// <c>{ "&lt;jsonPath&gt;": { "&lt;value&gt;": "&lt;desc&gt;" } }</c>; non-object root entries
    /// (e.g. a <c>"$comment"</c> string) are ignored.
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> GetEnumDescriptions(string cacheFileName)
    {
        string descFileName = Path.GetFileNameWithoutExtension(cacheFileName)
                              + ".enumdescriptions"
                              + Path.GetExtension(cacheFileName);
        byte[]? bytes = BundledResource.TryRead("Descriptions", descFileName);
        if (bytes is null)
        {
            return EmptyEnumDescriptions;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return EmptyEnumDescriptions;
            }

            Dictionary<string, IReadOnlyDictionary<string, string>> result = new(StringComparer.Ordinal);
            foreach (JsonProperty pathEntry in doc.RootElement.EnumerateObject())
            {
                // Skip non-object entries (e.g. a "$comment" string at the root).
                if (pathEntry.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                Dictionary<string, string> map = new(StringComparer.Ordinal);
                foreach (JsonProperty d in pathEntry.Value.EnumerateObject())
                {
                    if (d.Value.ValueKind == JsonValueKind.String)
                    {
                        map[d.Name] = d.Value.GetString()!;
                    }
                }

                if (map.Count > 0)
                {
                    result[pathEntry.Name] = map;
                }
            }

            return result.Count > 0 ? result : EmptyEnumDescriptions;
        }
        catch (JsonException)
        {
            return EmptyEnumDescriptions;
        }
    }

    /// <summary>Shared empty result for <see cref="GetHookCommandVariants"/>.</summary>
    private static readonly IReadOnlyList<HookCommandVariantInfo> EmptyHookCommandVariants = [];

    /// <summary>
    /// The hook command variants declared in the settings schema's
    /// <c>$defs.hookCommand.anyOf</c> — each variant's <c>type</c> discriminator, its
    /// description, and its per-field descriptions. Powers the Hooks editor's Type-picker
    /// help text and per-field tooltips, sourced from the schema instead of a hardcoded mirror.
    /// </summary>
    /// <remarks>
    /// Reads the bundled merged schema JSON directly via <see cref="System.Text.Json"/>
    /// (mirrors <see cref="GetEnumDescriptions"/>), NOT the flattened <see cref="SchemaNode"/>
    /// tree: the <c>anyOf</c> variants and their <c>type.const</c> discriminators don't survive
    /// <see cref="SchemaTreeBuilder"/>, which collapses combinator branches. The bundled schema is
    /// the same source the node tree is built from (<see cref="GetSchemaAsync"/> prefers bundled +
    /// overlay over disk/network), so the two stay consistent. Returns an empty list when the
    /// resource is missing or malformed (fail-open — never blocks the editor).
    /// </remarks>
    public static IReadOnlyList<HookCommandVariantInfo> GetHookCommandVariants(string cacheFileName)
    {
        byte[]? bytes = TryReadBundledBytesMerged(cacheFileName);
        if (bytes is null)
        {
            return EmptyHookCommandVariants;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(bytes);
            if (!doc.RootElement.TryGetProperty("$defs", out JsonElement defs)
                || !defs.TryGetProperty("hookCommand", out JsonElement hookCommand)
                || !hookCommand.TryGetProperty("anyOf", out JsonElement anyOf)
                || anyOf.ValueKind != JsonValueKind.Array)
            {
                return EmptyHookCommandVariants;
            }

            List<HookCommandVariantInfo> variants = new();
            foreach (JsonElement variant in anyOf.EnumerateArray())
            {
                if (variant.ValueKind != JsonValueKind.Object
                    || !variant.TryGetProperty("properties", out JsonElement props)
                    || props.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? variantDesc = ReadDescription(variant);

                string? type = null;
                List<HookFieldInfo> fields = new();
                foreach (JsonProperty prop in props.EnumerateObject())
                {
                    if (prop.Name == "type")
                    {
                        // The discriminator is captured as Type, not surfaced as a field —
                        // its description is a bare "Hook type" that adds nothing as a tooltip.
                        type = ReadTypeConst(prop.Value);
                        continue;
                    }

                    fields.Add(new HookFieldInfo(prop.Name, ReadDescription(prop.Value)));
                }

                // A hookCommand variant with no `type` discriminator is unusable for the
                // picker (nothing to key it by); skip it rather than emit an anonymous entry.
                if (!string.IsNullOrEmpty(type))
                {
                    variants.Add(new HookCommandVariantInfo(type, variantDesc, fields));
                }
            }

            return variants.Count > 0 ? variants : EmptyHookCommandVariants;
        }
        catch (JsonException)
        {
            return EmptyHookCommandVariants;
        }
    }

    /// <summary>Shared empty result for <see cref="GetHookEvents"/>.</summary>
    private static readonly IReadOnlyList<HookEventInfo> EmptyHookEvents = [];

    /// <summary>
    /// The hook lifecycle events declared in the settings schema's
    /// <c>properties.hooks.properties</c> — each event's name plus its schema description.
    /// The raw-JSON counterpart to reading the <c>hooks</c> <see cref="SchemaNode"/>'s children:
    /// used when a client wasn't opened via <c>OpenAsync</c> (e.g. the GUI's
    /// <c>FromExistingWorkspace</c> path), so no <see cref="SchemaNode"/> tree was cached, yet
    /// the event descriptions must still surface. Reads the bundled merged schema — the same
    /// source the tree derives from — so the two stay consistent. Fail-open empty on a missing
    /// or malformed resource.
    /// </summary>
    public static IReadOnlyList<HookEventInfo> GetHookEvents(string cacheFileName)
    {
        byte[]? bytes = TryReadBundledBytesMerged(cacheFileName);
        if (bytes is null)
        {
            return EmptyHookEvents;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(bytes);
            if (!doc.RootElement.TryGetProperty("properties", out JsonElement props)
                || !props.TryGetProperty("hooks", out JsonElement hooks)
                || !hooks.TryGetProperty("properties", out JsonElement hooksProps)
                || hooksProps.ValueKind != JsonValueKind.Object)
            {
                return EmptyHookEvents;
            }

            List<HookEventInfo> events = new();
            foreach (JsonProperty ev in hooksProps.EnumerateObject())
            {
                events.Add(new HookEventInfo(ev.Name, ReadDescription(ev.Value)));
            }

            return events.Count > 0 ? events : EmptyHookEvents;
        }
        catch (JsonException)
        {
            return EmptyHookEvents;
        }
    }

    /// <summary>Read a schema node's <c>description</c> string, or <see langword="null"/>.</summary>
    private static string? ReadDescription(JsonElement schemaNode) =>
        schemaNode.TryGetProperty("description", out JsonElement d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()
            : null;

    /// <summary>
    /// Read the <c>type</c> field's discriminator value: prefer <c>const</c>, fall back to
    /// the first <c>enum</c> entry. Returns <see langword="null"/> when neither is a string.
    /// </summary>
    private static string? ReadTypeConst(JsonElement typeSchema)
    {
        if (typeSchema.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (typeSchema.TryGetProperty("const", out JsonElement c) && c.ValueKind == JsonValueKind.String)
        {
            return c.GetString();
        }

        if (typeSchema.TryGetProperty("enum", out JsonElement e) && e.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in e.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    return item.GetString();
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Get a schema by URL, with bundled-first loading and disk caching.
    /// Loading priority: memory cache → bundled resource (+ overlay) → disk cache → HTTP fetch.
    /// <para>
    /// Bundled resources are loaded before the disk cache or network because the
    /// embedded schema may have an accompanying <c>.overlay.json</c> sibling that
    /// adds hand-curated fields the upstream schemastore.org schema does not carry
    /// (e.g. <c>model.examples</c> populating the AutoCompleteBox suggestion list,
    /// <c>model.default</c> driving the "(inherits: &lt;alias&gt;)" watermark).  The
    /// overlay is applied at this layer via RFC 7396 JSON Merge Patch semantics so
    /// refreshing the upstream schema via <c>scripts/refresh-schema.{sh,ps1}</c>
    /// never touches the hand-curated additions — they live in a separate file.
    /// Placing bundled second (over disk/network) ensures the merged result is
    /// always the source of truth regardless of whether a stale disk cache or fresh
    /// network copy exists.
    /// </para>
    /// </summary>
    public async Task<JsonSchema> GetSchemaAsync(string url, string cacheFileName, CancellationToken ct = default)
    {
        // 1. Memory cache
        if (_memoryCache.TryGetValue(url, out JsonSchema? cached))
        {
            return cached;
        }

        string diskPath = Path.Combine(PlatformPaths.SchemaCacheDirectory, cacheFileName);

        // 2. Bundled resource (with overlay applied) — always preferred over
        //    disk/network when present.  Hand-curated additions survive cache
        //    refreshes via the overlay-merge step inside the helper.
        byte[]? bundledBytes = TryReadBundledBytesMerged(cacheFileName);
        if (bundledBytes != null)
        {
            JsonSchema schema = ParseSchema(Encoding.UTF8.GetString(bundledBytes));
            _memoryCache[url] = schema;
            // Keep disk cache in sync as a side-effect (fire-and-forget; failures are silent).
            _ = SyncDiskWithBundledAsync(diskPath, bundledBytes, ct);
            return schema;
        }

        // 3. Disk cache (for schemas without a bundled resource)
        if (File.Exists(diskPath))
        {
            try
            {
                JsonSchema schema = await LoadFromFileAsync(diskPath, ct);
                _memoryCache[url] = schema;
                return schema;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Log.Debug(ex, "[Schema] Disk cache read failed for {Url}, falling through to HTTP", url);
            }
        }

        // 4. HTTPS fetch (only for real HTTPS URLs — "bundled://" etc. fall straight through).
        //
        // Plain http:// is rejected: a network intercept could serve an attacker-crafted
        // schema that the cache step at SaveToDiskCacheAsync would persist, poisoning
        // every subsequent launch even after the network is healthy. If a caller passes
        // an http URL we fall straight through to the empty-schema fallback rather than
        // fetching over plaintext.
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string json = await FetchWithRedirectAsync(url, ct);
                JsonSchema schema = ParseSchema(json);
                _memoryCache[url] = schema;
                await SaveToDiskCacheAsync(diskPath, json, ct);
                return schema;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                           or IOException or JsonException)
            {
                Log.Warning(ex, "[Schema] HTTPS fetch failed for {Url}, falling back to empty schema", url);
            }
        }
        else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(
                "[Schema] Refused to fetch over plain HTTP: {Url}. Use https:// or bundled://",
                url);
        }

        // 5. Absolute last resort — return an empty schema so the app can still start.
        return ParseSchema("{}");
    }

    private static async Task SyncDiskWithBundledAsync(string diskPath, byte[] bundledBytes, CancellationToken ct)
    {
        try
        {
            if (File.Exists(diskPath))
            {
                byte[] diskBytes = await File.ReadAllBytesAsync(diskPath, ct);
                if (bundledBytes.AsSpan().SequenceEqual(diskBytes.AsSpan()))
                {
                    return; // already in sync
                }
            }

            await SaveToDiskCacheAsync(diskPath, Encoding.UTF8.GetString(bundledBytes), ct);
        }
        catch (OperationCanceledException)
        {
            /* app shutting down — normal */
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Disk sync is best-effort; a read-only cache directory is acceptable.
            Log.Debug(ex, "[Schema] Disk-sync failed for {Path}", diskPath);
        }
    }

    /// <summary>
    /// Force-refresh the disk cache for a schema URL.
    /// </summary>
    public async Task RefreshAsync(string url, string cacheFileName, CancellationToken ct = default)
    {
        _memoryCache.TryRemove(url, out JsonSchema? _);
        string diskPath = Path.Combine(PlatformPaths.SchemaCacheDirectory, cacheFileName);
        if (File.Exists(diskPath))
        {
            File.Delete(diskPath);
        }

        await GetSchemaAsync(url, cacheFileName, ct);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Parse a JSON Schema text without touching the process-wide
    /// <see cref="Json.Schema.SchemaRegistry.Global"/> singleton.
    /// <para>
    /// <c>JsonSchema.FromText</c> normally registers the parsed schema in the
    /// global registry keyed by its <c>$id</c> URI. Calling it a second time
    /// for the same document (on reload, project open, or from a second
    /// <see cref="SchemaRegistry"/> instance in tests) throws
    /// <c>JsonSchemaException: Overwriting registered schemas is not permitted</c>.
    /// Supplying a fresh local <see cref="Json.Schema.SchemaRegistry"/> via
    /// <see cref="BuildOptions"/> keeps every parse fully isolated.
    /// </para>
    /// </summary>
    internal static JsonSchema ParseSchema(string json)
    {
        BuildOptions opts = new() { SchemaRegistry = new Json.Schema.SchemaRegistry() };
        return JsonSchema.FromText(json, opts);
    }

    private static async Task<JsonSchema> LoadFromFileAsync(string path, CancellationToken ct)
    {
        string json = await File.ReadAllTextAsync(path, ct);
        return ParseSchema(json);
    }

    private async Task<string> FetchWithRedirectAsync(string url, CancellationToken ct)
    {
        // schemastore.org sends a 301; HttpClient follows automatically
        HttpResponseMessage response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static async Task SaveToDiskCacheAsync(string path, string json, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, json, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug(ex, "[Schema] Cannot write disk cache for {Path}", path);
        }
    }

    private static byte[]? TryReadBundledBytes(string cacheFileName)
        => BundledResource.TryRead("Schemas", cacheFileName);

    /// <summary>
    /// Read the bundled base schema and apply its sibling <c>.overlay.json</c>
    /// (if present) via RFC 7396 JSON Merge Patch.  Returns the merged bytes,
    /// or <c>null</c> if no base resource exists.
    /// </summary>
    /// <remarks>
    /// Overlay naming: for input <c>foo.json</c> the overlay is
    /// <c>foo.overlay.json</c> (i.e. inject <c>.overlay</c> before the extension).
    /// If the overlay resource doesn't exist, the base bytes are returned
    /// unchanged.  If the overlay exists but is malformed, the merge is
    /// skipped with a warning and the base bytes are returned unchanged —
    /// fail-open so a broken overlay never prevents the app from starting.
    /// </remarks>
    internal static byte[]? TryReadBundledBytesMerged(string cacheFileName)
    {
        byte[]? baseBytes = TryReadBundledBytes(cacheFileName);
        if (baseBytes == null)
        {
            return null;
        }

        string overlayFileName = Path.GetFileNameWithoutExtension(cacheFileName)
                                 + ".overlay"
                                 + Path.GetExtension(cacheFileName);
        byte[]? overlayBytes = TryReadBundledBytes(overlayFileName);
        if (overlayBytes == null)
        {
            return baseBytes; // no overlay, return base unchanged
        }

        try
        {
            JsonNode? baseNode = JsonNode.Parse(baseBytes);
            JsonNode? overlayNode = JsonNode.Parse(overlayBytes);
            JsonNode? merged = ApplyMergePatch(baseNode, overlayNode);
            // Serialise with the same compact-ish indentation upstream uses; the
            // bytes feed into ParseSchema + may also land in the disk cache.
            string json = merged?.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
                          ?? Encoding.UTF8.GetString(baseBytes);
            return Encoding.UTF8.GetBytes(json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            Log.Warning(ex,
                "[Schema] Overlay {Overlay} could not be applied to {Base}; using base unchanged",
                overlayFileName, cacheFileName);
            return baseBytes;
        }
    }

    /// <summary>
    /// RFC 7396 JSON Merge Patch.  Recursively merges <paramref name="patch"/>
    /// onto <paramref name="target"/>: object values are merged key-by-key
    /// (overlay key replaces target key, or recurses if both are objects);
    /// arrays / primitives in the patch wholesale-replace; <c>null</c> in the
    /// patch deletes the key in the target.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by <see cref="TryReadBundledBytesMerged"/> to layer the
    /// hand-curated overlay onto the verbatim upstream schema at load time.
    /// Internal for parity testing — see <c>SchemaRegistryOverlayTests</c>.
    /// </para>
    /// <para>
    /// when the target already has the patched key, the assignment
    /// uses the <see cref="JsonObject"/> indexer setter rather than a
    /// <c>Remove</c> + re-add pair, because the latter moves the key to the
    /// end of the underlying <c>OrderedDictionary</c> and silently re-orders
    /// the schema — observed as the <c>model</c> property jumping to the
    /// bottom of the editor list after the overlay was introduced.
    /// </para>
    /// </remarks>
    internal static JsonNode? ApplyMergePatch(JsonNode? target, JsonNode? patch)
    {
        // RFC 7396 §1: if patch is not an object, patch replaces target wholesale.
        if (patch is not JsonObject patchObj)
        {
            return patch?.DeepClone();
        }

        JsonObject? targetObj = target as JsonObject;
        JsonObject result = targetObj is not null ? (JsonObject)targetObj.DeepClone() : new JsonObject();
        foreach (KeyValuePair<string, JsonNode?> kvp in patchObj)
        {
            if (kvp.Value is null)
            {
                // RFC 7396 §2: null in patch removes the key from target.
                result.Remove(kvp.Key);
                continue;
            }

            // Compute the new value before assigning.  ApplyMergePatch returns
            // a fresh JsonNode (DeepClone for non-object patch values, a brand-
            // new JsonObject for object recursion) — neither has a parent, so
            // it's safe to attach via the indexer setter.  Direct assignment
            // (no Remove first) preserves the existing key's position in the
            // OrderedDictionary; Remove + re-add would push it to the end.
            JsonNode? newValue = kvp.Value is JsonObject
                ? ApplyMergePatch(targetObj?[kvp.Key], kvp.Value)
                : kvp.Value.DeepClone();
            result[kvp.Key] = newValue;
        }

        return result;
    }

    private static JsonSchema LoadBundledFallback(string cacheFileName)
    {
        // Same overlay path as the primary loader so the fallback respects
        // hand-curated additions too.
        byte[]? bytes = TryReadBundledBytesMerged(cacheFileName);
        if (bytes == null)
        {
            return ParseSchema("{}"); // absolute last resort — empty schema
        }

        return ParseSchema(Encoding.UTF8.GetString(bytes));
    }

    /// <summary>
    /// Validates every dirty, writable document in <paramref name="workspace"/> against the
    /// JSON schema for that product (<paramref name="isClaudeCode"/> selects Claude Code
    /// settings vs. Claude Desktop config).
    /// </summary>
    /// <returns>
    /// An empty list when all documents pass, or a list of <see cref="SchemaValidationError"/>
    /// entries describing each violation.  Returns an empty list without blocking when the
    /// schema could not be loaded (fail-open — we never prevent saves due to missing schema).
    /// </returns>
    public async Task<IReadOnlyList<SchemaValidationError>> ValidateWorkspaceAsync(
        SettingsWorkspace workspace,
        bool isClaudeCode,
        CancellationToken ct = default)
    {
        JsonSchema schema = isClaudeCode
            ? await GetSchemaAsync(ClaudeCodeSettingsSchemaUrl, "claude-code-settings.json", ct)
            : await GetSchemaAsync("bundled://claude-desktop-config", "claude-desktop-config.json", ct);

        List<SchemaValidationError> errors = new();
        EvaluationOptions evalOpts = new() { OutputFormat = OutputFormat.List };

        foreach (SettingsDocument doc in workspace.Documents.Where(d => d.IsDirty && !d.IsReadOnly))
        {
            // Delta validation: only report violations that are NEW (introduced by the
            // user's edits in this session).  Pre-existing violations that were already in
            // the on-disk file before editing are not the user's fault — reporting them
            // when saving a single field would produce hundreds of spurious errors.
            IReadOnlyList<SchemaValidationError> baselineErrors =
                CollectSchemaErrors(schema, doc.BaselineRoot ?? new JsonObject(), evalOpts, doc.FilePath);
            HashSet<(string InstancePath, string Message)> baselineKeys = new(
                baselineErrors.Select(static e => (e.InstancePath, e.Message)));

            foreach (SchemaValidationError err in CollectSchemaErrors(schema, doc.Root, evalOpts, doc.FilePath))
            {
                if (!baselineKeys.Contains((err.InstancePath, err.Message)))
                {
                    errors.Add(err);
                }
            }
        }

        return await EnrichAllowedValuesAsync(errors, isClaudeCode, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Best-effort pass that attaches <see cref="SchemaValidationError.AllowedValues"/>
    /// to enum-mismatch errors, so the message can show the permitted values (from the
    /// same <see cref="SchemaNode"/> tree the editor uses for its dropdowns) rather than
    /// just "should match one of the enum values". Runs only when errors exist (rare,
    /// user-facing save path); the node fetch is cached, so this is cheap. Never throws
    /// — enrichment failure falls back to the un-enriched errors.
    /// </summary>
    private async Task<IReadOnlyList<SchemaValidationError>> EnrichAllowedValuesAsync(
        List<SchemaValidationError> errors, bool isClaudeCode, CancellationToken ct)
    {
        if (errors.Count == 0)
        {
            return errors;
        }

        Dictionary<string, IReadOnlyList<string>> enumsByPath;
        try
        {
            JsonSchemaNode rootNode = isClaudeCode
                ? await GetClaudeCodeSettingsNodeAsync(ct).ConfigureAwait(false)
                : await GetClaudeDesktopConfigNodeAsync(ct).ConfigureAwait(false);
            enumsByPath = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            CollectEnumPaths(SchemaTreeBuilder.BuildTopLevel(rootNode), enumsByPath);
        }
        catch
        {
            return errors;
        }

        for (int i = 0; i < errors.Count; i++)
        {
            // InstancePath is a JSON Pointer (/permissions/defaultMode); SchemaNode.JsonPath
            // is dot-separated (permissions.defaultMode). Convert to match leaf enum props.
            string dotPath = errors[i].InstancePath.TrimStart('/').Replace('/', '.');
            if (dotPath.Length > 0 && enumsByPath.TryGetValue(dotPath, out IReadOnlyList<string>? allowed))
            {
                errors[i] = errors[i] with { AllowedValues = allowed };
            }
        }

        return errors;
    }

    /// <summary>
    /// Recursively index every enum-bearing node by its dot-separated
    /// <see cref="SchemaNode.JsonPath"/> so a validation error's instance path resolves
    /// to its permitted values.
    /// </summary>
    private static void CollectEnumPaths(
        IReadOnlyList<SchemaNode> nodes, Dictionary<string, IReadOnlyList<string>> sink)
    {
        foreach (SchemaNode node in nodes)
        {
            if (node.EnumValues.Count > 0)
            {
                sink[node.JsonPath] = node.EnumValues;
            }

            if (node.Properties.Count > 0)
            {
                CollectEnumPaths(node.Properties, sink);
            }

            if (node.ItemsSchema is { } items)
            {
                CollectEnumPaths([items], sink);
            }
        }
    }

    /// <summary>
    /// Validates <b>every</b> writable document in <paramref name="workspace"/>
    /// against its product schema and returns <b>all</b> currently-invalid
    /// fields — including pre-existing violations that were already on disk
    /// before the user edited anything.  Counterpart to
    /// <see cref="ValidateWorkspaceAsync"/>, which only reports
    /// user-introduced deltas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>When to use which:</b>
    /// </para>
    /// <list type="bullet">
    ///   <item><description><see cref="ValidateWorkspaceAsync"/> — pre-save
    ///   "what did I just introduce" check; suppresses pre-existing errors so
    ///   a single edit doesn't surface hundreds of unrelated baseline issues.</description></item>
    ///   <item><description><see cref="ValidateAllWorkspaceAsync"/> — post-reload
    ///   "what's currently wrong in the loaded files" check, used by the
    ///   schema-violation banner so externally-introduced invalid values are
    ///   surfaced even when the workspace is otherwise clean.</description></item>
    /// </list>
    /// </remarks>
    public async Task<IReadOnlyList<SchemaValidationError>> ValidateAllWorkspaceAsync(
        SettingsWorkspace workspace,
        bool isClaudeCode,
        CancellationToken ct = default)
    {
        JsonSchema schema = isClaudeCode
            ? await GetSchemaAsync(ClaudeCodeSettingsSchemaUrl, "claude-code-settings.json", ct)
            : await GetSchemaAsync("bundled://claude-desktop-config", "claude-desktop-config.json", ct);

        List<SchemaValidationError> errors = new();
        EvaluationOptions evalOpts = new() { OutputFormat = OutputFormat.List };

        // No IsDirty filter, no baseline-delta filter: report every current
        // violation in every writable document.  Read-only (Managed) docs are
        // skipped because the user has no path to fix them from this app.
        foreach (SettingsDocument doc in workspace.Documents.Where(d => !d.IsReadOnly))
        {
            errors.AddRange(CollectSchemaErrors(schema, doc.Root, evalOpts, doc.FilePath));
        }

        return errors;
    }

    /// <summary>
    /// Validates <paramref name="root"/> against <paramref name="schema"/> and returns all
    /// violations as a flat list.  Used by <see cref="ValidateWorkspaceAsync"/> to compute the
    /// delta between the baseline (on-disk) state and the current (edited) state.
    /// </summary>
    /// <remarks>
    /// user report (3.10 manual test): adding a valid command hook
    /// to <c>WorktreeCreate</c> produced 9 leaked errors with messages like
    /// "Required properties [\"prompt\"] are not present" and
    /// "All values fail against the false schema". Root cause: JsonSchema.Net's
    /// <see cref="OutputFormat.List"/> emits an <see cref="EvaluationResults"/>
    /// detail for EVERY anyOf branch at every site, with each branch's
    /// <see cref="EvaluationResults.IsValid"/> reported independently. The
    /// previous logic only early-returned via <c>if (results.IsValid) return [];</c>
    /// — when pre-existing baseline errors elsewhere kept root invalid, the
    /// per-detail iteration emitted non-matching anyOf-branch failures even
    /// though sibling branches passed.
    ///
    /// Fix: pre-compute the set of "passing anyOf branch roots" (eval paths
    /// shaped like <c>.../anyOf/&lt;digits&gt;</c> with IsValid=true) and
    /// suppress any error detail whose path traverses an anyOf site whose
    /// sibling matched. Handles arbitrary anyOf nesting via a single
    /// per-prefix lookup. Errors from anyOfs where ALL branches failed (i.e.
    /// the keyword genuinely failed) still emit because no sibling-passing
    /// prefix matches their path.
    /// </remarks>
    private static IReadOnlyList<SchemaValidationError> CollectSchemaErrors(
        JsonSchema schema,
        JsonObject root,
        EvaluationOptions evalOpts,
        string filePath)
    {
        // Round-trip through JsonElement — JsonSchema.Net v8 Evaluate() takes JsonElement.
        using JsonDocument jsonDoc = JsonDocument.Parse(root.ToJsonString());
        EvaluationResults results = schema.Evaluate(jsonDoc.RootElement, evalOpts);
        if (results.IsValid)
        {
            return [];
        }

        // Build the set of "an anyOf branch root passed at this site". Each
        // entry is the prefix `.../anyOf/` (with trailing slash) of an
        // evaluation path whose tail is a passing branch root.
        HashSet<string> passingAnyOfPrefixes = new(StringComparer.Ordinal);
        foreach (EvaluationResults detail in results.Details ?? [])
        {
            if (!detail.IsValid)
            {
                continue;
            }

            string evalPath = detail.EvaluationPath.ToString() ?? string.Empty;
            string? pfx = TryGetPassingAnyOfBranchPrefix(evalPath);
            if (pfx is not null)
            {
                passingAnyOfPrefixes.Add(pfx);
            }
        }

        List<SchemaValidationError> errors = new();
        foreach (EvaluationResults detail in results.Details ?? [])
        {
            Dictionary<string, string>? errs = detail.Errors;
            if (detail.IsValid || errs is null || errs.Count == 0)
            {
                continue;
            }

            string evalPath = detail.EvaluationPath.ToString() ?? string.Empty;
            if (IsLeakedAnyOfBranchError(evalPath, passingAnyOfPrefixes))
            {
                continue;
            }

            string path = detail.InstanceLocation.ToString() ?? string.Empty;
            // Read the offending value once per site (all messages here share the path)
            // so the user sees WHAT they have, not just that it's wrong.
            string? offendingValue = RenderOffendingValue(NavigateToInstance(root, path));
            foreach ((string _, string message) in errs)
            {
                errors.Add(new SchemaValidationError(filePath, path, message) { Value = offendingValue });
            }
        }

        return CollapseFailedAnyOfErrors(errors);
    }

    /// <summary>
    /// Resolve a JSON-Pointer instance location (e.g. <c>/permissions/allow/0</c>)
    /// against the parsed document, returning the node at that path or null when the
    /// path doesn't resolve. Version-agnostic (walks the tree by hand rather than
    /// depending on a pointer library's evaluate API); unescapes the pointer tokens
    /// <c>~1</c>→<c>/</c> and <c>~0</c>→<c>~</c>.
    /// </summary>
    private static JsonNode? NavigateToInstance(JsonObject root, string instancePointer)
    {
        if (string.IsNullOrEmpty(instancePointer) || instancePointer == "/")
        {
            return root;
        }

        JsonNode? current = root;
        foreach (string rawSeg in instancePointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string seg = rawSeg.Replace("~1", "/").Replace("~0", "~");
            switch (current)
            {
                case JsonObject obj when obj.TryGetPropertyValue(seg, out JsonNode? child):
                    current = child;
                    break;
                case JsonArray arr when int.TryParse(seg, out int idx) && idx >= 0 && idx < arr.Count:
                    current = arr[idx];
                    break;
                default:
                    return null;
            }
        }

        return current;
    }

    /// <summary>
    /// Render <paramref name="node"/> as compact JSON for display in a validation
    /// message — quoted for strings (so <c>"max"</c> reads as a string), braces for
    /// objects/arrays — truncated so a large offending object doesn't flood the dialog.
    /// Null in → null out (nothing to show).
    /// </summary>
    private static string? RenderOffendingValue(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        string json;
        try
        {
            json = node.ToJsonString();
        }
        catch
        {
            return null;
        }

        const int max = 200;
        return json.Length > max ? string.Concat(json.AsSpan(0, max), "…") : json;
    }

    /// <summary>
    /// Collapse multiple errors that share an <see cref="SchemaValidationError.InstancePath"/>
    /// into a single combined error.  When every branch of an anyOf / oneOf
    /// fails, JsonSchema.Net emits one error per branch — for a Marketplace
    /// <c>source</c> object that matches none of the schema's variants this can
    /// produce 6+ "Required <c>X</c> not present" / "Expected <c>"git"</c>"
    /// lines for one logical "doesn't match any allowed shape".
    /// </summary>
    /// <remarks>
    /// Heuristic: errors sharing an instance path almost always originate from
    /// the same anyOf / oneOf evaluation site (the combinator site IS the path
    /// at which all variants are evaluated against the same value).  Collapsing
    /// by instance path is therefore a safe approximation without having to
    /// track evaluation-path provenance through every emit site.
    /// <para>
    /// Distinct messages from a single instance path are joined with <c>" | "</c>
    /// so the user still sees what each branch wanted; the resulting line is
    /// readable even when 6 variants were tried because the message text is
    /// usually short ("Required X not present", "Expected literal Y").
    /// </para>
    /// </remarks>
    private static IReadOnlyList<SchemaValidationError> CollapseFailedAnyOfErrors(
        List<SchemaValidationError> errors)
    {
        if (errors.Count <= 1)
        {
            return errors;
        }

        List<SchemaValidationError> collapsed = new(errors.Count);
        foreach (IGrouping<(string FilePath, string InstancePath), SchemaValidationError> group in errors.GroupBy(e => (e.FilePath, e.InstancePath)))
        {
            List<SchemaValidationError> list = group.ToList();
            if (list.Count == 1)
            {
                collapsed.Add(list[0]);
                continue;
            }

            // Distinct preserves order of first occurrence so the user sees
            // the messages in the order JsonSchema.Net emitted them
            // (typically the schema's declared variant order).
            List<string> distinct = list
                                    .Select(e => e.Message)
                                    .Distinct(StringComparer.Ordinal)
                                    .ToList();

            string summary = distinct.Count == 1
                ? distinct[0]
                : $"Value matches none of the {distinct.Count} permitted variants — "
                  + $"each variant requires one of: {string.Join(" | ", distinct)}";

            collapsed.Add(new SchemaValidationError(
                group.Key.FilePath,
                group.Key.InstancePath,
                summary)
            {
                // Every branch shares the instance path, so they share the offending
                // value — carry it onto the collapsed error (e.g. the theme object).
                Value = list[0].Value,
            });
        }

        return collapsed;
    }

    /// <summary>
    /// If <paramref name="evaluationPath"/> looks like a passing anyOf branch
    /// root (the LAST <c>/anyOf/</c> in the path is followed only by digits),
    /// returns the prefix up to and including that <c>/anyOf/</c>. The caller
    /// stashes this prefix to identify which anyOf evaluation sites had at
    /// least one matching branch.
    /// </summary>
    /// <remarks>
    /// Returning the prefix WITH a trailing slash lets <see cref="IsLeakedAnyOfBranchError"/>
    /// do a substring match without false positives on unrelated keywords
    /// that happen to start with "anyOf" (none today, but future-proofing).
    /// </remarks>
    private static string? TryGetPassingAnyOfBranchPrefix(string evaluationPath)
    {
        const string marker = "/anyOf/";
        int pos = evaluationPath.LastIndexOf(marker, StringComparison.Ordinal);
        if (pos < 0)
        {
            return null;
        }

        int after = pos + marker.Length;
        if (after >= evaluationPath.Length)
        {
            return null;
        }

        // Everything after the last "/anyOf/" must be a positive integer
        // (i.e., this detail is exactly at a branch root, not somewhere
        // deeper). A digits-only suffix is the JsonPointer encoding of an
        // array index; nothing else is a valid branch index.
        for (int i = after; i < evaluationPath.Length; i++)
        {
            char ch = evaluationPath[i];
            if (ch < '0' || ch > '9')
            {
                return null;
            }
        }

        return evaluationPath[..after];
    }

    /// <summary>
    /// True when <paramref name="evaluationPath"/> traverses any anyOf
    /// evaluation site whose sibling branch matched. Walks every
    /// <c>/anyOf/</c> boundary in the path and consults the pre-computed
    /// <paramref name="passingAnyOfPrefixes"/> set; returns on the first hit.
    /// Handles nested anyOfs because suppression at any outer level subsumes
    /// inner failures within a non-matching outer branch.
    /// </summary>
    private static bool IsLeakedAnyOfBranchError(string evaluationPath, HashSet<string> passingAnyOfPrefixes)
    {
        const string marker = "/anyOf/";
        int i = 0;
        while ((i = evaluationPath.IndexOf(marker, i, StringComparison.Ordinal)) >= 0)
        {
            string prefix = evaluationPath[..(i + marker.Length)];
            if (passingAnyOfPrefixes.Contains(prefix))
            {
                return true;
            }

            i += marker.Length;
        }

        return false;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}

// ---------------------------------------------------------------------------
// Companion types
// ---------------------------------------------------------------------------

/// <summary>One schema violation produced by <see cref="SchemaRegistry.ValidateWorkspaceAsync"/>.</summary>
public sealed record SchemaValidationError(string FilePath, string InstancePath, string Message)
{
    /// <summary>
    /// The offending value at <see cref="InstancePath"/>, rendered as compact JSON
    /// (e.g. <c>"max"</c> or <c>{"base":"dark",…}</c>), truncated when long. Null when
    /// the value could not be read (e.g. the failure is a missing-required-property).
    /// Surfaced in the validation message so the user sees <em>what</em> they have.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// The permitted values for an <c>enum</c> property, or null when the failure is
    /// not an enum mismatch (or the options aren't known). Surfaced so the user sees
    /// <em>what is allowed</em> instead of only "should match one of the enum values".
    /// </summary>
    public IReadOnlyList<string>? AllowedValues { get; init; }

    /// <summary>
    /// Human-readable property path, e.g.
    /// "extraKnownMarketplaces → everything-claude-code → source → repo".
    /// </summary>
    public string DisplayPath =>
        string.IsNullOrEmpty(InstancePath) || InstancePath == "/"
            ? "(root)"
            : InstancePath.TrimStart('/').Replace("/", " \u2192 ");
}

/// <summary>
/// Thrown by <c>IClaudeConfigClient.SaveAsync</c> when one or more dirty
/// documents fail schema validation and the caller did not pass
/// <c>force: true</c>.
/// </summary>
/// <remarks>
/// <para>
/// Consumers can either fix the offending values and retry, surface the
/// errors to the user, or call <c>SaveAsync(force: true, ct)</c> to bypass
/// validation entirely.
/// </para>
/// </remarks>
public sealed class SchemaValidationException : Exception
{
    /// <summary>The validation errors that blocked the save.</summary>
    public IReadOnlyList<SchemaValidationError> Errors { get; }

    public SchemaValidationException(IReadOnlyList<SchemaValidationError> errors)
        : base($"{errors.Count} schema validation error(s) blocked save.")
    {
        Errors = errors;
    }
}