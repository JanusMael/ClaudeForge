// Avalonia test assembly — kept explicitly serial. Avalonia view-model/control
// tests can touch process-wide UI framework state, and have not been validated as
// safe under MSTest class/method parallelization. DoNotParallelize preserves the
// current sequential behavior and guards against a global .runsettings enabling
// parallelization. Only the pure-logic assemblies (Core.Tests, Sdk.Tests) opt in
// to [assembly: Parallelize].
//
// xUnit v4 replaced CollectionBehavior(DisableTestParallelization = true) with this attribute; the
// old property is obsolete-as-error there. ParallelMode.None is the same setting: no two test
// collections run at once.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
