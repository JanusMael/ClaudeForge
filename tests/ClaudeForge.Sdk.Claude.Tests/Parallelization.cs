// Same reasoning as AgentForge.Sdk.Tests: pure-logic tests with no Avalonia
// headless session or dispatcher, and the one process-global seam
// (PlatformPaths.TestUserProfileOverride) is AsyncLocal-backed, so concurrent
// tests stay isolated. Method-level parallelization is safe here.
//
// Contrast: the Avalonia/headless assemblies (ClaudeForge.Tests and the
// LayeredEditors/Avalonia projects) are [assembly: DoNotParallelize] because
// Avalonia.Headless.HeadlessUnitTestSession runs a single serial dispatcher per
// assembly and wedges under parallel execution.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
