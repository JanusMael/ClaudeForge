using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk.Backup;
using Bennewitz.Ninja.ClaudeForge.Sdk.Env;
using Bennewitz.Ninja.ClaudeForge.Sdk.Hooks;
using Bennewitz.Ninja.ClaudeForge.Sdk.Marketplaces;
using Bennewitz.Ninja.ClaudeForge.Sdk.McpServers;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Plugins;

namespace Bennewitz.Ninja.ClaudeForge.Sdk;

/// <summary>
/// Avalonia-independent client for reading, writing, backing up, and
/// restoring Claude configuration. Two concrete implementations:
/// <see cref="ClaudeCodeClient"/> and <see cref="ClaudeDesktopClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread-safe.</b> A single client instance can be used from multiple
/// threads concurrently — MCP server authors do not need to wrap their own
/// locks. Workspace state is guarded internally by a reader-writer lock;
/// reads (<see cref="GetEffective{T}"/>, accessor reads) acquire the read
/// lock and capture a lazy snapshot, writes (<see cref="SetValue{T}(string, T)"/>,
/// <see cref="RemoveValue"/>, accessor mutations, <see cref="ReloadAsync"/>,
/// <see cref="SaveAsync(bool, CancellationToken)"/>) acquire the write lock.
/// </para>
/// <para>
/// <b>Event-handler contract.</b> The <see cref="Changed"/> event is raised
/// AFTER the lock is released, so handlers see consistent state. Subscribers
/// MUST NOT call back into the SDK synchronously from inside a handler — queue
/// the work on a different thread (UI dispatcher, <c>Task.Run</c>, etc.) and
/// call SDK methods from there. Synchronous re-entrancy is undefined behaviour
/// and may deadlock.
/// </para>
/// <para>
/// <b>Cancellation.</b> Every async method takes a <i>required</i>
/// <see cref="CancellationToken"/> — no defaulted parameters. Pass
/// <see cref="CancellationToken.None"/> only when the call site genuinely
/// cannot be cancelled. Cancellation aborts cleanly with no partial state on
/// disk (atomic temp+rename) and no mid-mutation in memory (the lock is
/// either acquired and the mutation commits, or it is not and nothing
/// happens). <see cref="OperationCanceledException"/> propagates to the
/// awaiter.
/// </para>
/// <para>
/// <b>Disposal.</b> <see cref="IDisposable.Dispose"/> aborts any in-flight
/// save, drains the AutoSave queue, and releases collaborators. Every public
/// method throws <see cref="ObjectDisposedException"/> after disposal.
/// </para>
/// </remarks>
public interface IClaudeConfigClient : IDisposable
{
    // ── Lifecycle ──────────────────────────────────────────────────────────

    /// <summary>Discover and load the workspace from disk.</summary>
    /// <param name="projectRoot">
    /// Optional project root to add Project / Local scopes for. <c>null</c>
    /// means "User-only" mode.
    /// </param>
    /// <param name="ct">
    /// Required. Pass <see cref="CancellationToken.None"/> only when the call
    /// site genuinely cannot be cancelled (e.g. fire-and-forget startup hooks).
    /// </param>
    Task OpenAsync(string? projectRoot, CancellationToken ct);

    /// <summary>Reload the workspace from disk, discarding any unsaved edits.</summary>
    Task ReloadAsync(CancellationToken ct);

    /// <summary>Persist all dirty documents to disk atomically.</summary>
    /// <param name="force">
    /// When <see langword="true"/>, skip pre-save schema validation.
    /// Default <see langword="false"/> throws <see cref="SchemaValidationException"/>
    /// if any dirty document violates the schema.
    /// </param>
    /// <param name="ct">Required cancellation token. See class remarks.</param>
    Task SaveAsync(bool force, CancellationToken ct);

    /// <summary>Persist all dirty documents with a custom header comment.</summary>
    /// <param name="force">
    /// When <see langword="true"/>, skip pre-save schema validation.
    /// </param>
    /// <param name="headerComment">
    /// Optional header line written above the JSON document — e.g. a "saved
    /// by ClaudeForge GUI v1.2" stamp. <see langword="null"/> emits no header.
    /// </param>
    /// <param name="ct">Required cancellation token. See class remarks.</param>
    /// <remarks>
    /// 4.3.7 step 6: added so the GUI can keep its provenance stamp while
    /// routing saves through the SDK. Headless consumers should prefer the
    /// <see cref="SaveAsync(bool, CancellationToken)"/> overload — they have
    /// no equivalent need for a "saved by" header.
    /// </remarks>
    Task SaveAsync(bool force, string? headerComment, CancellationToken ct);

    // ── State ──────────────────────────────────────────────────────────────

    /// <summary><see langword="true"/> when there are unsaved in-memory changes.</summary>
    bool HasUnsavedChanges { get; }

    /// <summary>
    /// When <see langword="true"/>, every <see cref="SetValue{T}(string, T)"/> /
    /// accessor mutation immediately writes to disk via a coalescing queue
    /// (see threading contract). Default <see langword="false"/> — explicit
    /// <see cref="SaveAsync(bool, CancellationToken)"/> required (matches GUI behavior).
    /// </summary>
    bool AutoSave { get; set; }

    /// <summary>
    /// Default scope for accessor mutations and unscoped
    /// <see cref="SetValue{T}(string, T)"/> calls. Configured at construction
    /// time. Per-call overrides go through the explicit-scope
    /// <see cref="SetValue{T}(string, T, ConfigScope)"/> /
    /// <see cref="RemoveValue"/> overloads.
    /// </summary>
    ConfigScope DefaultScope { get; }

    /// <summary>
    /// Scopes that have a writable document loaded into the workspace,
    /// ordered from widest (User) to narrowest (Local).
    /// <see cref="ConfigScope.Managed"/> is always excluded — managed
    /// (org-policy) settings are read-only by definition.
    /// </summary>
    /// <remarks>
    /// 4.3.7 step 7: GUI editors expose a scope-selector dropdown that must
    /// only offer scopes the user can actually write to. Without a project
    /// open, the workspace contains only User (and optionally Managed)
    /// documents — surfacing Project / Local in the dropdown would throw
    /// "no document loaded for scope X" on the first edit attempt. Always
    /// returns at least <see cref="ConfigScope.User"/> so the UI has a
    /// sensible default even before <see cref="OpenAsync"/> populates the
    /// workspace.
    /// </remarks>
    IReadOnlyList<ConfigScope> EditableScopes { get; }

    // ── Strongly-typed accessors ───────────────────────────────────────────

    /// <summary>Permissions accessor — Allow/Deny/Ask lists and DefaultMode.</summary>
    IPermissionsAccessor Permissions { get; }

    /// <summary>Hooks accessor — pre/post tool-use hooks.</summary>
    IHooksAccessor Hooks { get; }

    /// <summary>MCP servers accessor — typed servers keyed by name.</summary>
    IMcpServersAccessor McpServers { get; }

    /// <summary>Marketplaces accessor — typed marketplace entries.</summary>
    IMarketplacesAccessor Marketplaces { get; }

    /// <summary>Enabled plugins accessor.</summary>
    IEnabledPluginsAccessor Plugins { get; }

    /// <summary>
    /// Environment-variable accessor — typed reads / writes for the
    /// <c>env</c> map under settings.json.  Includes convenience
    /// properties for the well-known high-importance keys defined in
    /// <see cref="EnvVarKey"/> (<c>MAX_THINKING_TOKENS</c>,
    /// <c>CLAUDE_CODE_MAX_OUTPUT_TOKENS</c>, etc.).  The OS-level env
    /// var surface (Windows registry, shell profiles) is owned by the
    /// GUI's <c>EnvironmentEditorViewModel</c> + <c>IEnvironmentProvider</c>;
    /// this accessor stays focused on the persisted-config slice so
    /// non-GUI consumers can use it cleanly.
    /// </summary>
    IEnvAccessor Env { get; }

    // ── Generic escape hatch ───────────────────────────────────────────────

    /// <summary>
    /// Returns the effective (merged) value at <paramref name="path"/>
    /// deserialized to <typeparamref name="T"/>. Returns <c>default(T)</c>
    /// when the path is unset across all scopes.
    /// </summary>
    /// <param name="path">Dotted JSON path, e.g. <c>"model"</c> or
    /// <c>"permissions.defaultMode"</c>.</param>
    T? GetEffective<T>(string path);

    /// <summary>Set a value at <see cref="DefaultScope"/>.</summary>
    void SetValue<T>(string path, T value);

    /// <summary>Set a value at the specified scope (overrides
    /// <see cref="DefaultScope"/>).</summary>
    void SetValue<T>(string path, T value, ConfigScope scope);

    /// <summary>Remove a value at the specified scope.</summary>
    void RemoveValue(string path, ConfigScope scope);

    // ── Backup / restore ───────────────────────────────────────────────────

    /// <summary>
    /// Backup / restore client. See <see cref="IBackupClient"/> for the full
    /// surface; the same threading and cancellation contracts apply.
    /// </summary>
    IBackupClient Backup { get; }

    // ── Schema search ──────────────────────────────────────────────────────

    /// <summary>
    /// Search all schema properties whose name, title, description, or dotted
    /// JSON path contain <paramref name="query"/> (case-insensitive).
    /// Returns an empty list when the client has not yet been opened via
    /// <see cref="OpenAsync"/> (schema nodes are cached on first open).
    /// </summary>
    /// <param name="query">
    /// The search string. Results are meaningful for queries of two or more
    /// characters; single-character queries may return too many results to be
    /// useful.
    /// </param>
    /// <param name="maxResults">
    /// Maximum number of results to return. Defaults to 50. Results are
    /// returned in depth-first schema order, not ranked by relevance.
    /// </param>
    /// <remarks>
    /// <b>Navigation note.</b> Results carry the <see cref="SchemaSearchResult.JsonPath"/>
    /// that locates the schema property; they do NOT carry a reference to any
    /// UI navigation node. Applications that need to deep-link into an editor
    /// page must maintain their own mapping from JsonPath to navigation context
    /// (e.g. a dictionary built from the navigation tree's
    /// <c>SettingsGroupEditorViewModel.SchemaNodes</c>).
    /// </remarks>
    IReadOnlyList<SchemaSearchResult> SearchSchema(string query, int maxResults = 50);

    // ── Validation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Validate all dirty documents against the schema, reporting only
    /// errors introduced since the on-disk baseline.  Returns an empty list
    /// when valid.  Called automatically by
    /// <see cref="SaveAsync(bool, CancellationToken)"/> unless
    /// <c>force: true</c>.
    /// </summary>
    /// <remarks>
    /// Use <see cref="ValidateAllAsync"/> instead when you want to surface
    /// pre-existing violations (e.g. immediately after a reload, where the
    /// workspace has no in-flight edits but the on-disk file may already be
    /// invalid because someone edited it externally).
    /// </remarks>
    Task<IReadOnlyList<SchemaValidationError>> ValidateAsync(CancellationToken ct);

    /// <summary>
    /// Validate <b>every</b> writable document against the schema and report
    /// <b>all</b> currently-invalid fields, including pre-existing violations
    /// from before the user edited anything.  Counterpart to
    /// <see cref="ValidateAsync"/>; useful for post-reload "what's wrong in
    /// the loaded files" surfaces (e.g. a schema-violation banner) where
    /// pre-existing issues are exactly what the user needs to know about.
    /// </summary>
    Task<IReadOnlyList<SchemaValidationError>> ValidateAllAsync(CancellationToken ct);

    // ── Events ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised after every state-mutating operation
    /// (<see cref="SetValue{T}(string, T)"/>, <see cref="RemoveValue"/>,
    /// accessor mutations) and after <see cref="SaveAsync(bool, CancellationToken)"/> /
    /// <see cref="ReloadAsync"/>. Single event; consumers filter via
    /// <see cref="ClientChangedEventArgs.Kind"/>.
    /// </summary>
    /// <remarks>
    /// Raised on whatever thread triggered the change, AFTER the workspace
    /// lock has been released. Subscribers must not call back into the SDK
    /// synchronously — see the class remarks.
    /// </remarks>
    event EventHandler<ClientChangedEventArgs>? Changed;

    // ── Memory & footprint (Phase 5) ──────────────────────────────────────

    /// <summary>
    /// Snapshot the Tier 1 user-memory inventory — files Claude reads on
    /// every session that the user authored (CLAUDE.md, agents, slash
    /// commands, hooks, plans, rules, skills, cross-tool memory).
    /// </summary>
    /// <param name="projectRoot">
    /// Optional project directory; when supplied, the project-scoped
    /// <c>CLAUDE.md</c> / <c>AGENTS.md</c> entries are included. Pass
    /// <see langword="null"/> when no project is open.
    /// </param>
    /// <returns>
    /// Unsorted list. Claude Code returns the full inventory; Claude Desktop
    /// returns an empty list (no <c>CLAUDE.md</c>-equivalent surface).
    /// </returns>
    IReadOnlyList<UserMemoryFile> SnapshotUserMemoryFiles(string? projectRoot = null);

    /// <summary>
    /// Read the full text of a Tier 1 file. Returns <see langword="null"/>
    /// when the file no longer exists OR cannot be read; the caller's UI
    /// should fall back gracefully rather than throw.
    /// </summary>
    Task<string?> ReadMemoryFileAsync(string absolutePath, CancellationToken ct);

    /// <summary>
    /// Compute the Tier 2 footprint stats for every <see cref="FootprintCategory"/>.
    /// Returns one row per category, including categories whose directory
    /// is missing (count = 0, size = 0). Cancellable.
    /// </summary>
    Task<IReadOnlyList<FootprintCategoryStats>> GetFootprintStatsAsync(CancellationToken ct);

    /// <summary>
    /// Delete every file under the named footprint category. Throws on
    /// the first per-file failure so the GUI can surface which file the
    /// deletion stopped on; partial deletions are NOT rolled back.
    /// </summary>
    Task DeleteFootprintCategoryAsync(FootprintCategory category, CancellationToken ct);

    /// <summary>
    /// Per-project breakdown of the SessionTranscripts category — one row
    /// per <c>~/.claude/projects/&lt;mangled&gt;/</c> subdirectory. Use
    /// <see cref="DeleteProjectTranscriptsAsync"/> to wipe one project's
    /// transcripts without touching the others.
    /// </summary>
    Task<IReadOnlyList<ProjectTranscriptStats>> GetProjectTranscriptStatsAsync(CancellationToken ct);

    /// <summary>
    /// Delete every <c>*.jsonl</c> transcript under one project's
    /// <c>~/.claude/projects/&lt;mangledName&gt;/</c> directory. The
    /// directory itself is left in place. Callers MUST source
    /// <paramref name="mangledName"/> from a
    /// <see cref="ProjectTranscriptStats"/> record returned by
    /// <see cref="GetProjectTranscriptStatsAsync"/>, never from freeform
    /// user input — the SDK validates that the name is a flat directory
    /// segment (no separators / drive specs) but defence-in-depth.
    /// </summary>
    Task DeleteProjectTranscriptsAsync(string mangledName, CancellationToken ct);
}

/// <summary>Discriminator for <see cref="ClientChangedEventArgs"/>.</summary>
public enum ClientChangeKind
{
    /// <summary>An in-memory mutation (SetValue, RemoveValue, accessor write).</summary>
    Mutation,

    /// <summary>The workspace was persisted to disk via <see cref="IClaudeConfigClient.SaveAsync(bool, CancellationToken)"/>.</summary>
    Saved,

    /// <summary>The workspace was reloaded from disk via <see cref="IClaudeConfigClient.ReloadAsync"/>.</summary>
    Reloaded,
}

/// <summary>
/// Payload for <see cref="IClaudeConfigClient.Changed"/>.
/// </summary>
/// <param name="Kind">Discriminator for the kind of change.</param>
/// <param name="Path">
/// Dotted path of the mutated property when <paramref name="Kind"/> is
/// <see cref="ClientChangeKind.Mutation"/>. <c>null</c> for save / reload events.
/// </param>
public sealed record ClientChangedEventArgs(ClientChangeKind Kind, string? Path);