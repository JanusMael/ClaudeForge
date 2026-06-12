// Avalonia test assembly — kept explicitly serial. Avalonia view-model/control
// tests can touch process-wide UI framework state, and have not been validated as
// safe under MSTest class/method parallelization. DoNotParallelize preserves the
// current sequential behavior and guards against a global .runsettings enabling
// parallelization. Only the pure-logic assemblies (Core.Tests, Sdk.Tests) opt in
// to [assembly: Parallelize].
[assembly: DoNotParallelize]
