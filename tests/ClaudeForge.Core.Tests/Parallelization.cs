// ClaudeForge.Core.Tests is pure-logic (no Avalonia headless / dispatcher). Its
// process-global test seams — PlatformPaths.TestUserProfileOverride /
// TestAppBaseDirOverride — are AsyncLocal-backed, so concurrent tests stay isolated.
// The one exception is PlatformInfoTests, which mutates the process-wide
// PlatformInfo.Current by design and is therefore marked [DoNotParallelize].
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
