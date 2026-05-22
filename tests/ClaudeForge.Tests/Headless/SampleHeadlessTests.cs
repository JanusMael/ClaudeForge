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
/// Pattern for adding a new headless test:
/// <code>
/// [TestMethod]
/// public Task MyTest() =&gt; Session.Dispatch(async () =&gt;
/// {
///     // Now on the headless UI thread.  Construct controls, fire
///     // dispatcher work, await async ops as if you were in a real app.
///     var window = new Window { Width = 800, Height = 600 };
///     window.Show();
///     await Dispatcher.UIThread.InvokeAsync(() =&gt; { /* user-thread work */ });
///     Assert.IsTrue(window.IsVisible);
///     window.Close();
/// });
/// </code>
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