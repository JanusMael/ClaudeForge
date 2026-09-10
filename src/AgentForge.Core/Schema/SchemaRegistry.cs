using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Json.Schema;
using Serilog;

namespace Bennewitz.Ninja.AgentForge.Core.Schema;

/// <summary>
/// Manages loading and caching of JSON schemas.
/// <para>
/// Loading priority: <b>memory cache → HTTPS fetch (+ strip, + overlay) → bundled resource
/// (+ strip, + overlay)</b>. There is no disk cache and no empty fallback. See
/// <see cref="GetSchemaAsync"/> for the full rationale, and
/// <c>SchemaLoadPrecedenceTests</c> for the behavioural guard.
/// </para>
/// <para>
/// ⚠ <b>This used to be bundled-first, and the prose said so in four places — twice as the
/// stated reason for a test's design.</b> The reversal is deliberate, not drift: the overlay
/// and the external-<c>$ref</c> strip now apply to whichever source wins, which is what made
/// bundled-first unnecessary. If you are about to "fix" a comment to match the old order,
/// read <see cref="GetSchemaAsync"/> first.
/// </para>
/// </summary>
public sealed class SchemaRegistry : IDisposable
{
    public const string ClaudeCodeSettingsSchemaUrl = "https://json.schemastore.org/claude-code-settings.json";

    private readonly HttpClient? _http;

    /// <summary>
    /// How long one schema fetch may take before the bundled copy is used instead.
    /// </summary>
    /// <remarks>
    /// ⚠ Deliberately far shorter than the <see cref="HttpClient"/>'s own 15s. This runs on
    /// the startup path, once per schema URL, so the client timeout would let a black-holed
    /// network freeze a launch for three quarters of a minute. Three seconds is generous for a
    /// small JSON over HTTPS. <see cref="_networkUnavailable"/> then bounds the offline cost to
    /// one timeout per REGISTRY INSTANCE rather than one per schema.
    /// <para>
    /// ⚠ <b>Measured: a launch builds TWO registries</b> — the window makes one for its page
    /// tree and each client makes its own — so each schema is fetched twice and an offline
    /// launch pays two timeouts, not one. Found by reading the app log, not by a test. Sharing
    /// one instance is worth doing; it needs the registry threaded through client
    /// construction, which is a separate change.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(3);

    /// <summary>A line whose only content is an <c>http(s)</c> <c>$ref</c>.</summary>
    private static readonly Regex ExternalRefLine = new(
        @"^\s*""\$ref""\s*:\s*""https?://",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Set once a fetch fails for a connectivity reason; suppresses further attempts for the
    /// life of this registry. Per-instance rather than static so tests stay isolated.
    /// </summary>
    private bool _networkUnavailable;

    /// <summary>
    /// The merged bytes per schema file name, from whichever source won. Lets the metadata
    /// readers describe the document the tree was actually built from.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte[]> _materialisedBytes = new(StringComparer.Ordinal);

    /// <summary>Where each loaded schema came from, keyed by file name.</summary>
    private readonly ConcurrentDictionary<string, SchemaProvenance> _provenance =
        new(StringComparer.Ordinal);

    // ConcurrentDictionary: GetSchemaAsync is called from multiple async call sites
    // (including background tasks); a plain Dictionary is not thread-safe for concurrent
    // reads + writes and would cause intermittent data races.
    private readonly ConcurrentDictionary<string, JsonSchema> _memoryCache = new();

    /// <param name="httpClient">
    /// The client used for the HTTPS step.
    /// <para>
    /// ⛔⛔ <b><see langword="null"/> means OFFLINE</b> — the fetch is skipped entirely and the
    /// bundled resource is used. That is the default deliberately, and it is the opposite of
    /// what it used to be: since the fetch now OUTRANKS bundled, a registry built without
    /// saying anything about the network would otherwise make live outbound requests and
    /// resolve its schemas against whatever upstream is serving today. Thirty-four test sites
    /// construct one exactly that way.
    /// </para>
    /// <para>
    /// Production wants the network, so it asks for it by name — see
    /// <see cref="CreateWithNetwork"/>. A production site that forgets simply behaves as the
    /// app did before network-first, which is why this default is the safe one.
    /// </para>
    /// </param>
    /// <param name="sourceOverride">
    /// Force the load down one branch, for diagnosis. <see langword="null"/> falls back to
    /// <see cref="ProcessSourceOverride"/>, and then to the normal chain.
    /// <para>
    /// ⚠ Explicit here, not read from the process default only, so a TEST can pin a branch
    /// without touching global state that another test would then see.
    /// </para>
    /// </param>
    public SchemaRegistry(HttpClient? httpClient = null, SchemaSourceOverride? sourceOverride = null)
    {
        _http = httpClient;
        _sourceOverride = sourceOverride ?? ProcessSourceOverride;
    }

    private readonly SchemaSourceOverride? _sourceOverride;

    /// <summary>
    /// Process-wide source override, set once at startup from a debug flag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>Process-wide because a CLI flag genuinely is.</b> A launch builds several registries
    /// — the window's, and one per client — and a flag that only reached the first would leave
    /// the pages bundled while save-validation still fetched. Modelling it as a parameter alone
    /// would have made the flag half-effective in a way nobody would notice.
    /// </para>
    /// <para>
    /// ⛔⛔ <b>The constructor parameter wins over this, and a test that SETS this must live in a
    /// <c>[DoNotParallelize]</c> class.</b> <c>AgentForge.Core.Tests</c> is
    /// <c>[assembly: Parallelize(MethodLevel)]</c>, so setting it from a parallelized test is not
    /// merely flaky — it makes every registry another test constructs concurrently load bundled.
    /// Measured: it passed in isolation and failed in the full suite. Prefer pinning a branch
    /// through the constructor, which is why that argument takes precedence.
    /// </para>
    /// <para>
    /// ⚠ It cannot reach here from <c>DebugFlags</c> directly: that class lives in the ClaudeForge
    /// APP, and this assembly is upstream of it. The app sets this; the shared code never reads
    /// the app.
    /// </para>
    /// </remarks>
    public static SchemaSourceOverride? ProcessSourceOverride { get; set; }

    /// <summary>
    /// A registry allowed to fetch schemas over HTTPS. <b>The production composition root.</b>
    /// </summary>
    /// <remarks>
    /// The client's own 15s timeout is a backstop only; <see cref="FetchTimeout"/> is what
    /// actually bounds a load, because this runs on the startup path.
    /// </remarks>
    public static SchemaRegistry CreateWithNetwork(SchemaSourceOverride? sourceOverride = null)
        => new(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, sourceOverride);

    /// <summary>
    /// Get the Claude Code settings schema root node.
    /// Uses the standard loading chain: memory → HTTPS (+ strip, + overlay) → bundled
    /// (+ strip, + overlay).
    /// </summary>
    /// <summary>
    /// The two products this registry knew by name before Phase 4. They are declared once,
    /// here, instead of being re-stated as a ternary at each of the five call sites that
    /// used to branch on an <c>isClaudeCode</c> flag.
    /// </summary>
    /// <remarks>
    /// This does not teach <c>AgentForge.Core</c> anything it did not already know — the
    /// URLs and file names were already hardcoded throughout this file. It concentrates
    /// that knowledge so the eventual product/shared split has one thing to move rather
    /// than five branches to find.
    /// </remarks>
    public static readonly ProductDescriptor ClaudeCodeProduct =
        new("claude-code", "Claude Code", ClaudeCodeSettingsSchemaUrl, "claude-code-settings.json",
            ArchiveFolder: "ClaudeCode");

    /// <inheritdoc cref="ClaudeCodeProduct"/>
    public static readonly ProductDescriptor ClaudeDesktopProduct =
        new("claude-desktop", "Claude Desktop", "bundled://claude-desktop-config", "claude-desktop-config.json",
            ArchiveFolder: "ClaudeDesktop");

    /// <summary>
    /// Get the settings schema root node for <paramref name="product"/>.
    /// Uses the standard loading chain: memory → HTTPS (+ strip, + overlay) → bundled
    /// (+ strip, + overlay).
    /// </summary>
    /// <exception cref="SchemaUnavailableException">
    /// No source could supply the schema. There is no empty-schema fallback — see that
    /// exception's remarks for why returning one would be worse than failing.
    /// </summary>
    public async Task<JsonSchemaNode> GetSettingsNodeAsync(
        ProductDescriptor product,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        JsonSchema schema = await GetSchemaAsync(product.SchemaUrl, product.SchemaFileName, ct);
        // Root is non-null for any successfully parsed schema, and there is no longer an
        // empty-schema fallback that could hand back a null one — so this throw now only fires
        // on a library contract break rather than on the ordinary offline path.
        return schema.Root
               ?? throw new InvalidOperationException("Loaded schema had a null root node.");
    }

    public Task<JsonSchemaNode> GetClaudeCodeSettingsNodeAsync(CancellationToken ct = default)
    {
        return GetSettingsNodeAsync(ClaudeCodeProduct, ct);
    }

    /// <summary>
    /// Get the Claude Desktop config schema root node.
    /// </summary>
    public Task<JsonSchemaNode> GetClaudeDesktopConfigNodeAsync(CancellationToken ct = default)
    {
        return GetSettingsNodeAsync(ClaudeDesktopProduct, ct);
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
        => ParseHookCommandVariants(TryReadBundledBytesMerged(cacheFileName));

    /// <summary>
    /// The hook command variants of the schema copy that actually loaded, not always the bundled one.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>Prefer this over the static overload wherever a registry instance is in hand.</b>
    /// The static reads the bundled resource, which was equivalent while bundled always won.
    /// Under network-first a fetched copy can be the one the tree was built from, and then the
    /// static describes a different document — silently, because both parse fine and neither
    /// is empty. The symptom would be an editor offering last release's shapes.
    /// </remarks>
    public IReadOnlyList<HookCommandVariantInfo> GetHookCommandVariantsFor(string cacheFileName)
        => ParseHookCommandVariants(MaterialisedOrBundled(cacheFileName));

    /// <summary>Shared body, over bytes that already have the overlay applied.</summary>
    internal static IReadOnlyList<HookCommandVariantInfo> ParseHookCommandVariants(byte[]? bytes)
    {
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
        => ParseHookEvents(TryReadBundledBytesMerged(cacheFileName));

    /// <summary>
    /// The hook events of the schema copy that actually loaded, not always the bundled one.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>Prefer this over the static overload wherever a registry instance is in hand.</b>
    /// The static reads the bundled resource, which was equivalent while bundled always won.
    /// Under network-first a fetched copy can be the one the tree was built from, and then the
    /// static describes a different document — silently, because both parse fine and neither
    /// is empty. The symptom would be an editor offering last release's shapes.
    /// </remarks>
    public IReadOnlyList<HookEventInfo> GetHookEventsFor(string cacheFileName)
        => ParseHookEvents(MaterialisedOrBundled(cacheFileName));

    /// <summary>Shared body, over bytes that already have the overlay applied.</summary>
    internal static IReadOnlyList<HookEventInfo> ParseHookEvents(byte[]? bytes)
    {
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
    /// Get a schema by URL. <b>Network first, bundled as the fallback.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Order: <b>memory cache → HTTPS fetch (+ strip, + overlay) → bundled resource
    /// (+ strip, + overlay)</b>. There is no disk cache and no empty fallback.
    /// </para>
    /// <para>
    /// ⭐ <b>The overlay and the external-<c>$ref</c> strip apply to whichever source wins</b>,
    /// which is what makes network-first safe. An earlier design welded both to the bundled
    /// reader, and the resulting "bundled must outrank the network or the overlay disappears"
    /// was an artifact of that welding rather than a requirement — it was then written down as
    /// a design principle. Materialising every source the same way removes the argument.
    /// </para>
    /// <para>
    /// ⛔⛔ <b>The strip is not optional and it is not only the script's job.</b> Upstream
    /// <c>opencode-config.json</c> types four <c>model</c> properties with a
    /// <c>models.dev</c> <c>$ref</c>. A fetched copy carrying one makes schema evaluation
    /// throw through <c>ValidateWorkspaceAsync</c> → <c>SaveAsync</c> for any config that sets
    /// a model — so the moment a fetch can win, the runtime has to strip exactly as
    /// <c>scripts/refresh-schema.{ps1,sh}</c> does. Bundled files are already stripped, so
    /// re-applying it there is a no-op; that is deliberate, because one code path for every
    /// source is what stops the two drifting.
    /// </para>
    /// <para>
    /// ⚠ <b>Startup blocks on this</b> — it is awaited from <c>AgentConfigClientCore.OpenAsync</c>.
    /// Hence <see cref="FetchTimeout"/>, which is deliberately much shorter than the
    /// <see cref="HttpClient"/>'s own 15s, and <see cref="_networkUnavailable"/>, which makes
    /// one failed probe stand for the whole process: an offline launch pays a single timeout
    /// rather than one per schema.
    /// </para>
    /// </remarks>
    /// <exception cref="SchemaUnavailableException">
    /// Neither the network nor a bundled resource could supply this schema. Deliberately a
    /// throw rather than an empty schema: an empty JSON Schema permits <em>everything</em>, so
    /// returning one turns save-validation into a no-op and reports success for a document
    /// nothing has checked. A section that cannot load its schema is broken, and the hosts
    /// already degrade a failed section visibly.
    /// </exception>
    public async Task<JsonSchema> GetSchemaAsync(string url, string cacheFileName, CancellationToken ct = default)
    {
        // 1. Memory cache — one fetch per URL per process, whatever the source.
        if (_memoryCache.TryGetValue(url, out JsonSchema? cached))
        {
            return cached;
        }

        // 2. HTTPS fetch.
        //
        // Plain http:// is refused outright: this copy now OUTRANKS the bundled one, so a
        // network intercept serving an attacker-crafted schema would decide what the editor
        // considers valid. https or nothing.
        if (_http is not null
            && !_networkUnavailable
            && _sourceOverride != SchemaSourceOverride.Bundled
            && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using CancellationTokenSource fetchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                fetchCts.CancelAfter(FetchTimeout);

                string json = await FetchWithRedirectAsync(url, fetchCts.Token).ConfigureAwait(false);
                JsonSchema fetched = Materialise(json, cacheFileName, SchemaSource.Fetched);
                _memoryCache[url] = fetched;
                Log.Information("[Schema] {File} loaded from {Url}", cacheFileName, url);
                return fetched;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                // Connectivity-shaped: assume the whole process is offline and stop probing.
                _networkUnavailable = true;
                Log.Information(ex, "[Schema] Network unavailable; using bundled schemas for this session");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Our own FetchTimeout fired, not the caller's cancellation. Same conclusion.
                _networkUnavailable = true;
                Log.Information(
                    "[Schema] Fetch of {Url} exceeded {Timeout}; using bundled schemas for this session",
                    url, FetchTimeout);
            }
            catch (JsonException ex)
            {
                // Reachable but serving nonsense. NOT a connectivity failure, so the latch
                // stays open — another schema on another host may still be fine.
                Log.Warning(ex, "[Schema] {Url} returned content that is not valid JSON", url);
            }
            catch (UnstrippableSchemaRefException ex)
            {
                // The served copy carries an external $ref the line-based strip cannot remove.
                // Fall back to bundled, which is known-stripped. Not a connectivity failure.
                Log.Warning(ex, "[Schema] {Url} carries an unstrippable external $ref; using bundled", url);
            }
        }
        else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(
                "[Schema] Refused to fetch over plain HTTP: {Url}. Use https:// or bundled://",
                url);
        }

        // ⛔ --schema-source fetched means the fetched copy or nothing. Falling back here would
        // produce a run that LOOKS like it exercised the network path and did not, which is
        // worse than a failure because the screenshot is indistinguishable from success.
        if (_sourceOverride == SchemaSourceOverride.Fetched)
        {
            throw new SchemaUnavailableException(url, cacheFileName);
        }

        // 3. Bundled resource. Always present for every product this repo ships, so in
        //    practice this is what runs whenever the network is slow, blocked or absent.
        byte[]? bundledBytes = TryReadBundledBytes(cacheFileName);
        if (bundledBytes != null)
        {
            JsonSchema schema = Materialise(
                Encoding.UTF8.GetString(bundledBytes), cacheFileName, SchemaSource.Bundled);
            _memoryCache[url] = schema;
            return schema;
        }

        // 4. Nothing could supply it. See the exception's remarks for why this is not "{}".
        throw new SchemaUnavailableException(url, cacheFileName);
    }

    /// <summary>
    /// Turn raw schema text from <em>any</em> source into the schema the editors see: strip
    /// external <c>$ref</c>s, apply the sibling overlay, parse.
    /// </summary>
    /// <remarks>
    /// The merged bytes are retained per file name so the metadata readers
    /// (<see cref="GetHookEventsFor"/>, <see cref="GetHookCommandVariantsFor"/>,
    /// <see cref="GetEnumDescriptionsFor"/>) can report on the copy that actually won rather
    /// than always on the bundled one. Before network-first they could read bundled and be
    /// right by construction; now that a fetch can win, reading bundled would describe a
    /// different document than the tree was built from.
    /// </remarks>
    private JsonSchema Materialise(string json, string cacheFileName, SchemaSource source)
    {
        string stripped = StripExternalRefs(json);

        // ⛔ The strip is LINE-based, so an external $ref sharing a line with its siblings
        // survives it. Upstream formats one key per line, which is the only reason this has
        // never mattered — so verify rather than trust the formatting. A surviving ref makes
        // evaluation throw on save, and refusing the source is recoverable where shipping it
        // is not: the fetch branch falls back to bundled, and a BUNDLED file in this state is
        // a build-time defect that should be loud.
        if (ExternalRefLine.IsMatch(stripped) || stripped.Contains("\"$ref\": \"http", StringComparison.Ordinal))
        {
            throw new UnstrippableSchemaRefException(cacheFileName);
        }

        byte[] merged = MergeOverlayOnto(Encoding.UTF8.GetBytes(stripped), cacheFileName);
        _materialisedBytes[cacheFileName] = merged;

        // Hash the MERGED bytes, not the raw source — see SchemaProvenance for why. Recorded
        // here rather than at the call sites so a future third source cannot forget to.
        _provenance[cacheFileName] = new SchemaProvenance(
            source,
            source == SchemaSource.Fetched ? DateTimeOffset.UtcNow : null,
            Convert.ToHexString(SHA256.HashData(merged)).ToLowerInvariant());

        return ParseSchema(Encoding.UTF8.GetString(merged));
    }

    /// <summary>
    /// Delete every line whose only content is an <c>http(s)</c> <c>$ref</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>This rule is duplicated in <c>scripts/refresh-schema.ps1</c> and its <c>.sh</c>
    /// twin, and the three must agree.</b> The scripts strip at refresh time so the committed
    /// file resolves offline; this strips at load time so a fetched copy does too. A change
    /// here needs the same change there — <c>SchemaStripParityTests</c> asserts this
    /// implementation against the same fixtures the scripts are checked with.
    /// </para>
    /// <para>
    /// Textual, not parse-and-reserialise, for the same reason the scripts are: it keeps the
    /// document byte-identical apart from the removed lines. Two cases, and reversing them
    /// yields invalid JSON — a <c>$ref</c> line ending in a comma has siblings after it, one
    /// that does not was the last key in its object and the PRECEDING line's comma must go too.
    /// </para>
    /// <para>
    /// Idempotent: text with no external <c>$ref</c> is returned unchanged, which is why a
    /// bundled file (already stripped by the script) can go through the same path.
    /// </para>
    /// </remarks>
    internal static string StripExternalRefs(string json)
    {
        if (!ExternalRefLine.IsMatch(json))
        {
            return json;
        }

        string[] lines = json.Split('\n');
        List<string> kept = new(lines.Length);

        foreach (string line in lines)
        {
            if (!ExternalRefLine.IsMatch(line))
            {
                kept.Add(line);
                continue;
            }

            if (line.TrimEnd().EndsWith(','))
            {
                continue;
            }

            for (int j = kept.Count - 1; j >= 0; j--)
            {
                if (kept[j].Trim().Length == 0)
                {
                    continue;
                }

                string trimmed = kept[j].TrimEnd();
                if (trimmed.EndsWith(','))
                {
                    kept[j] = trimmed[..^1];
                }

                break;
            }
        }

        return string.Join('\n', kept);
    }

    /// <summary>
    /// Force a re-load for one schema URL, bypassing the memory cache.
    /// </summary>
    /// <remarks>
    /// Under network-first this is a genuine re-fetch, which is what an in-app "check for
    /// schema updates" action needs. It also clears the offline latch, so an explicit user
    /// request retries the network even after an earlier probe failed — the latch exists to
    /// keep startup fast, not to refuse a deliberate retry.
    /// </remarks>
    public async Task<JsonSchema> RefreshAsync(string url, string cacheFileName, CancellationToken ct = default)
    {
        _memoryCache.TryRemove(url, out JsonSchema? _);
        _materialisedBytes.TryRemove(cacheFileName, out byte[]? _);
        _provenance.TryRemove(cacheFileName, out SchemaProvenance? _);
        _networkUnavailable = false;

        return await GetSchemaAsync(url, cacheFileName, ct).ConfigureAwait(false);
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

    private async Task<string> FetchWithRedirectAsync(string url, CancellationToken ct)
    {
        // Only ever reached from the fetch step, which checks _http first.
        HttpResponseMessage response = await _http!.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Where this registry's copy of a schema came from, or <see langword="null"/> if it has
    /// not loaded that schema yet.
    /// </summary>
    /// <remarks>
    /// ⚠ Null rather than a Bundled default. "Not loaded" and "loaded from the binary" are
    /// different facts, and a badge that renders the second when it means the first would
    /// state something nobody established.
    /// </remarks>
    public SchemaProvenance? ProvenanceFor(string cacheFileName)
        => _provenance.TryGetValue(cacheFileName, out SchemaProvenance? p) ? p : null;

    /// <summary>
    /// The merged bytes this registry last loaded for a schema, or the bundled copy when it
    /// has not loaded that schema yet.
    /// </summary>
    /// <remarks>
    /// The fallback matters: these readers are called from view-models that may run before, or
    /// entirely without, a <see cref="GetSchemaAsync"/> for that file. Bundled is the right
    /// answer then — it is what the tree would be built from too.
    /// </remarks>
    private byte[]? MaterialisedOrBundled(string cacheFileName)
        => _materialisedBytes.TryGetValue(cacheFileName, out byte[]? bytes)
            ? bytes
            : TryReadBundledBytesMerged(cacheFileName);

    private static byte[]? TryReadBundledBytes(string cacheFileName)
        => BundledResource.TryRead("Schemas", cacheFileName);

    /// <summary>
    /// The overlay sibling for a bundled schema file name: for <c>foo.json</c> it is
    /// <c>foo.overlay.json</c> (inject <c>.overlay</c> before the extension).
    /// </summary>
    /// <remarks>
    /// Extracted so the loader and <c>BackupEngine.BundleSchemas</c> derive the name the
    /// same way. They are a reader and a writer of the same convention: if only one of them
    /// changed, an archive would carry a base schema whose overlay had silently stopped
    /// being included, and the restore-time validation would quietly use un-overlaid rules.
    /// Nothing would fail loudly.
    /// </remarks>
    internal static string OverlayFileNameFor(string schemaFileName)
        => Path.GetFileNameWithoutExtension(schemaFileName)
           + ".overlay"
           + Path.GetExtension(schemaFileName);

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
        return baseBytes == null ? null : MergeOverlayOnto(baseBytes, cacheFileName);
    }

    /// <summary>
    /// Apply a schema's sibling <c>.overlay.json</c> to <paramref name="baseBytes"/>, whatever
    /// source those came from.
    /// </summary>
    /// <remarks>
    /// ⭐ Extracted from the bundled reader so a FETCHED base gets the overlay too. While the
    /// merge lived inside that reader, only the bundled copy could carry hand-curated
    /// additions — which is the whole reason bundled used to have to outrank the network.
    /// </remarks>
    internal static byte[] MergeOverlayOnto(byte[] baseBytes, string cacheFileName)
    {
        byte[]? overlayBytes = TryReadBundledBytes(OverlayFileNameFor(cacheFileName));
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
            // bytes feed into ParseSchema.
            string json = merged?.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
                          ?? Encoding.UTF8.GetString(baseBytes);
            return Encoding.UTF8.GetBytes(json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            Log.Warning(ex,
                "[Schema] Overlay {Overlay} could not be applied to {Base}; using base unchanged",
                OverlayFileNameFor(cacheFileName), cacheFileName);
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
    public Task<IReadOnlyList<SchemaValidationError>> ValidateWorkspaceAsync(
        SettingsWorkspace workspace,
        bool isClaudeCode,
        CancellationToken ct = default)
    {
        return ValidateWorkspaceAsync(workspace, isClaudeCode ? ClaudeCodeProduct : ClaudeDesktopProduct, ct);
    }

    /// <inheritdoc cref="ValidateWorkspaceAsync(SettingsWorkspace, bool, CancellationToken)"/>
    public async Task<IReadOnlyList<SchemaValidationError>> ValidateWorkspaceAsync(
        SettingsWorkspace workspace,
        ProductDescriptor product,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        JsonSchema schema = await GetSchemaAsync(product.SchemaUrl, product.SchemaFileName, ct);

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

        return await EnrichAllowedValuesAsync(errors, product, ct).ConfigureAwait(false);
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
        List<SchemaValidationError> errors, ProductDescriptor product, CancellationToken ct)
    {
        if (errors.Count == 0)
        {
            return errors;
        }

        Dictionary<string, IReadOnlyList<string>> enumsByPath;
        try
        {
            JsonSchemaNode rootNode = await GetSettingsNodeAsync(product, ct).ConfigureAwait(false);
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
    public Task<IReadOnlyList<SchemaValidationError>> ValidateAllWorkspaceAsync(
        SettingsWorkspace workspace,
        bool isClaudeCode,
        CancellationToken ct = default)
    {
        return ValidateAllWorkspaceAsync(workspace, isClaudeCode ? ClaudeCodeProduct : ClaudeDesktopProduct, ct);
    }

    /// <inheritdoc cref="ValidateAllWorkspaceAsync(SettingsWorkspace, bool, CancellationToken)"/>
    public async Task<IReadOnlyList<SchemaValidationError>> ValidateAllWorkspaceAsync(
        SettingsWorkspace workspace,
        ProductDescriptor product,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        JsonSchema schema = await GetSchemaAsync(product.SchemaUrl, product.SchemaFileName, ct);

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
        // Null on an offline registry, which is the default. See the constructor.
        _http?.Dispose();
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
/// Thrown by <c>IAgentConfigClient.SaveAsync</c> when one or more dirty
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