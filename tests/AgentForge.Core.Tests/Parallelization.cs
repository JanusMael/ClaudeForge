// AgentForge.Core.Tests is pure-logic (no Avalonia headless / dispatcher). Its
// process-global test seams — PlatformPaths.TestUserProfileOverride /
// TestAppBaseDirOverride — are AsyncLocal-backed, so concurrent tests stay isolated.
// The one exception is PlatformInfoTests, which mutates the process-wide
// PlatformInfo.Current by design and is therefore in the DoNotParallelize collection.
//
// ⓘ Converted by hand from MSTest's method-level [assembly: Parallelize] (plans/00006). xUnit has
// no method-level mode: it runs test CLASSES in parallel and a class's methods one at a time, so
// this is the same or less concurrency than before, never more. Stated explicitly although it is
// xUnit's default, so the assembly's choice stays visible where it always was.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerClass)]
