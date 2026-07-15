// Avalonia diagnostics test assembly — kept explicitly serial (see
// ClaudeForge.Avalonia.Tests Parallelization.cs for rationale). It also exercises
// a rolling-file sink with process-level file/timing state. Not validated as
// parallel-safe; only Core.Tests / Sdk.Tests opt in to [assembly: Parallelize].
[assembly: DoNotParallelize]
