using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests;

/// <summary>
/// architectural-hardening regression tests for the SDK's
/// thread-reentrant <c>_stateLock</c> pattern in
/// <see cref="ClaudeConfigClientCore"/>.
/// </summary>
/// <remarks>
/// <para>
/// The original deadlock: SDK.SetValue acquires <c>_stateLock</c>, calls
/// <c>_workspace.SetValue</c> which fires <c>workspace.Changed</c>
/// synchronously while the lock is still held, and any Changed handler
/// that calls back into SDK accessors (e.g. <c>GetEffective&lt;T&gt;</c>)
/// hangs because the SemaphoreSlim is non-reentrant.
/// </para>
/// <para>
/// Fix: <c>EnterStateLock</c> / <c>ExitStateLock</c> helpers track the
/// holder thread + a re-entry depth counter, letting the same thread
/// skip re-acquisition. These tests lock the contract:
/// </para>
/// <list type="number">
///   <item>Same-thread re-entry from a Changed handler does NOT deadlock.</item>
///   <item>Re-entry depth is correctly unwound — the lock is fully
///         released once all matched Exit calls run, allowing a different
///         thread to acquire.</item>
///   <item>Different threads still serialise (no shared-state corruption).</item>
/// </list>
/// </remarks>
[TestClass]
public sealed class ClaudeConfigClientCoreReentrancyTests
{
    private string _tempDir = null!;
    private string? _previousOverride;

    [TestInitialize]
    public void Setup()
    {
        // Isolate each test's filesystem so OpenAsync doesn't read the
        // real user's ~/.claude/ tree and so parallel tests don't trip
        // over each other's temp dirs.
        _tempDir = Path.Combine(Path.GetTempPath(), "sdk-reentrancy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _previousOverride = PlatformPaths.TestUserProfileOverride;
        PlatformPaths.TestUserProfileOverride = _tempDir;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = _previousOverride;
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            /* best effort */
        }
    }

    [TestMethod]
    public void Reentrancy_SetValueFromInsideChangedHandler_DoesNotDeadlock()
    {
        // Repro the user-hit pattern: a Changed handler that calls back
        // into the SDK on the same thread that just called SetValue.
        using ClaudeCodeClient client = new();
        Task openTask = client.OpenAsync(projectRoot: null, ct: CancellationToken.None);
        openTask.Wait(TimeSpan.FromSeconds(10));

        bool handlerRan = false;
        client.Changed += (_, _) =>
        {
            // This call is synchronous from inside SetValue's lock — it
            // would have hung on the original SemaphoreSlim. With the
            // reentrant helpers it succeeds.
            _ = client.GetEffective<string>("model");
            handlerRan = true;
        };

        // Bound by a generous timeout — a regression would manifest as
        // the test hanging.
        bool done = Task.Run(() => { client.SetValue("model", "opus", ConfigScope.User); })
                        .Wait(TimeSpan.FromSeconds(5));

        Assert.IsTrue(done, "SetValue must complete; reentrant lock is broken if this hangs.");
        Assert.IsTrue(handlerRan, "Changed handler must have fired.");
    }

    [TestMethod]
    public void Reentrancy_NestedReadInsideRead_DoesNotDeadlock()
    {
        // GetEffective from inside a Changed-fire-context that triggers
        // another GetEffective. This is the read-then-read variant.
        using ClaudeCodeClient client = new();
        Task openTask = client.OpenAsync(projectRoot: null, ct: CancellationToken.None);
        openTask.Wait(TimeSpan.FromSeconds(10));

        client.SetValue("model", "sonnet", ConfigScope.User);

        bool nestedRead = false;
        client.Changed += (_, _) =>
        {
            // First read on the handler-fire path
            string? first = client.GetEffective<string>("model");
            // Second read — verifies depth counter unwinds correctly so
            // we don't release the lock between reads.
            string? second = client.GetEffective<string>("model");
            Assert.AreEqual(first, second);
            nestedRead = true;
        };

        bool done = Task.Run(() => { client.SetValue("model", "opus", ConfigScope.User); })
                        .Wait(TimeSpan.FromSeconds(5));

        Assert.IsTrue(done);
        Assert.IsTrue(nestedRead);
    }

    [TestMethod]
    public async Task Reentrancy_DifferentThreads_StillSerialiseProperly()
    {
        // Thread isolation contract: even with re-entrancy on the same
        // thread, two DIFFERENT threads must still serialise. This guards
        // against an over-eager "skip the wait" that would let two
        // threads mutate concurrently.
        using ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        int iterations = 50;
        Task t1 = Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                client.SetValue("a", $"t1-{i}", ConfigScope.User);
            }
        });
        Task t2 = Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                client.SetValue("b", $"t2-{i}", ConfigScope.User);
            }
        });

        await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(15));

        // Final values reflect last iteration on each thread.
        Assert.AreEqual($"t1-{iterations - 1}", client.GetEffective<string>("a"));
        Assert.AreEqual($"t2-{iterations - 1}", client.GetEffective<string>("b"));
    }

    [TestMethod]
    public void Reentrancy_DepthThreeNesting_UnwindsCorrectly()
    {
        // Trigger a 3-deep nesting: SetValue -> Changed -> GetEffective
        //   -> (in our handler) SetValue -> Changed -> GetEffective.
        // This exercises the +1 / +1 / +1 / -1 / -1 / -1 unwinding of the
        // depth counter. A bug in the depth math would either deadlock
        // or release the lock prematurely.
        using ClaudeCodeClient client = new();
        client.OpenAsync(projectRoot: null, ct: CancellationToken.None).Wait(TimeSpan.FromSeconds(10));

        int handlerInvocations = 0;
        client.Changed += (_, e) =>
        {
            // First-level: just read.
            client.GetEffective<string>("model");
            handlerInvocations++;

            // On the FIRST Changed only, recurse: another SetValue from
            // inside the handler. This re-enters the lock 2 more times
            // (the inner SetValue + its own Changed handler invocation).
            if (handlerInvocations == 1)
            {
                client.SetValue("nested", "value", ConfigScope.User);
            }
        };

        bool done = Task.Run(() => { client.SetValue("model", "outer", ConfigScope.User); })
                        .Wait(TimeSpan.FromSeconds(5));

        Assert.IsTrue(done, "Nested re-entry must not deadlock.");
        Assert.IsTrue(handlerInvocations >= 2, "Outer + nested SetValue both fire Changed.");

        // After full unwinding the lock must be free — verified by a
        // fresh write succeeding without hanging.
        Task.Run(() => client.SetValue("post", "x", ConfigScope.User)).Wait(TimeSpan.FromSeconds(5));
        Assert.AreEqual("x", client.GetEffective<string>("post"));
    }
}