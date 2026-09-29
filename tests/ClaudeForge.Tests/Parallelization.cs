// ClaudeForge.Tests uses Avalonia.Headless (HeadlessUnitTestSession +
// [assembly: AvaloniaTestApplication]), which runs ONE serial dispatcher thread
// per assembly. Running its test classes/methods in parallel wedges the test
// host (the headless dispatcher is not concurrent-safe), so this assembly MUST
// stay serial. DoNotParallelize enforces that even if a global .runsettings
// enables parallelization. See docs/ASYNC-FIRST-MIGRATION-PLAN.md / the
// test-parallelism notes.
//
// xUnit v4 replaced CollectionBehavior(DisableTestParallelization = true) with this attribute; the
// old property is obsolete-as-error there. ParallelMode.None is the same setting: no two test
// collections run at once.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
