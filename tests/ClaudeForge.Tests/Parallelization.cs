// ClaudeForge.Tests stays serial. Its tests share process-global seams (DebugFlags, sandboxed
// temp settings files, Avalonia's lazily-bound Dispatcher.UIThread) that two classes running at
// once would interleave on. The headless-session tests that first made this assembly serial moved
// to ClaudeForge.Headless.Tests; see its csproj Description for why they live apart.
//
// xUnit v4 replaced CollectionBehavior(DisableTestParallelization = true) with this attribute; the
// old property is obsolete-as-error there. ParallelMode.None is the same setting: no two test
// collections run at once.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
