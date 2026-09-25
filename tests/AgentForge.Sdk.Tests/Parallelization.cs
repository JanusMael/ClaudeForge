// AgentForge.Sdk.Tests is pure-logic (no Avalonia headless / dispatcher), and its
// only process-global test seam — PlatformPaths.TestUserProfileOverride — is now
// AsyncLocal-backed, so concurrent tests stay isolated. Parallelization is
// therefore safe and speeds the suite up.
//
// Contrast: the Avalonia/headless test assemblies (ClaudeForge.Tests and
// ClaudeForge.Avalonia.Tests) disable parallelization because
// Avalonia.Headless.HeadlessUnitTestSession runs a single serial dispatcher per
// assembly and wedges under parallel execution.
//
// ⓘ Converted by hand from MSTest's method-level [assembly: Parallelize] (plans/00006). xUnit has
// no method-level mode: it runs test CLASSES in parallel and a class's methods one at a time, so
// this is the same or less concurrency than before, never more. Stated explicitly although it is
// xUnit's default, so the assembly's choice stays visible where it always was.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerClass)]
