// Every test here dispatches onto ONE Avalonia headless session: one dispatcher thread and, under
// the PerAssembly isolation set in Headless/HeadlessSessionOwnership.cs, one Application for the
// whole process. Its queue is serial and the shared Application is process-global state, so two
// test classes running at once would interleave on both. This assembly MUST stay serial.
//
// xUnit v4 replaced CollectionBehavior(DisableTestParallelization = true) with this attribute; the
// old property is obsolete-as-error there. ParallelMode.None is the same setting: no two test
// collections run at once.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
