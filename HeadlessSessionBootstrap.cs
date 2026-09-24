using System.Reflection;

using Avalonia;
using Avalonia.Headless;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bennewitz.Ninja.TestSupport.Headless;

/// <summary>
/// Starts the assembly's shared <see cref="HeadlessUnitTestSession"/> <em>and forces the Avalonia
/// application to be built</em>, once, before any test in that assembly runs.
///
/// <para>
/// ⭐ <b>This file is LINKED, never copied</b> — every headless test project compiles
/// <c>../../HeadlessSessionBootstrap.cs</c>, the same way they all link
/// <c>AssemblyInfo.InternalsVisibleTo.cs</c>. One file that is obviously complete, in place of
/// one per project that are individually correct and collectively unknowable.
/// <c>Assembly.GetExecutingAssembly()</c> resolves per compiled assembly, which is exactly what
/// makes a single linked file correct here: each assembly warms up <em>its own</em> session.
/// </para>
///
/// <para>
/// Every headless fixture reaches the session through
/// <c>HeadlessUnitTestSession.GetOrStartForAssembly(...)</c>, which starts it lazily on first use.
/// That makes the <em>starting</em> of the Avalonia platform the responsibility of whichever test
/// happens to be scheduled first — which differs between runs and between operating systems.
/// </para>
/// <para>
/// The cost of that showed up the first time CI ran on macOS: platform start-up threw
/// <c>InvalidOperationException: The calling thread cannot access this object because a different
/// thread owns it</c> from <c>Compositor..ctor</c> → <c>DefaultRenderLoop.Add</c> →
/// <c>Dispatcher.VerifyAccess</c>, and the failure was reported against
/// <c>StatusControllerTests.Set_SuccessKind_AutoClearsAfterDelay</c> — a status-bar assertion with
/// nothing whatever to do with compositor construction. A start-up fault attributed to an
/// arbitrary unrelated assertion is close to undiagnosable from a CI log.
/// </para>
/// <para>
/// ⛔⛔ <b>CORRECTED 2026-09-24 — the causal model below was FALSE when written (PR #74), and this
/// type does not do what its summary says.</b> Per Avalonia 12.1.3's own source (<c>Headless/Avalonia.Headless/HeadlessUnitTestSession.cs</c> in the AvaloniaUI/Avalonia repository): with no <c>[AvaloniaTestIsolation]</c> on the assembly — and this repository sets none — the isolation level defaults to <c>PerTest</c>, and under <c>PerTest</c> EVERY <c>Dispatch</c> runs <c>EnsureIsolatedApplication()</c>: <c>Dispatcher.ResetBeforeUnitTests()</c>, then <c>AppBuilder.SetupUnsafe()</c>.
/// The application is rebuilt for every test, so there is no single first build for a warm-up to
/// move, and the warm-up changes nothing on the failing path. Proof beyond the source: the same
/// exception recurred on CI for PR #76 (Windows) after this merged, and
/// <c>HeadlessSessionBootstrapTests</c> passed in that run. The failure is still OPEN; the
/// candidate fix — <c>[assembly: AvaloniaTestIsolation(PerAssembly)]</c> — is unverified and
/// being tried on its own branch. The paragraphs below are kept as the record of what was believed.
/// </para>
/// <para>
/// ⛔⛔ <b><c>GetOrStartForAssembly</c> ALONE DOES NOT PREVENT THAT, and the original per-project
/// version of this file claimed it did for three weeks.</b> It starts the session object and its
/// dispatcher <em>thread</em>; it does not build the Avalonia application.
/// <c>HeadlessUnitTestSession.EnsureIsolatedApplication()</c> — the call that runs
/// <c>AppBuilder.SetupUnsafe()</c> and constructs the compositor — is invoked lazily from
/// <c>DispatchCore</c>, i.e. on the <b>first <c>Dispatch</c></b>. So application set-up stayed the
/// first-scheduled test's responsibility, which is the exact ordering this type exists to remove.
/// </para>
/// <para>
/// ⭐ <b>Measured, not reasoned.</b> ⛔ <i>(False as a conclusion — see the correction above: the
/// stack was real, but <c>SetupUnsafe</c> runs inside EVERY test's dispatch under <c>PerTest</c>, so
/// it shows nothing about set-up having been skipped.)</i> On 2026-09-22 CI caught the exception again, reported against
/// <c>SchemaProvenanceBadgeTests.ClaudeCode_FallenBackToBundled_SaysTheFetchWasTried</c>, on a pull
/// request whose whole diff was a Markdown file. Its stack has <c>EnsureIsolatedApplication</c> →
/// <c>SetupUnsafe</c> → <c>Compositor..ctor</c> running <b>inside that test's <c>Dispatch</c></b> —
/// which it could only do if set-up had not already happened. The same job passed in the duplicate
/// CI run of the identical commit.
/// </para>
/// <para>
/// ⚠ <b>Why a wrong thread at all:</b> <c>Dispatcher.UIThread</c> is a lazily-resolved
/// process-global singleton that binds to whichever thread touches it first — the same property
/// <c>LiveLogWindow</c> documents for the shipping app, where constructing any
/// <c>AvaloniaObject</c> too early "forces <c>Dispatcher.UIThread</c> to resolve". In a full-suite
/// run a non-headless test that touches Avalonia can bind it to the MSTest thread; the session
/// thread then builds the compositor and <c>VerifyAccess</c> fails. In an isolated class run
/// nothing gets there first, which is why affected classes are green alone and flaky together.
/// </para>
/// <para>
/// ⛔ <i>(False — under <c>PerTest</c> it is not load-bearing; see the correction above.)</i>
/// So the warm-up <c>Dispatch</c> below is the load-bearing line, not the
/// <c>GetOrStartForAssembly</c> above it: it makes the session thread the first toucher,
/// deterministically, on the one code path MSTest guarantees runs before every test.
/// </para>
/// <para>
/// This complements, and does not replace, each project's <c>[assembly: DoNotParallelize]</c>:
/// that keeps the single headless dispatcher from being driven concurrently once it is up; this
/// decides when it comes up.
/// </para>
/// <para>
/// ⚠ <b>Only link this into a project that actually has an <c>[AvaloniaTestApplication]</c>.</b>
/// Without one there is no application to build, and this would turn a working assembly's
/// <c>[AssemblyInitialize]</c> into a hard failure. <c>AgentForge.Sdk.Tests</c> and
/// <c>ClaudeForge.Sdk.Claude.Tests</c> mention the session in a comment only and are deliberately
/// NOT linked.
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

        // ⛔ CORRECTED 2026-09-24: under PerTest isolation (the default; this repository sets none)
        // every Dispatch rebuilds the application, so this one is not load-bearing. Kept until the
        // PerAssembly trial decides between making it real and deleting the bootstrap.
        // Was: DO NOT DELETE. This dispatch is what builds the application — see the type remarks.
        // Without it the session exists but Avalonia does not, and the first test to dispatch
        // pays for SetupUnsafe() while a wrongly-bound Dispatcher.UIThread makes it throw.
        // The result is captured rather than discarded so the guard test can assert it.
        ApplicationBuiltDuringAssemblyInitialize = session
            .Dispatch(() => Application.Current is not null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }
}
