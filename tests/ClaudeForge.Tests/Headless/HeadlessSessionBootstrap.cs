using System.Reflection;

using Avalonia;
using Avalonia.Headless;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// Starts the assembly's shared <see cref="HeadlessUnitTestSession"/> <em>and forces the Avalonia
/// application to be built</em>, once, before any test runs.
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
/// ⛔⛔ <b><c>GetOrStartForAssembly</c> ALONE DOES NOT DO THAT, and this file claimed it did for
/// three weeks.</b> It starts the session object and its dispatcher <em>thread</em>; it does not
/// build the Avalonia application. <c>HeadlessUnitTestSession.EnsureIsolatedApplication()</c> —
/// the call that runs <c>AppBuilder.SetupUnsafe()</c> and constructs the compositor — is invoked
/// lazily from <c>DispatchCore</c>, i.e. on the <b>first <c>Dispatch</c></b>. So application
/// set-up stayed the first-scheduled test's responsibility, which is exactly the ordering this
/// type was added to remove.
/// </para>
/// <para>
/// ⭐ <b>Measured, not reasoned.</b> On 2026-09-22 CI caught the same exception again, this time
/// reported against <c>SchemaProvenanceBadgeTests.ClaudeCode_FallenBackToBundled_SaysTheFetchWasTried</c>,
/// on a pull request whose whole diff was a Markdown file. Its stack has
/// <c>EnsureIsolatedApplication</c> → <c>SetupUnsafe</c> → <c>Compositor..ctor</c> running
/// <b>inside that test's <c>Dispatch</c></b> — which it could only do if set-up had not already
/// happened. The same job passed in the duplicate CI run of the identical commit.
/// </para>
/// <para>
/// ⚠ <b>Why a wrong thread at all:</b> <c>Dispatcher.UIThread</c> is a lazily-resolved
/// process-global singleton that binds to whichever thread touches it first — the same property
/// <c>LiveLogWindow</c> documents for the shipping app, where constructing any
/// <c>AvaloniaObject</c> too early "forces <c>Dispatcher.UIThread</c> to resolve". In a full-suite
/// run, a non-headless test that touches Avalonia can bind it to the MSTest thread; the session
/// thread then builds the compositor and <c>VerifyAccess</c> fails. In an isolated class run
/// nothing gets there first, which is why the affected classes are green alone and flaky together.
/// </para>
/// <para>
/// So the warm-up <c>Dispatch</c> below is the load-bearing line, not the
/// <c>GetOrStartForAssembly</c> above it: it makes the session thread the first toucher,
/// deterministically, on the one code path MSTest guarantees runs before every test.
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
    /// <summary>
    /// Whether the warm-up dispatch actually observed a built Avalonia application. Read by
    /// <c>HeadlessSessionBootstrapTests</c>; it is the only way to prove from inside the suite
    /// that set-up happened at assembly-initialize time rather than in the first test.
    /// </summary>
    internal static bool ApplicationBuiltDuringAssemblyInitialize { get; private set; }

    [AssemblyInitialize]
    public static void StartHeadlessSession(TestContext context)
    {
        _ = context;

        // The session is cached per assembly, so this is the one start; every
        // GetOrStartForAssembly call in a fixture then returns the same instance.
        HeadlessUnitTestSession session =
            HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

        // ⛔ DO NOT DELETE. This dispatch is what builds the application — see the type remarks.
        // Without it the session exists but Avalonia does not, and the first test to dispatch
        // pays for SetupUnsafe() while a wrongly-bound Dispatcher.UIThread makes it throw.
        // The result is captured rather than discarded so the guard test can assert it.
        ApplicationBuiltDuringAssemblyInitialize = session
            .Dispatch(() => Application.Current is not null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }
}
