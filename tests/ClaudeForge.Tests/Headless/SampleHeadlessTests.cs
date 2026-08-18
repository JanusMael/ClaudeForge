using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// Demonstrates the H-3 Avalonia headless test harness.  Each test
/// dispatches onto the shared <see cref="HeadlessUnitTestSession"/>'s UI
/// thread and exercises a real Avalonia primitive (Window, dispatcher,
/// layout pass) in-process — without a desktop / display server.
/// </summary>
/// <remarks>
/// <para>
/// This is a foundation pass.  The fixtures it unlocks need
/// significant per-fixture setup: a fake <c>IDialogService</c> with
/// <c>TaskCompletionSource</c>, a fake <c>IFileWatcher</c>
/// controllable from tests, and an MWVM constructed against in-memory
/// SDK clients.  Building those is a follow-up; this file proves the
/// harness itself works.
/// </para>
/// <para>
/// Pattern for adding a new SYNCHRONOUS headless test — the two below:
/// <code>
/// [TestMethod]
/// public Task MyTest() =&gt; Session.Dispatch(() =&gt;
/// {
///     // Now on the headless UI thread.  Construct controls and fire
///     // dispatcher work as if you were in a real app.
///     var window = new Window { Width = 800, Height = 600 };
///     window.Show();
///     Assert.IsTrue(window.IsVisible);
///     window.Close();
/// }, CancellationToken.None);
/// </code>
/// A non-async lambda binds to <c>Dispatch(Action, CancellationToken)</c> and is safe.
/// </para>
/// <para>
/// ⚠ <b>An ASYNC body needs a different shape, or the test cannot fail.</b> Writing
/// <c>Session.Dispatch(async () =&gt; { … })</c> binds to
/// <c>Dispatch&lt;T&gt;(Func&lt;T&gt;, CancellationToken)</c> with <c>T = Task</c>, so the call
/// returns <c>Task&lt;Task&gt;</c>. The framework awaits only the OUTER task, which completes
/// the moment the lambda hands back its inner task — every assertion after the first
/// <c>await</c> runs unobserved and its exception is swallowed. Adding a single
/// <c>await</c> does not help; the inner task still goes unawaited. <b>Return a value
/// from the lambda</b> so it binds <c>Dispatch&lt;T&gt;(Func&lt;Task&lt;T&gt;&gt;, …)</c>, which
/// unwraps properly:
/// <code>
/// [TestMethod]
/// public async Task MyAsyncTest()
/// {
///     string result = await Session.Dispatch(async () =&gt;
///     {
///         var vm = BuildViewModel();
///         await vm.LoadAllWorkspacesAsync();
///         return vm.SomeValue;          // ← the return is what makes this observable
///     }, CancellationToken.None);
///
///     Assert.AreEqual("expected", result);
/// }
/// </code>
/// <c>Headless/SavePreservationTests.cs</c> is the worked example. Whichever shape you
/// use, <b>canary it</b>: put <c>Assert.Fail("canary")</c> inside and confirm the test
/// actually reports Failed before trusting a green run.
/// </para>
/// </remarks>
[TestClass]
public sealed class SampleHeadlessTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    [TestMethod]
    public Task Headless_Dispatcher_RunsActionOnUIThread()
    {
        return Session.Dispatch(() =>
        {
            // We're on the headless UI thread now; verify the dispatcher
            // says so.  This is the canonical "harness is alive" smoke
            // test — a regression on this method means the headless
            // session itself didn't spin up.
            Assert.IsTrue(Dispatcher.UIThread.CheckAccess(),
                "Action body must execute on the headless UI thread.");
        }, CancellationToken.None);
    }

    [TestMethod]
    public Task Headless_Window_LayoutCompletes()
    {
        return Session.Dispatch(() =>
        {
            // Simple layout pass.  Window MeasureOverride / ArrangeOverride
            // run against the headless platform — no display, no GPU,
            // but the geometry is real.
            Window window = new()
            {
                Width = 400,
                Height = 300,
                Content = new TextBlock { Text = "headless smoke" },
            };
            window.Show();

            Assert.AreEqual(400, window.Width);
            Assert.AreEqual(300, window.Height);
            Assert.IsTrue(window.IsVisible);

            window.Close();
        }, CancellationToken.None);
    }
}