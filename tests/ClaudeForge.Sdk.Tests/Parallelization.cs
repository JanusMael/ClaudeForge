// ClaudeForge.Sdk.Tests is pure-logic (no Avalonia headless / dispatcher), and its
// only process-global test seam — PlatformPaths.TestUserProfileOverride — is now
// AsyncLocal-backed, so concurrent tests stay isolated. Method-level parallelization
// is therefore safe and speeds the suite up.
//
// Contrast: the Avalonia/headless test assemblies (ClaudeForge.Tests and the
// LayeredEditors/Avalonia projects) are [assembly: DoNotParallelize] because
// Avalonia.Headless.HeadlessUnitTestSession runs a single serial dispatcher per
// assembly and wedges under parallel execution.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
