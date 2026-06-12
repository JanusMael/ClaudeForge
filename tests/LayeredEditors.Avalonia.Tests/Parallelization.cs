// Avalonia test assembly — kept explicitly serial (see ClaudeForge.Avalonia.Tests
// Parallelization.cs for rationale). Not validated as parallel-safe; only the
// pure-logic assemblies (Core.Tests, Sdk.Tests) opt in to [assembly: Parallelize].
[assembly: DoNotParallelize]
