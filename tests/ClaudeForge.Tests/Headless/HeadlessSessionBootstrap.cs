using System.Reflection;

using Avalonia.Headless;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// Starts the assembly's shared <see cref="HeadlessUnitTestSession"/> once, before any
/// test runs.
///
/// <para>
/// Every headless fixture reaches the session through
/// <c>HeadlessUnitTestSession.GetOrStartForAssembly(...)</c>, which starts it lazily on
/// first use. That makes the <em>starting</em> of the Avalonia platform the
/// responsibility of whichever test happens to be scheduled first — which differs
/// between runs and between operating systems.
/// </para>
/// <para>
/// The cost of that showed up the first time CI ran this branch on macOS: platform
/// start-up threw <c>InvalidOperationException: The calling thread cannot access this
/// object because a different thread owns it</c> from
/// <c>Compositor..ctor</c> → <c>DefaultRenderLoop.Add</c> → <c>Dispatcher.VerifyAccess</c>,
/// and the failure was reported against <c>StatusControllerTests.Set_SuccessKind_AutoClearsAfterDelay</c>
/// — a status-bar auto-clear assertion that has nothing whatever to do with compositor
/// construction. A start-up fault attributed to an arbitrary unrelated assertion is
/// close to undiagnosable from a CI log.
/// </para>
/// <para>
/// Starting here makes that ordering explicit: the session comes up once, on the test
/// host's initialisation path, before any fixture can race into it — and if platform
/// start-up ever fails again, it fails <em>here</em>, where the stack trace means what
/// it says.
/// </para>
/// <para>
/// This complements, and does not replace, the <c>[assembly: DoNotParallelize]</c> in
/// <c>Parallelization.cs</c>: that keeps the single headless dispatcher from being
/// driven concurrently once it is up; this decides when it comes up.
/// </para>
/// </summary>
[TestClass]
public static class HeadlessSessionBootstrap
{
    [AssemblyInitialize]
    public static void StartHeadlessSession(TestContext context)
    {
        _ = context;

        // The session is cached per assembly, so this is the one start; every
        // GetOrStartForAssembly call in a fixture then returns the same instance.
        _ = HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());
    }
}
