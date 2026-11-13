// Serial, like ClaudeForge.Tests, which every test here either came from or is modelled on
// (plans/00008 step 4). Those tests have only ever run one at a time, so whether they are safe to
// interleave has never been measured, and the view models under test raise events, read and write
// files and reach process-wide seams. A suite that is fast and occasionally wrong is worse than a
// slow one; parallelize only after measuring it.
//
// Contrast: AgentForge.Sdk.Tests runs classes in parallel because its one process-global seam is
// AsyncLocal-backed; see its Parallelization.cs.
//
// ParallelMode.None is xUnit v4's form of the obsolete CollectionBehavior(DisableTestParallelization
// = true): no two test collections run at once.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
