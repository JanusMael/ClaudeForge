using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests;

/// <summary>
/// The async read/write variants (<c>GetEffectiveAsync</c> / <c>SetValueAsync</c> /
/// <c>RemoveValueAsync</c> + the atomic <c>SetValueIfChangedAsync</c>): functional
/// parity with their sync twins, PLUS the async-pitfall contract every future async
/// SDK method must honor —
/// <list type="bullet">
///   <item>cancellation is honored and leaves no partial mutation;</item>
///   <item>the state lock is released when the in-lock body throws (no leak/deadlock);</item>
///   <item>no deadlock when a SynchronizationContext is captured (ConfigureAwait(false));</item>
///   <item>the conditional write is an atomic compare-and-set under contention (no TOCTOU);</item>
///   <item>Changed fires once on a real write and never on a no-op.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class ClaudeConfigClientAsyncTests
{
    private static ClaudeConfigClientCore MakeClient(string userJson = "{}")
    {
        JsonObject root = (JsonObject)JsonNode.Parse(userJson)!;
        SettingsDocument doc = new(ConfigScope.User, "user.json", root, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        return ClaudeCodeClient.FromExistingWorkspace(ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
    }

    /// <summary>
    /// A client with a read-only Managed scope shadowing a writable User scope — the
    /// minimum needed to exercise cross-scope shadowing (Managed &gt; User for
    /// non-array paths). Writes target User; Managed wins the effective merge.
    /// </summary>
    private static ClaudeConfigClientCore MakeClientWithManaged(string managedJson, string userJson = "{}")
    {
        JsonObject managedRoot = (JsonObject)JsonNode.Parse(managedJson)!;
        JsonObject userRoot = (JsonObject)JsonNode.Parse(userJson)!;
        SettingsDocument managed = new(ConfigScope.Managed, "managed.json", managedRoot, isReadOnly: true);
        SettingsDocument user = new(ConfigScope.User, "user.json", userRoot, isReadOnly: false);
        SettingsWorkspace ws = new([managed, user]);
        return ClaudeCodeClient.FromExistingWorkspace(ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
    }

    // ── Functional parity ────────────────────────────────────────────────────

    [TestMethod]
    public async Task SetGetRemoveAsync_RoundTrips_AndIsVisibleToSync()
    {
        using ClaudeConfigClientCore c = MakeClient();
        await c.SetValueAsync("model", "claude-opus-4-8", CancellationToken.None);
        Assert.AreEqual("claude-opus-4-8", await c.GetEffectiveAsync<string>("model", CancellationToken.None));
        Assert.AreEqual("claude-opus-4-8", c.GetEffective<string>("model"), "Async write is visible to a sync read.");

        await c.RemoveValueAsync("model", c.DefaultScope, CancellationToken.None);
        Assert.IsTrue(string.IsNullOrEmpty(await c.GetEffectiveAsync<string>("model", CancellationToken.None)));
    }

    [TestMethod]
    public async Task SetValueIfChangedAsync_WritesOnlyWhenScopeValueChanges()
    {
        // Single-scope client, so the target-scope value and the effective value
        // coincide here; the multi-scope distinction is pinned by the _ShadowedScope_
        // tests below.
        using ClaudeConfigClientCore c = MakeClient("""{"model":"a"}""");

        Assert.IsFalse(await c.SetValueIfChangedAsync("model", "a", c.DefaultScope, CancellationToken.None),
            "Equal to the value already at this scope → no write.");
        Assert.IsFalse(c.HasUnsavedChanges, "A no-op conditional write must not dirty the document.");

        Assert.IsTrue(await c.SetValueIfChangedAsync("model", "b", c.DefaultScope, CancellationToken.None),
            "Different from the value at this scope → writes.");
        Assert.AreEqual("b", c.GetEffective<string>("model"));
    }

    [TestMethod]
    public async Task SetValueIfChangedAsync_SkipsValueAlreadyAtScope_NoGhost()
    {
        // effortLevel is already 'high' at the User (default) scope; re-writing 'high'
        // changes nothing at that scope → must skip (the ghost-change class the atomic
        // conditional write exists to prevent). Single-scope here, so scope == effective.
        using ClaudeConfigClientCore c = MakeClient("""{"effortLevel":"high"}""");
        Assert.IsFalse(await c.SetValueIfChangedAsync("effortLevel", "high", c.DefaultScope, CancellationToken.None));
        Assert.IsFalse(c.HasUnsavedChanges);
    }

    // ── Scope-specific (not cross-scope effective) compare basis ───────────────
    // A higher-priority Managed scope shadows the written User scope. The compare
    // basis MUST be the target scope, not the merged effective value.

    [TestMethod]
    public async Task SetValueIfChangedAsync_ShadowedScope_ReassertingSameScopeValue_IsNoOp()
    {
        // User explicitly has model="b"; Managed shadows it with "a" (effective="a").
        // Re-writing "b" at User is a true no-op AT THE USER SCOPE — must skip, even
        // though it differs from the effective value. (The pre-fix effective compare
        // saw "a" != "b" and wrongly wrote + raised Changed with no scope change.)
        using ClaudeConfigClientCore c = MakeClientWithManaged("""{"model":"a"}""", """{"model":"b"}""");
        int changed = 0;
        c.Changed += (_, _) => Interlocked.Increment(ref changed);

        Assert.IsFalse(await c.SetValueIfChangedAsync("model", "b", ConfigScope.User, CancellationToken.None),
            "Value already present at the target scope → no write.");
        Assert.AreEqual(0, Volatile.Read(ref changed), "A scope no-op must not raise Changed.");
        Assert.IsFalse(c.HasUnsavedChanges, "A scope no-op must not dirty the document.");
        Assert.AreEqual("a", c.GetEffective<string>("model"), "Managed still shadows User.");
    }

    [TestMethod]
    public async Task SetValueIfChangedAsync_ShadowedScope_WritesGenuineExplicitPin()
    {
        // User has no model; Managed shadows with "a". Pinning "b" at User genuinely
        // changes the User scope, so it MUST write (not be dropped as "no effective
        // change") — the symmetric guarantee that mirrors the editor's shadowed-scope
        // pin fix (A3). Effective stays "a" because Managed wins the merge.
        using ClaudeConfigClientCore c = MakeClientWithManaged("""{"model":"a"}""");
        int changed = 0;
        c.Changed += (_, _) => Interlocked.Increment(ref changed);

        Assert.IsTrue(await c.SetValueIfChangedAsync("model", "b", ConfigScope.User, CancellationToken.None),
            "A new explicit value at the target scope → writes.");
        Assert.AreEqual(1, Volatile.Read(ref changed), "The genuine scope change raises Changed once.");
        Assert.AreEqual("b", c.GetScopeValue("model", ConfigScope.User)?.GetValue<string>(), "The pin landed at User.");
        Assert.AreEqual("a", c.GetEffective<string>("model"), "Managed still shadows the User pin in the effective view.");
    }

    // ── RemoveValueAsync: nested-absent is a clean no-op; emptying a parent drops it ──

    [TestMethod]
    public async Task RemoveValueAsync_NestedAbsentKey_RaisesNoChanged_AndStaysClean()
    {
        using ClaudeConfigClientCore c = MakeClient();
        int changed = 0;
        c.Changed += (_, _) => Interlocked.Increment(ref changed);

        await c.RemoveValueAsync("sandbox.network.allowedDomains", ConfigScope.User, CancellationToken.None);

        Assert.AreEqual(0, Volatile.Read(ref changed), "Removing an absent nested key raises no Changed.");
        Assert.IsFalse(c.HasUnsavedChanges, "Removing an absent nested key does not dirty the document.");
    }

    [TestMethod]
    public async Task RemoveValueAsync_NestedRemoveEmptyingParent_RemovesTopKey()
    {
        using ClaudeConfigClientCore c = MakeClient();
        await c.SetValueAsync("sandbox.allowUnix", true, ConfigScope.User, CancellationToken.None);
        Assert.IsNotNull(c.GetScopeValue("sandbox", ConfigScope.User), "Precondition: the sandbox object exists at User.");

        await c.RemoveValueAsync("sandbox.allowUnix", ConfigScope.User, CancellationToken.None);

        Assert.IsNull(c.GetScopeValue("sandbox", ConfigScope.User),
            "Removing the only nested key drops the now-empty top-level object.");
    }

    // ── Cancellation ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SetValueAsync_PreCancelledToken_Throws_AndDoesNotMutate()
    {
        using ClaudeConfigClientCore c = MakeClient("""{"model":"a"}""");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // SemaphoreSlim.WaitAsync surfaces TaskCanceledException (a subclass of
        // OperationCanceledException); assert by assignability, not exact type.
        Exception ex = await CaptureAsync(() => c.SetValueAsync("model", "b", cts.Token));
        Assert.IsInstanceOfType(ex, typeof(OperationCanceledException), "Cancellation should surface an OperationCanceledException.");

        Assert.AreEqual("a", c.GetEffective<string>("model"), "A cancelled write must commit no mutation.");
        Assert.IsFalse(c.HasUnsavedChanges);
    }

    [TestMethod]
    public async Task GetEffectiveAsync_PreCancelledToken_Throws()
    {
        using ClaudeConfigClientCore c = MakeClient("""{"model":"a"}""");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Exception ex = await CaptureAsync(() => c.GetEffectiveAsync<string>("model", cts.Token));
        Assert.IsInstanceOfType(ex, typeof(OperationCanceledException), "Cancellation should surface an OperationCanceledException.");
    }

    // ── Lock is released when the in-lock body throws (no leak → no deadlock) ──

    [TestMethod]
    public async Task AsyncWrite_ReleasesLock_WhenInLockBodyThrows()
    {
        // A not-opened client throws EnsureOpen INSIDE the lock (after acquiring it).
        // If the finally didn't release, the next acquisition would hang forever.
        using var notOpen = new ClaudeCodeClient();

        await AssertThrowsAsync(() => notOpen.SetValueAsync("model", "x", CancellationToken.None));

        Task second = notOpen.SetValueAsync("model", "y", CancellationToken.None);
        Task finished = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.AreSame(second, finished, "Second async write hung → the state lock was leaked on the first exception.");
        await AssertThrowsAsync(() => second);
    }

    // ── No deadlock when a SynchronizationContext is captured ──────────────────

    [TestMethod]
    public void ConcurrentAsyncWrites_NoDeadlock_UnderCapturedContext()
    {
        using ClaudeConfigClientCore c = MakeClient("""{"model":"a"}""");
        SynchronizationContext? prev = SynchronizationContext.Current;
        var ctx = new CountingSyncContext();
        SynchronizationContext.SetSynchronizationContext(ctx);
        try
        {
            // 50 concurrent writes genuinely contend the state lock, so WaitAsync
            // truly suspends and resumes via a continuation. ConfigureAwait(false)
            // means those continuations run OFF this (non-pumping) context; without
            // it they'd queue here and, since the thread is blocked in Wait(),
            // deadlock.
            Task[] tasks = Enumerable.Range(0, 50)
                .Select(i => c.SetValueAsync("model", "v" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None))
                .ToArray();

            bool all = Task.WhenAll(tasks).Wait(TimeSpan.FromSeconds(10));
            Assert.IsTrue(all, "Concurrent async writes deadlocked under a non-pumping SynchronizationContext.");
            Assert.AreEqual(0, ctx.Posts, "A continuation marshaled back to the captured context — an await is missing ConfigureAwait(false).");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prev);
        }
    }

    // ── Atomic compare-and-set under contention (the TOCTOU killer) ────────────

    [TestMethod]
    public async Task SetValueIfChangedAsync_IsAtomic_UnderConcurrency()
    {
        using ClaudeConfigClientCore c = MakeClient("""{"model":"old"}""");

        // N tasks race to set the SAME new value. Because the compare AND the write
        // happen under one lock acquisition, exactly ONE task observes effective !=
        // "new" and writes; every other task then sees "new" and skips. A non-atomic
        // get-then-set would let several write (all read "old" before any commits).
        Task<bool>[] tasks = Enumerable.Range(0, 64)
            .Select(_ => c.SetValueIfChangedAsync("model", "new", c.DefaultScope, CancellationToken.None))
            .ToArray();
        bool[] results = await Task.WhenAll(tasks);

        Assert.AreEqual(1, results.Count(wrote => wrote), "Exactly one concurrent conditional write must have committed.");
        Assert.AreEqual("new", c.GetEffective<string>("model"));
    }

    // ── Changed: once per real write, never on a no-op ─────────────────────────

    [TestMethod]
    public async Task SetValueIfChangedAsync_RaisesChangedOnce_NotOnNoOp()
    {
        using ClaudeConfigClientCore c = MakeClient("""{"model":"a"}""");
        int changed = 0;
        c.Changed += (_, _) => Interlocked.Increment(ref changed);

        await c.SetValueIfChangedAsync("model", "b", c.DefaultScope, CancellationToken.None); // real write
        await c.SetValueIfChangedAsync("model", "b", c.DefaultScope, CancellationToken.None); // no-op

        Assert.AreEqual(1, Volatile.Read(ref changed), "Changed fires once for the write, never for the no-op skip.");
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static async Task AssertThrowsAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            return;
        }

        Assert.Fail("Expected an exception but none was thrown.");
    }

    private static async Task<Exception> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            return ex;
        }

        Assert.Fail("Expected an exception but none was thrown.");
        return null!; // unreachable
    }

    private sealed class CountingSyncContext : SynchronizationContext
    {
        private int _posts;
        public int Posts => Volatile.Read(ref _posts);
        public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _posts);
        public override void Send(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _posts);
    }
}
