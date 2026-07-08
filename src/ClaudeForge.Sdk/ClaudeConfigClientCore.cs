using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.FileIO;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk.Backup;
using Bennewitz.Ninja.ClaudeForge.Sdk.Env;
using Bennewitz.Ninja.ClaudeForge.Sdk.Hooks;
using Bennewitz.Ninja.ClaudeForge.Sdk.Internal;
using Bennewitz.Ninja.ClaudeForge.Sdk.Marketplaces;
using Bennewitz.Ninja.ClaudeForge.Sdk.McpServers;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;
using Bennewitz.Ninja.ClaudeForge.Sdk.Models;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Plugins;
using Json.Schema;
using SchemaRegistry = Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaRegistry;

namespace Bennewitz.Ninja.ClaudeForge.Sdk;

/// <summary>
/// Shared implementation of <see cref="IClaudeConfigClient"/>. Both
/// <see cref="ClaudeCodeClient"/> and <see cref="ClaudeDesktopClient"/> derive
/// from this; they only need to override the file-discovery strategy and the
/// <see cref="IsClaudeCode"/> flag for schema validation.
/// </summary>
/// <remarks>
/// <para>
/// Public-but-abstract: consumers depend on <see cref="IClaudeConfigClient"/>
/// or one of the two concrete clients; this class itself is not directly
/// instantiable. It is exposed on the public surface only because the C#
/// language requires a base class to be at least as accessible as a public
/// derived class.
/// </para>
/// <para>
/// Thread-safety is implemented via a single <see cref="SemaphoreSlim"/> mutex.
/// All public read/write methods acquire it; async methods use
/// <see cref="SemaphoreSlim.WaitAsync(CancellationToken)"/>, sync methods use
/// <see cref="SemaphoreSlim.Wait()"/>. The design doc (§7) specifies a
/// <c>ReaderWriterLockSlim</c> for read parallelism — that optimization is
/// deferred until the workload justifies it (interactive GUI + small-N MCP
/// servers do not benefit measurably).
/// </para>
/// <para>
/// Implementation of the typed accessors (<see cref="Permissions"/>, etc.)
/// and <see cref="Backup"/> is intentionally still <see cref="NotImplementedException"/>
/// at this layer — those land in 4.3.4 and 4.3.5 respectively.
/// </para>
/// </remarks>
public abstract class ClaudeConfigClientCore : IClaudeConfigClient
{
    // ── Construction ───────────────────────────────────────────────────────

    private readonly SchemaRegistry _schemaRegistry;
    private readonly bool _ownsSchemaRegistry;

    /// <summary>
    /// Construct the shared core. The default constructor that consumers see on
    /// <see cref="ClaudeCodeClient"/> / <see cref="ClaudeDesktopClient"/> creates
    /// a fresh <see cref="SchemaRegistry"/>; tests inject one via the internal
    /// overload.
    /// </summary>
    /// <param name="defaultScope">Scope mutations target when not specified.</param>
    /// <param name="schemaRegistry">Optional injected schema registry (tests).</param>
    /// <param name="preLoadedWorkspace">
    /// Optional already-loaded <see cref="SettingsWorkspace"/>. When supplied,
    /// the client skips the disk load that <see cref="OpenAsync"/> would
    /// normally perform — the caller has already done it. Used by the GUI's
    /// SDK migration so its existing legacy
    /// workspace and the SDK client share a single underlying state object.
    /// After 4.3.7 fully migrates the GUI off the legacy workspace this
    /// parameter and its associated <c>InternalsVisibleTo("ClaudeForge")</c>
    /// grant become unused and should be removed.
    /// </param>
    protected ClaudeConfigClientCore(
        ConfigScope defaultScope,
        SchemaRegistry? schemaRegistry,
        SettingsWorkspace? preLoadedWorkspace = null)
    {
        DefaultScope = defaultScope;
        _schemaRegistry = schemaRegistry ?? new SchemaRegistry();
        _ownsSchemaRegistry = schemaRegistry is null;
        _workspace = preLoadedWorkspace;

        // Subscribe to the pre-loaded workspace's Changed event so SDK consumers
        // see EVERY mutation, including ones initiated outside the SDK (e.g. the
        // GUI's editor live-write loop that still calls _workspace.SetValue
        // directly). 4.3.7 deferred sub-step: the forwarder turns the SDK's
        // Changed event into the unified mutation feed for the workspace.
        if (preLoadedWorkspace is not null)
        {
            preLoadedWorkspace.Changed += OnWorkspaceChanged;
        }
    }

    /// <summary>
    /// Discover the set of config files this client is responsible for.
    /// Concrete clients implement this — Claude Code combines settings + mcp.json,
    /// Claude Desktop returns its single config file.
    /// </summary>
    protected abstract IReadOnlyList<DiscoveredFile> DiscoverFiles(string? projectRoot);

    /// <summary>
    /// Used by <see cref="ValidateAsync"/> to pick the right schema. Always
    /// <see langword="true"/> for Claude Code, <see langword="false"/> for Desktop.
    /// </summary>
    protected abstract bool IsClaudeCode { get; }

    // ── State ──────────────────────────────────────────────────────────────

    private readonly SemaphoreSlim _stateLock = new(1, 1);

    // ── Thread-reentrancy support for _stateLock ──────────────────────────
    //
    // deadlock hardening. SemaphoreSlim is non-reentrant by
    // design, but the SDK's mutation methods (SetValue, RemoveValue) call
    // _workspace.SetValue while holding _stateLock, and that synchronously
    // raises workspace.Changed. Any handler subscribed to workspace.Changed
    // (or to SDK.Changed via the forwarder) that calls back into SDK
    // accessors like GetEffective / GetScopeValue would hit a deadlock
    // because those accessors also acquire _stateLock.
    //
    // The single-editor live-write path was protected by `_selfWriting` in
    // SettingsGroupEditorViewModel; the bulk-save path was not — that bug
    // was patched, but any FUTURE workspace.Changed subscriber that reads
    // via SDK accessors would re-introduce the deadlock unless it knew to
    // mirror the same guard.
    //
    // Bulletproof fix: track the lock holder thread and re-entry depth,
    // letting the same thread skip re-acquisition. This mirrors Monitor's
    // ("lock { }") reentrant lock semantics. Subscribers no longer need to
    // know about _selfWriting-style guards — any synchronous callback
    // from inside the lock to another lock-acquiring method on the same
    // thread succeeds without blocking.
    //
    // Caveats:
    //   * Re-entrancy is SYNCHRONOUS-ONLY. If an async method awaits on
    //     a worker thread (TaskScheduler default), then resumes on a
    //     different thread, then re-enters via EnterStateLock — the helper
    //     sees no match and tries to acquire, which blocks (the original
    //     thread still holds the semaphore). None of the SDK's current
    //     async methods do nested locked calls across awaits, so this is
    //     not a problem in practice.
    //   * Per-instance state, NOT [ThreadStatic] — multiple SDK clients
    //     on the same thread (Code + Desktop in the GUI) are independent.
    //   * Dispose uses the same helpers (EnterStateLock / ExitStateLock)
    //     for symmetry. Disposal is not expected to be re-entered; the
    //     helpers behave identically to bare Wait/Release in that case.
    //     The final `_stateLock.Dispose()` call after exit is unchanged.
    //
    // See ClaudeConfigClientCoreReentrancyTests for the contract regression
    // tests.
    private int _lockHolderThreadId; // 0 = not held; otherwise managed thread id
    private int _lockReentryDepth; // mutated only while holding _stateLock

    private void EnterStateLock()
    {
        int tid = Environment.CurrentManagedThreadId;
        // Volatile read — the holder thread id is written by whichever
        // thread last acquired the lock; we only trust the value when it
        // matches our own thread id.
        if (Volatile.Read(ref _lockHolderThreadId) == tid)
        {
            _lockReentryDepth++;
            return;
        }

        _stateLock.Wait();
        _lockHolderThreadId = tid;
        _lockReentryDepth = 1;
    }

    private async Task EnterStateLockAsync(CancellationToken ct)
    {
        int tid = Environment.CurrentManagedThreadId;
        if (Volatile.Read(ref _lockHolderThreadId) == tid)
        {
            _lockReentryDepth++;
            return;
        }

        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
        // Re-read after the await — the resume may be on a different
        // thread than the call site, and we need to record the actual
        // thread that now holds the semaphore.
        _lockHolderThreadId = Environment.CurrentManagedThreadId;
        _lockReentryDepth = 1;
    }

    private void ExitStateLock()
    {
        if (--_lockReentryDepth == 0)
        {
            _lockHolderThreadId = 0;
            _stateLock.Release();
        }
    }

    private SettingsWorkspace? _workspace;

    private string? _projectRoot;

    // Populated by OpenAsync / ReloadAsync; read by SearchSchema. Written under
    // the state lock; read via a lock-and-snapshot to avoid holding the lock
    // during the CPU-bound search walk (see SearchSchema).
    private IReadOnlyList<SchemaNode>? _cachedSchemaNodes;

    private bool _autoSave;

    // Atomic via Interlocked. The first Dispose call swaps 0→1 and runs cleanup;
    // subsequent calls see the prior value 1 and short-circuit before touching
    // the already-disposed semaphore.
    private int _disposedFlag;

    // ── workspace.Changed → SDK.Changed forwarder (4.3.7 step 8) ────────────
    //
    // The workspace's Changed event fires synchronously inside SetValue /
    // RemoveValue, including writes initiated outside the SDK (the GUI editor
    // live-write loop still calls _workspace.SetValue directly during the
    // partial-migration period).
    //
    // The forwarder unifies all mutations onto the SDK's Changed event so
    // consumers subscribe in one place. SDK-initiated mutations are dedup'd
    // via _suppressForwarder: SetValue / RemoveValue set the flag while they
    // do the workspace mutation, then explicitly raise SDK Changed with the
    // dotted path AFTER releasing the lock. The forwarder skips while the
    // flag is set so consumers see exactly one event per write.
    //
    // [ThreadStatic] would not work — multiple SDK clients can share the same
    // workspace via FromExistingWorkspace, so the flag must be per-client.
    // The lock orders SDK-initiated writes; the editor live-write loop is
    // single-threaded by the GUI dispatcher, so no race between the flag
    // probe and the workspace event firing inside the same thread.
    //
    // dual-guard contract.
    // This flag pairs with `_selfWriting` in
    // `ClaudeForge.ViewModels.SettingsGroupEditorViewModel` to prevent the
    // SDK Changed → editor reload → SDK SetValue → SDK Changed infinite
    // loop. See the comment block on `_selfWriting` for the full contract.
    // If you change either flag's lifecycle, also review the other side
    // and re-run the regression tests in
    // `tests/ClaudeForge.Tests/ViewModels/SettingsGroupEditorViewModelTests`.
    private bool _suppressForwarder;

    /// <inheritdoc/>
    public ConfigScope DefaultScope { get; }

    /// <inheritdoc/>
    public IReadOnlyList<ConfigScope> EditableScopes
    {
        get
        {
            ThrowIfDisposed();
            EnterStateLock();
            try
            {
                if (_workspace is null)
                {
                    return [ConfigScope.User];
                }

                List<ConfigScope> list = _workspace.Documents
                                                   .Where(d => d.Scope != ConfigScope.Managed && !d.IsReadOnly)
                                                   .Select(d => d.Scope)
                                                   .Distinct()
                                                   .OrderBy(s => (int)s)
                                                   .ToList();

                return list.Count > 0 ? list : [ConfigScope.User];
            }
            finally
            {
                ExitStateLock();
            }
        }
    }

    /// <inheritdoc/>
    public bool HasUnsavedChanges
    {
        get
        {
            ThrowIfDisposed();
            EnterStateLock();
            try
            {
                return _workspace?.Documents.Any(d => d.HasActualChanges()) ?? false;
            }
            finally
            {
                ExitStateLock();
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 4.3.3 ships a setter that simply remembers the flag. The coalescing
    /// queue described in the design doc §7.3 is deferred to a follow-up
    /// commit; until then, mutations do NOT auto-write to disk even when
    /// <see cref="AutoSave"/> is <see langword="true"/>. Callers must invoke
    /// <see cref="SaveAsync(bool, CancellationToken)"/> explicitly.
    /// </remarks>
    public bool AutoSave
    {
        get
        {
            ThrowIfDisposed();
            return Volatile.Read(ref _autoSave);
        }
        set
        {
            ThrowIfDisposed();
            Volatile.Write(ref _autoSave, value);
        }
    }

    private IPermissionsAccessor? _permissionsAccessor;
    private IHooksAccessor? _hooksAccessor;
    private IMcpServersAccessor? _mcpServersAccessor;
    private IMarketplacesAccessor? _marketplacesAccessor;
    private IEnabledPluginsAccessor? _pluginsAccessor;
    private IEnvAccessor? _envAccessor;

    /// <inheritdoc/>
    public IPermissionsAccessor Permissions => _permissionsAccessor ??= new PermissionsAccessor(this);

    /// <inheritdoc/>
    public IHooksAccessor Hooks => _hooksAccessor ??= new HooksAccessor(this);

    /// <summary>
    /// Hook lifecycle events from the currently-loaded settings schema — each
    /// event's name plus its schema description (the <c>hooks</c> node's child
    /// properties). The fresh, schema-driven set the GUI's schema tree is built
    /// from too. Empty before <see cref="OpenAsync"/> or when the schema exposes no
    /// <c>hooks.properties</c>. Consumed by the Hooks accessor's <c>KnownEvents</c>
    /// so headless callers and the editor share one source of truth — including the
    /// descriptions, not just the names.
    /// </summary>
    internal IReadOnlyList<HookEventInfo> SchemaHookEvents()
    {
        SchemaNode? hooks = _cachedSchemaNodes?.FirstOrDefault(n =>
            string.Equals(n.Name, "hooks", StringComparison.Ordinal));
        if (hooks is not null)
        {
            return hooks.Properties.Select(p => new HookEventInfo(p.Name, p.Description)).ToList();
        }

        // No cached schema tree — the client was constructed via FromExistingWorkspace
        // (the GUI's path) and never ran OpenAsync, so _cachedSchemaNodes is null. Read the
        // event names + descriptions straight from the bundled schema (same source, same
        // descriptions) so KnownEvents — and thus the editor's per-event tooltips/labels —
        // stay populated regardless of how the client was built. Mirrors SchemaHookCommandVariants.
        return IsClaudeCode
            ? SchemaRegistry.GetHookEvents("claude-code-settings.json")
            : [];
    }

    /// <summary>
    /// Hook command variants from the settings schema's <c>$defs.hookCommand.anyOf</c> —
    /// each variant's <c>type</c> discriminator, description, and field descriptions. Read
    /// from the bundled merged schema JSON because the <c>anyOf</c> variants don't survive the
    /// flattened <see cref="SchemaNode"/> tree the GUI builds from (unlike <see cref="SchemaHookEvents"/>,
    /// which reads that tree); the bundled schema is the same source the tree derives from, so
    /// they stay consistent. Empty for non-Claude-Code clients — hooks are a Claude Code concept.
    /// Consumed by the Hooks accessor's <c>KnownCommandTypes</c> so headless callers and the editor
    /// share one source for the per-type picker text and per-field descriptions.
    /// </summary>
    internal IReadOnlyList<HookCommandVariantInfo> SchemaHookCommandVariants() =>
        IsClaudeCode
            ? SchemaRegistry.GetHookCommandVariants("claude-code-settings.json")
            : [];

    /// <inheritdoc/>
    public IMcpServersAccessor McpServers => _mcpServersAccessor ??= new McpServersAccessor(this);

    /// <inheritdoc/>
    public IMarketplacesAccessor Marketplaces => _marketplacesAccessor ??= new MarketplacesAccessor(this);

    /// <inheritdoc/>
    public IEnabledPluginsAccessor Plugins => _pluginsAccessor ??= new EnabledPluginsAccessor(this);

    /// <inheritdoc/>
    public IEnvAccessor Env => _envAccessor ??= new EnvAccessor(this);

    /// <inheritdoc/>
    public IModelCatalogAccessor Models => ModelCatalogProvider.Default;

    private IBackupClient? _backupClient;

    /// <inheritdoc/>
    public IBackupClient Backup => _backupClient ??= CreateBackupClient();

    /// <summary>
    /// Constructs the per-product <see cref="IBackupClient"/>. Subclasses
    /// override to set the appropriate Include flags on the underlying Core
    /// <c>BackupRequest</c>.
    /// </summary>
    protected abstract IBackupClient CreateBackupClient();

    /// <inheritdoc/>
    public event EventHandler<ClientChangedEventArgs>? Changed;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task OpenAsync(string? projectRoot, CancellationToken ct)
    {
        ThrowIfDisposed();

        // Discover and load OUTSIDE the lock — the load is async I/O. We then
        // briefly take the lock to publish the new workspace into the field.
        IReadOnlyList<DiscoveredFile> files = DiscoverFiles(projectRoot);
        SettingsWorkspace workspace = await ConfigFileLoader.LoadWorkspaceAsync(files, ct).ConfigureAwait(false);

        // Build schema node cache outside the lock. GetClaudeCode/DesktopSettingsNodeAsync
        // uses SchemaRegistry's memory cache after the first load — subsequent calls are
        // near-zero cost. SchemaTreeBuilder.BuildTopLevel is CPU-bound and fast.
        JsonSchemaNode schemaRoot = IsClaudeCode
            ? await _schemaRegistry.GetClaudeCodeSettingsNodeAsync(ct).ConfigureAwait(false)
            : await _schemaRegistry.GetClaudeDesktopConfigNodeAsync(ct).ConfigureAwait(false);
        IReadOnlyList<SchemaNode> freshNodes = SchemaTreeBuilder.BuildTopLevel(schemaRoot);

        await EnterStateLockAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            // Detach from any previously-loaded workspace's Changed event so
            // we never forward stale post-reload events.
            if (_workspace is not null)
            {
                _workspace.Changed -= OnWorkspaceChanged;
            }

            _workspace = workspace;
            _projectRoot = projectRoot;
            _cachedSchemaNodes = freshNodes;
            _workspace.Changed += OnWorkspaceChanged;
        }
        finally
        {
            ExitStateLock();
        }
    }

    /// <inheritdoc/>
    public async Task ReloadAsync(CancellationToken ct)
    {
        ThrowIfDisposed();

        // Capture projectRoot under lock, do I/O outside, then publish.
        string? projectRoot;
        await EnterStateLockAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureOpen();
            projectRoot = _projectRoot;
        }
        finally
        {
            ExitStateLock();
        }

        IReadOnlyList<DiscoveredFile> files = DiscoverFiles(projectRoot);
        SettingsWorkspace workspace = await ConfigFileLoader.LoadWorkspaceAsync(files, ct).ConfigureAwait(false);

        // Refresh schema node cache on reload (schema may have been updated on
        // disk since the last open). Memory-cached after first access, so cheap.
        JsonSchemaNode schemaRoot = IsClaudeCode
            ? await _schemaRegistry.GetClaudeCodeSettingsNodeAsync(ct).ConfigureAwait(false)
            : await _schemaRegistry.GetClaudeDesktopConfigNodeAsync(ct).ConfigureAwait(false);
        IReadOnlyList<SchemaNode> freshNodes = SchemaTreeBuilder.BuildTopLevel(schemaRoot);

        await EnterStateLockAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_workspace is not null)
            {
                _workspace.Changed -= OnWorkspaceChanged;
            }

            _workspace = workspace;
            _cachedSchemaNodes = freshNodes;
            _workspace.Changed += OnWorkspaceChanged;
        }
        finally
        {
            ExitStateLock();
        }

        Changed?.Invoke(this, new ClientChangedEventArgs(ClientChangeKind.Reloaded, null));
    }

    /// <summary>
    /// Forwards the underlying workspace's <c>Changed</c> event onto the
    /// SDK's <see cref="Changed"/> event so consumers see EVERY mutation —
    /// including ones initiated outside the SDK (e.g. the GUI editor
    /// live-write loop's direct <c>workspace.SetValue</c> calls during the
    /// partial-migration period).
    /// </summary>
    /// <remarks>
    /// SDK-initiated mutations skip this path: <see cref="SetValue{T}(string, T, ConfigScope)"/>
    /// and <see cref="RemoveValue"/> set <c>_suppressForwarder</c> while they
    /// do the workspace write, then raise <see cref="Changed"/> explicitly
    /// with the dotted path. Without the dedup the consumer would observe
    /// two events per SDK-initiated write (the workspace forwarder + the
    /// explicit raise), one with path info and one without.
    /// </remarks>
    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (_suppressForwarder)
        {
            return;
        }

        // Path info isn't available — the workspace event doesn't carry it.
        // Editor-direct writes get a path-less Mutation event; SDK-initiated
        // writes get a path-full one via the explicit raise paths below.
        Changed?.Invoke(this, new ClientChangedEventArgs(ClientChangeKind.Mutation, null));
    }

    /// <inheritdoc/>
    public Task SaveAsync(bool force, CancellationToken ct)
    {
        return SaveAsync(force, headerComment: null, ct);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(bool force, string? headerComment, CancellationToken ct)
    {
        ThrowIfDisposed();

        // Pre-save validation runs under no lock — it queries documents that
        // may still be mutating, but ValidateWorkspaceAsync iterates a snapshot
        // of dirty docs and reads their JsonObject roots; concurrent writes
        // would race against the read. A future hardening pass can take a
        // deep clone here for guaranteed isolation. For 4.3.3, the simpler
        // pattern is acceptable since the GUI single-threads its writes
        // anyway.
        if (!force)
        {
            IReadOnlyList<SchemaValidationError> errors = await ValidateAsync(ct).ConfigureAwait(false);
            if (errors.Count > 0)
            {
                throw new SchemaValidationException(errors);
            }
        }

        // Hold the lock for the duration of the save. Concurrent writes block
        // briefly; the atomic temp+rename inside ConfigFileLoader keeps each
        // file consistent even if cancelled.
        await EnterStateLockAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureOpen();
            await ConfigFileLoader.SaveDirtyAsync(_workspace!, headerComment, ct)
                                  .ConfigureAwait(false);
        }
        finally
        {
            ExitStateLock();
        }

        Changed?.Invoke(this, new ClientChangedEventArgs(ClientChangeKind.Saved, null));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SchemaValidationError>> ValidateAsync(CancellationToken ct)
    {
        ThrowIfDisposed();

        SettingsWorkspace ws;
        await EnterStateLockAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureOpen();
            ws = _workspace!;
        }
        finally
        {
            ExitStateLock();
        }

        // Validation runs without the state lock to avoid serialising long
        // schema fetches against ordinary writes. SchemaRegistry caches its
        // schema after the first load so repeat calls are cheap.
        // SchemaValidationError lives on Core now and the
        // SDK passes it through unchanged. Previously this method projected
        // each error into a flatter SDK-local record which lost the structural
        // InstancePath needed by the GUI's friendly-message formatter.
        return await _schemaRegistry.ValidateWorkspaceAsync(ws, IsClaudeCode, ct)
                                    .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SchemaValidationError>> ValidateAllAsync(CancellationToken ct)
    {
        ThrowIfDisposed();

        SettingsWorkspace ws;
        await EnterStateLockAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureOpen();
            ws = _workspace!;
        }
        finally
        {
            ExitStateLock();
        }

        // Same off-lock execution rationale as ValidateAsync above.
        // Routes through the Core's full-validation entry so pre-existing
        // (non-delta) errors surface — required for the post-reload banner.
        return await _schemaRegistry.ValidateAllWorkspaceAsync(ws, IsClaudeCode, ct)
                                    .ConfigureAwait(false);
    }

    // ── Generic escape hatch ───────────────────────────────────────────────

    /// <inheritdoc/>
    public T? GetEffective<T>(string path)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(path);

        EnterStateLock();
        try
        {
            ThrowIfDisposed();
            EnsureOpen();
            return JsonConversion.ConvertFromJsonNode<T>(GetEffectiveNodeLocked(path));
        }
        finally
        {
            ExitStateLock();
        }
    }

    // ── Locked primitives shared by the sync + async read/write methods ──────
    // Each assumes _stateLock is already held and the workspace is open. Keeping
    // the in-lock logic in one place means the async variants (which differ only
    // in how they ACQUIRE the lock) and the atomic SetValueIfChangedAsync reuse
    // exactly the same mutation/read semantics.

    /// <summary>Effective (merged) JSON node at <paramref name="path"/>. Lock held.</summary>
    private JsonNode? GetEffectiveNodeLocked(string path)
    {
        (string top, string? remainder) = SplitPath(path);
        JsonNode? node = _workspace!.GetLayeredValue(top).EffectiveValue;
        return remainder is not null ? ResolveByPath(node, remainder) : node;
    }

    /// <summary>
    /// Scope-specific JSON node at <paramref name="path"/> (no cross-scope merge) —
    /// the value as it exists at exactly <paramref name="scope"/>, or <c>null</c>
    /// when unset there. Lock held.
    /// </summary>
    private JsonNode? GetScopeNodeLocked(string path, ConfigScope scope)
    {
        (string top, string? remainder) = SplitPath(path);
        JsonNode? raw = ReadScopeKey(top, scope);
        return remainder is null ? raw : ResolveByPath(raw, remainder);
    }

    /// <summary>Write <paramref name="converted"/> at <paramref name="scope"/>. Lock held; raises NO Changed.</summary>
    private void ApplySetLocked(string path, JsonNode? converted, ConfigScope scope)
    {
        _suppressForwarder = true;
        try
        {
            (string top, string? remainder) = SplitPath(path);
            if (remainder is null)
            {
                _workspace!.SetValue(top, converted, scope);
            }
            else
            {
                JsonNode? existing = ReadScopeKey(top, scope);
                JsonObject mutated = existing is JsonObject obj ? (JsonObject)obj.DeepClone() : new JsonObject();
                SetNested(mutated, remainder, converted);
                _workspace!.SetValue(top, mutated, scope);
            }
        }
        finally
        {
            _suppressForwarder = false;
        }
    }

    /// <summary>
    /// Remove the value at <paramref name="path"/>/<paramref name="scope"/>. Lock held;
    /// raises NO Changed. Returns whether the caller should raise Changed — true for a
    /// top-level remove (preserves the prior always-notify behavior) and for a nested
    /// remove that actually changed something; false when a nested key was absent.
    /// </summary>
    private bool ApplyRemoveLocked(string path, ConfigScope scope)
    {
        _suppressForwarder = true;
        try
        {
            (string top, string? remainder) = SplitPath(path);
            if (remainder is null)
            {
                _workspace!.RemoveValue(top, scope);
                return true;
            }

            JsonNode? existing = ReadScopeKey(top, scope);
            if (existing is not JsonObject obj)
            {
                return false;
            }

            JsonObject mutated = (JsonObject)obj.DeepClone();
            if (!RemoveNested(mutated, remainder))
            {
                return false;
            }

            if (mutated.Count == 0)
            {
                _workspace!.RemoveValue(top, scope);
            }
            else
            {
                _workspace!.SetValue(top, mutated, scope);
            }

            return true;
        }
        finally
        {
            _suppressForwarder = false;
        }
    }

    /// <inheritdoc/>
    public void SetValue<T>(string path, T value)
    {
        SetValue(path, value, DefaultScope);
    }

    /// <inheritdoc/>
    public void SetValue<T>(string path, T value, ConfigScope scope)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(path);

        JsonNode? converted = JsonConversion.ConvertToJsonNode(value);

        EnterStateLock();
        try
        {
            ThrowIfDisposed();
            EnsureOpen();
            // ApplySetLocked suppresses the workspace-Changed forwarder while it
            // mutates, so the consumer sees exactly one Mutation event — the
            // path-full explicit raise below, not the path-less forwarder copy.
            ApplySetLocked(path, converted, scope);
        }
        finally
        {
            ExitStateLock();
        }

        Changed?.Invoke(this, new ClientChangedEventArgs(ClientChangeKind.Mutation, path));
    }

    /// <inheritdoc/>
    public void RemoveValue(string path, ConfigScope scope)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(path);

        bool mutated;
        EnterStateLock();
        try
        {
            ThrowIfDisposed();
            EnsureOpen();
            mutated = ApplyRemoveLocked(path, scope);
        }
        finally
        {
            ExitStateLock();
        }

        if (mutated)
        {
            Changed?.Invoke(this, new ClientChangedEventArgs(ClientChangeKind.Mutation, path));
        }
    }

    // ── Async read/write variants (genuinely async lock acquisition) ─────────
    // Same in-lock semantics as the sync methods (they share the *Locked helpers);
    // they differ only in acquiring the lock via EnterStateLockAsync(ct), so they
    // are non-blocking and honor cancellation. Changed is raised AFTER the lock is
    // released, matching the documented contract.

    /// <inheritdoc/>
    public async Task<T?> GetEffectiveAsync<T>(string path, CancellationToken ct)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(path);

        await EnterStateLockAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureOpen();
            return JsonConversion.ConvertFromJsonNode<T>(GetEffectiveNodeLocked(path));
        }
        finally
        {
            ExitStateLock();
        }
    }

    /// <inheritdoc/>
    public Task SetValueAsync<T>(string path, T value, CancellationToken ct)
        => SetValueAsync(path, value, DefaultScope, ct);

    /// <inheritdoc/>
    public async Task SetValueAsync<T>(string path, T value, ConfigScope scope, CancellationToken ct)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(path);

        JsonNode? converted = JsonConversion.ConvertToJsonNode(value);

        await EnterStateLockAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureOpen();
            ApplySetLocked(path, converted, scope);
        }
        finally
        {
            ExitStateLock();
        }

        Changed?.Invoke(this, new ClientChangedEventArgs(ClientChangeKind.Mutation, path));
    }

    /// <inheritdoc/>
    public async Task RemoveValueAsync(string path, ConfigScope scope, CancellationToken ct)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(path);

        bool mutated;
        await EnterStateLockAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureOpen();
            mutated = ApplyRemoveLocked(path, scope);
        }
        finally
        {
            ExitStateLock();
        }

        if (mutated)
        {
            Changed?.Invoke(this, new ClientChangedEventArgs(ClientChangeKind.Mutation, path));
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SetValueIfChangedAsync<T>(string path, T value, ConfigScope scope, CancellationToken ct)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(path);

        JsonNode? converted = JsonConversion.ConvertToJsonNode(value);

        bool wrote;
        await EnterStateLockAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureOpen();
            // Atomic compare-and-set under a SINGLE lock acquisition: write only
            // when it would change the value AT THE TARGET SCOPE. The compare basis
            // (scope-specific) and the write domain (scope-specific) MUST agree —
            // comparing the cross-scope EFFECTIVE value here would let a shadowed
            // scope both (a) write with no scope change suppressed and (b) skip a
            // legitimate explicit pin, the very ghost-change class this guards. This
            // mirrors SettingsWorkspace.SetValue's own scope-specific no-op guard and
            // eliminates the read-then-write (TOCTOU) race of a separate read + set.
            // DeepEquals treats two absent/null nodes as equal, so re-asserting the
            // value already present at this scope is a no-op.
            if (JsonNode.DeepEquals(GetScopeNodeLocked(path, scope), converted))
            {
                wrote = false;
            }
            else
            {
                ApplySetLocked(path, converted, scope);
                wrote = true;
            }
        }
        finally
        {
            ExitStateLock();
        }

        if (wrote)
        {
            Changed?.Invoke(this, new ClientChangedEventArgs(ClientChangeKind.Mutation, path));
        }

        return wrote;
    }

    // ── Schema search ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<SchemaSearchResult> SearchSchema(string query, int maxResults = 50)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        // Snapshot the node list reference under the lock so the walk runs
        // without holding the lock (could block long-running searches against
        // concurrent SetValue calls).
        IReadOnlyList<SchemaNode>? nodes;
        EnterStateLock();
        try
        {
            nodes = _cachedSchemaNodes;
        }
        finally
        {
            ExitStateLock();
        }

        if (nodes is null)
        {
            return [];
        }

        // Collect all matches with sort-key metadata.  Sorting happens after
        // collection so the cap is applied to the ranked list, not the raw
        // traversal order.
        List<(SchemaSearchResult Result, bool IsExact, int SectionPriority, int Position)> matches = new();
        int position = 0;
        foreach (SchemaNode node in FlattenSchemaNodesForSearch(nodes))
        {
            string name = node.Name ?? string.Empty;
            string title = node.Title ?? name;
            string desc = node.Description ?? string.Empty;
            string path = node.JsonPath ?? string.Empty;

            if (!name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !desc.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !path.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                position++;
                continue;
            }

            matches.Add((
                new SchemaSearchResult(path, name, title, desc, BuildSearchSnippet(desc, query)),
                IsExact: path.Equals(query, StringComparison.OrdinalIgnoreCase),
                SectionPriority: GetSearchSectionPriority(path),
                Position: position
            ));
            position++;
        }

        // Sort: (1) exact path match wins, (2) permissions before hooks always,
        // (3) schema depth-first traversal order as stable tiebreaker.
        matches.Sort((a, b) =>
        {
            int cmp = b.IsExact.CompareTo(a.IsExact); // desc: exact first
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = a.SectionPriority.CompareTo(b.SectionPriority); // asc: permissions(0) < hooks(1) < other(2)
            if (cmp != 0)
            {
                return cmp;
            }

            return a.Position.CompareTo(b.Position); // asc: schema order
        });

        int count = Math.Min(matches.Count, maxResults);
        List<SchemaSearchResult> results = new(count);
        for (int i = 0; i < count; i++)
        {
            results.Add(matches[i].Result);
        }

        return results;
    }

    /// <summary>
    /// Returns the section priority used to order search results:
    /// permissions (0) → hooks (1) → mcpServers (2) → everything else (3).
    /// </summary>
    private static int GetSearchSectionPriority(string jsonPath)
    {
        return jsonPath.StartsWith("permissions", StringComparison.OrdinalIgnoreCase) ? 0 :
            jsonPath.StartsWith("hooks", StringComparison.OrdinalIgnoreCase) ? 1 :
            jsonPath.StartsWith("mcpServers", StringComparison.OrdinalIgnoreCase) ? 2 : 3;
    }

    private static IEnumerable<SchemaNode> FlattenSchemaNodesForSearch(IEnumerable<SchemaNode> nodes)
    {
        foreach (SchemaNode node in nodes)
        {
            yield return node;
            foreach (SchemaNode descendant in FlattenSchemaNodesForSearch(node.Properties))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Build a short snippet around the first occurrence of
    /// <paramref name="query"/> in <paramref name="text"/>.
    /// Mirrors <c>SearchViewModel.BuildSnippet</c> — both must stay in sync
    /// (see <c>src/ClaudeForge/ViewModels/SearchViewModel.cs</c>).
    /// </summary>
    private static string BuildSearchSnippet(string text, string query, int maxLen = 70)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        int idx = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return text.Length <= maxLen ? text : text[..maxLen] + "…";
        }

        int start = Math.Max(0, idx - 20);
        int end = Math.Min(text.Length, idx + query.Length + 30);
        string snip = text[start..end];
        if (start > 0)
        {
            snip = "…" + snip;
        }

        if (end < text.Length)
        {
            snip += "…";
        }

        return snip;
    }

    // ── Disposal ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        // Atomic 0→1 swap so a concurrent second Dispose call observes the
        // prior value 1 and short-circuits BEFORE attempting to acquire an
        // already-disposed semaphore. The lock is taken only on the winner
        // path to flush in-flight state under the same critical section as
        // ordinary mutations.
        if (Interlocked.Exchange(ref _disposedFlag, 1) == 1)
        {
            return;
        }

        EnterStateLock();
        try
        {
            // Detach the workspace-Changed forwarder before releasing the
            // workspace reference so a late-firing event doesn't reach a
            // disposed client.
            if (_workspace is not null)
            {
                _workspace.Changed -= OnWorkspaceChanged;
            }

            _workspace = null;
        }
        finally
        {
            ExitStateLock();
        }

        _stateLock.Dispose();
        if (_ownsSchemaRegistry)
        {
            _schemaRegistry.Dispose();
        }
    }

    // ── Internal helper for GUI dirty-doc diagnostics (4.3.7 step 9) ──────

    /// <summary>
    /// Snapshot of every dirty workspace document — used by the GUI's
    /// save-confirmation dialog, change-log, and pre-save B4Forge backup
    /// helpers. Held under the state lock so the snapshot is internally
    /// consistent (no torn read mid-write); each document's
    /// <c>BaselineRoot</c> and <c>CurrentRoot</c> are deep-cloned so the
    /// caller can iterate them after the lock is released without racing
    /// against subsequent mutations.
    /// </summary>
    /// <summary>
    /// Compute the merged effective JSON across all loaded scopes — a
    /// fresh deep clone owned by the caller. Returns an empty object when
    /// no workspace is loaded.
    /// </summary>
    /// <remarks>
    /// Internal-only because the return type is a <see cref="JsonObject"/>;
    /// see <see cref="DirtyDocumentSnapshot"/> for the rationale on
    /// public-surface promotion. Used by the GUI's
    /// <c>EffectiveSettingsViewModel</c> for the JSON-preview pane.
    /// </remarks>
    internal JsonObject ComputeEffectiveSnapshot()
    {
        ThrowIfDisposed();
        EnterStateLock();
        try
        {
            ThrowIfDisposed();
            return _workspace?.ComputeEffective() ?? new JsonObject();
        }
        finally
        {
            ExitStateLock();
        }
    }

    /// <summary>
    /// All top-level keys with an explicit value at any loaded scope.
    /// Empty when the workspace is not loaded. Used by the GUI's
    /// effective-settings table.
    /// </summary>
    internal IReadOnlyList<string> AllDefinedKeysSnapshot()
    {
        ThrowIfDisposed();
        EnterStateLock();
        try
        {
            ThrowIfDisposed();
            return _workspace?.AllDefinedKeys().ToList() ?? new List<string>();
        }
        finally
        {
            ExitStateLock();
        }
    }

    /// <summary>
    /// Internal hook for the GUI's effective-settings table to read the
    /// per-scope view of one key. Returns an empty
    /// <see cref="LayeredValue"/> when the workspace is not loaded.
    /// </summary>
    internal LayeredValue GetLayeredValueSnapshot(string key)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(key);
        EnterStateLock();
        try
        {
            ThrowIfDisposed();
            return _workspace?.GetLayeredValue(key) ?? new LayeredValue(key, []);
        }
        finally
        {
            ExitStateLock();
        }
    }

    /// <summary>
    /// Direct access to the underlying <see cref="SettingsWorkspace"/>.
    /// Returns <see langword="null"/> until <see cref="OpenAsync"/> populates
    /// it. Internal-only; the GUI uses this so its
    /// <c>SettingsGroupEditorViewModel</c> + factory chain can keep their
    /// <c>workspace.GetLayeredValue</c> read paths during the partial
    /// migration without MWVM holding a duplicate <c>_workspace</c> field.
    /// </summary>
    /// <remarks>
    /// Acquires the state lock briefly to publish the reference safely.
    /// Once the SettingsGroupEditorViewModel + Object/leaf editor read
    /// paths fully migrate to <see cref="GetLayeredValueSnapshot"/> this
    /// accessor can be removed.
    /// </remarks>
    internal SettingsWorkspace? WorkspaceForGui
    {
        get
        {
            ThrowIfDisposed();
            EnterStateLock();
            try
            {
                ThrowIfDisposed();
                return _workspace;
            }
            finally
            {
                ExitStateLock();
            }
        }
    }

    /// <remarks>
    /// Kept <c>internal</c> — see <see cref="DirtyDocumentSnapshot"/> for
    /// the rationale. Returns an empty list when the workspace has not yet
    /// been loaded.
    /// </remarks>
    internal IReadOnlyList<DirtyDocumentSnapshot> SnapshotDirtyDocuments()
    {
        ThrowIfDisposed();
        EnterStateLock();
        try
        {
            ThrowIfDisposed();
            if (_workspace is null)
            {
                return Array.Empty<DirtyDocumentSnapshot>();
            }

            List<DirtyDocumentSnapshot> result = new();
            foreach (SettingsDocument doc in _workspace.DirtyDocuments())
            {
                // BaselineRoot is non-null on a loaded document — the workspace
                // populates it during ConfigFileLoader.LoadWorkspaceAsync. The
                // null-forgiving operator avoids a CS8602 warning here without
                // adding a runtime guard that would mask a real bug if the
                // invariant ever broke.
                result.Add(new DirtyDocumentSnapshot(
                    FilePath: doc.FilePath,
                    Scope: doc.Scope,
                    BaselineRoot: (JsonObject)doc.BaselineRoot!.DeepClone(),
                    CurrentRoot: (JsonObject)doc.Root.DeepClone()));
            }

            return result;
        }
        finally
        {
            ExitStateLock();
        }
    }

    // ── Internal helpers for typed accessors (4.3.4) ──────────────────────

    /// <summary>
    /// Reads the JSON value at <paramref name="path"/> as it exists at exactly
    /// <paramref name="scope"/>. Unlike <see cref="GetEffective{T}"/>, this does
    /// NOT merge across scopes — it returns the raw value at the requested
    /// scope, or <see langword="null"/> when the path is unset there.
    /// </summary>
    /// <remarks>
    /// Used by the typed accessors (Permissions, Hooks, McpServers, etc.) to
    /// read the slice of state they're about to mutate. The Core workspace
    /// stores scopes independently; mutations target a single scope, so
    /// add/remove operations need to read THAT scope's current array, modify
    /// it, and write the whole array back.
    /// </remarks>
    internal JsonNode? GetScopeValue(string path, ConfigScope scope)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(path);

        EnterStateLock();
        try
        {
            ThrowIfDisposed();
            EnsureOpen();
            return GetScopeNodeLocked(path, scope)?.DeepClone();
        }
        finally
        {
            ExitStateLock();
        }
    }

    /// <summary>
    /// Reads the effective (merged) value at <paramref name="path"/> as a
    /// raw <see cref="JsonNode"/>. Used by accessors when they need the merged
    /// view across all scopes (e.g. for <c>All</c> snapshots).
    /// </summary>
    internal JsonNode? GetEffectiveNode(string path)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(path);

        EnterStateLock();
        try
        {
            ThrowIfDisposed();
            EnsureOpen();
            (string top, string? remainder) = SplitPath(path);
            LayeredValue layered = _workspace!.GetLayeredValue(top);
            JsonNode? node = layered.EffectiveValue;
            if (remainder is not null)
            {
                node = ResolveByPath(node, remainder);
            }

            return node?.DeepClone();
        }
        finally
        {
            ExitStateLock();
        }
    }

    /// <summary>
    /// Raises <see cref="Changed"/> with <see cref="ClientChangeKind.Mutation"/>.
    /// Accessors call this AFTER their internal SetValue/RemoveValue calls
    /// (which the public methods on this class already locked). Subscribers
    /// see consistent state because the mutation has fully committed.
    /// </summary>
    internal void RaiseChangedFromAccessor(string? path)
    {
        Changed?.Invoke(this, new ClientChangedEventArgs(ClientChangeKind.Mutation, path));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposedFlag) != 0)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }

    private void EnsureOpen()
    {
        if (_workspace is null)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} has not been opened. Call OpenAsync(...) before reading or mutating state.");
        }
    }

    // ToCoreScope translation removed in 4.3.7 step 5: SDK now uses
    // ClaudeForge.Core.Settings.ConfigScope directly so the SDK ↔ Core
    // boundary is identity. Previously the SDK carried its own parallel
    // enum and forced a switch on every mutation.

    private JsonNode? ReadScopeKey(string topKey, ConfigScope scope)
    {
        SettingsDocument? doc = _workspace!.Documents.FirstOrDefault(d => d.Scope == scope);
        if (doc is null)
        {
            return null;
        }

        return doc.Root.TryGetPropertyValue(topKey, out JsonNode? v) ? v : null;
    }

    /// <summary>
    /// Splits <paramref name="path"/> at the first <c>.</c> separator. Top-level
    /// paths return <c>(path, null)</c>. Dotted paths return the segment-before
    /// and the unconsumed remainder for nested resolution.
    /// </summary>
    private static (string Top, string? Remainder) SplitPath(string path)
    {
        int dot = path.IndexOf('.');
        return dot < 0 ? (path, null) : (path[..dot], path[(dot + 1)..]);
    }

    private static JsonNode? ResolveByPath(JsonNode? root, string remainder)
    {
        JsonNode? node = root;
        foreach (string segment in remainder.Split('.'))
        {
            if (node is not JsonObject obj)
            {
                return null;
            }

            if (!obj.TryGetPropertyValue(segment, out node))
            {
                return null;
            }
        }

        return node;
    }

    private static void SetNested(JsonObject root, string remainder, JsonNode? value)
    {
        string[] segments = remainder.Split('.');
        JsonObject current = root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is JsonObject child)
            {
                current = child;
            }
            else
            {
                JsonObject fresh = new();
                current[segments[i]] = fresh;
                current = fresh;
            }
        }

        current[segments[^1]] = value;
    }

    private static bool RemoveNested(JsonObject root, string remainder)
    {
        string[] segments = remainder.Split('.');
        JsonObject current = root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JsonObject child)
            {
                return false;
            }

            current = child;
        }

        return current.Remove(segments[^1]);
    }

    // ── Memory & footprint (Phase 5) ──────────────────────────────────────
    //
    // Tier 1 (user-authored memory) and Tier 2 (behavioural footprint) both
    // anchor on ~/.claude/ regardless of which product is being managed.
    // Default implementations live here; ClaudeDesktopClient overrides
    // SnapshotUserMemoryFiles to return empty so the Desktop section of the
    // Memory page can render a "no equivalent surface" explainer panel
    // without binding to a phantom Tier 1 inventory.
    //
    // The footprint service is shared between products — the same
    // ~/.claude/projects, history.jsonl, etc. files are populated by the
    // CLI regardless of whether the user also runs Desktop.

    private FootprintService? _footprintService;

    /// <summary>
    /// Cached <see cref="FootprintService"/>. Created on first access using
    /// the production filesystem (<c>RealBackupFileSystem.Instance</c>);
    /// tests can swap it via the protected setter.
    /// </summary>
    protected FootprintService FootprintService
    {
        get => _footprintService ??= new FootprintService();
        set => _footprintService = value;
    }

    /// <inheritdoc/>
    public virtual IReadOnlyList<UserMemoryFile> SnapshotUserMemoryFiles(string? projectRoot = null)
    {
        // Pull the project root from the open workspace if the caller did
        // not supply one. Field is set by OpenAsync; null when no project
        // is open.
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            projectRoot = _projectRoot;
        }

        return UserMemoryService.SnapshotFiles(projectRoot);
    }

    /// <inheritdoc/>
    public Task<string?> ReadMemoryFileAsync(string absolutePath, CancellationToken ct)
    {
        return UserMemoryService.ReadAsync(absolutePath, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FootprintCategoryStats>> GetFootprintStatsAsync(CancellationToken ct)
    {
        return FootprintService.GetStatsAsync(ct);
    }

    /// <inheritdoc/>
    public Task DeleteFootprintCategoryAsync(FootprintCategory category, CancellationToken ct)
    {
        return FootprintService.DeleteAsync(category, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ProjectTranscriptStats>> GetProjectTranscriptStatsAsync(CancellationToken ct)
    {
        return FootprintService.GetProjectTranscriptStatsAsync(ct);
    }

    /// <inheritdoc/>
    public Task DeleteProjectTranscriptsAsync(string mangledName, CancellationToken ct)
    {
        return FootprintService.DeleteProjectTranscriptsAsync(mangledName, ct);
    }
}