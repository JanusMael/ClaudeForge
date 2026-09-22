using System.Reflection;

using Avalonia;
using Avalonia.Headless;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// Guards that <see cref="HeadlessSessionBootstrap"/> still does the thing it exists to do:
/// build the Avalonia application during <c>[AssemblyInitialize]</c>, not in whichever test
/// dispatches first.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>The regression this catches is INVISIBLE without it.</b> Deleting the warm-up dispatch
/// leaves every test passing on most runs — application set-up simply moves back into the first
/// test to dispatch, and only fails when a non-headless test has already bound
/// <c>Dispatcher.UIThread</c> to the MSTest thread. That is a rare, order-dependent failure
/// reported against an arbitrary unrelated assertion, which is precisely what cost this
/// repository three separate CI investigations (2026-09-19 ×2, 2026-09-22).
/// </para>
/// <para>
/// ⭐ <b>Why asserting a flag is the honest form here.</b> The property under test is a <i>timing</i>
/// one — "set-up had already happened before any test body ran" — and by the time any test can
/// observe the world, set-up has happened either way. Only <c>[AssemblyInitialize]</c> can witness
/// the difference, so it records what it saw and this asserts the record. A test that merely
/// checked <c>Application.Current is not null</c> from here would pass in both worlds and guard
/// nothing.
/// </para>
/// </remarks>
[TestClass]
public sealed class HeadlessSessionBootstrapTests
{
    [TestMethod]
    public void TheApplicationIsBuiltDuringAssemblyInitialize_NotByTheFirstTestToDispatch()
    {
        Assert.IsTrue(
            HeadlessSessionBootstrap.ApplicationBuiltDuringAssemblyInitialize,
            "HeadlessSessionBootstrap did not observe a built Avalonia application during "
            + "[AssemblyInitialize]. Its warm-up Dispatch has been removed or reordered, so "
            + "AppBuilder.SetupUnsafe() is once again the first-scheduled test's responsibility "
            + "— which reintroduces the intermittent 'The calling thread cannot access this "
            + "object because a different thread owns it' failure, attributed to an arbitrary "
            + "unrelated test.");
    }

    /// <summary>
    /// The premise of the fix: one session per assembly, so the warm-up and every fixture are
    /// talking about the same thing. If <c>GetOrStartForAssembly</c> ever returned a fresh
    /// session per call, warming one up would say nothing about the others.
    /// </summary>
    [TestMethod]
    public void GetOrStartForAssembly_ReturnsTheSameSessionEveryTime()
    {
        HeadlessUnitTestSession a =
            HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());
        HeadlessUnitTestSession b =
            HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

        Assert.AreSame(a, b,
            "The session is supposed to be cached per assembly. If it is not, the bootstrap "
            + "warms up an instance the fixtures never use.");
    }

    /// <summary>
    /// And that the session's thread really is the one the application belongs to — the property
    /// whose violation produces the cross-thread throw in the first place.
    /// </summary>
    [TestMethod]
    public Task TheSessionThreadOwnsTheDispatcher()
    {
        HeadlessUnitTestSession session =
            HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

        return session.Dispatch(() =>
        {
            Assert.IsNotNull(Application.Current,
                "The application must exist on the session thread.");
            Assert.IsTrue(global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess(),
                "Dispatcher.UIThread must be owned by the session thread. If this fails, the "
                + "global dispatcher was bound by some earlier toucher on another thread, which "
                + "is the condition that makes compositor construction throw.");
        }, CancellationToken.None);
    }
}
