// Pure string-in / string-out with no shared state, no filesystem, and no
// process-global seams — the safest possible case for parallelism.
//
// ⓘ Converted by hand from MSTest's method-level [assembly: Parallelize] (plans/00006). xUnit has
// no method-level mode: it runs test CLASSES in parallel and a class's methods one at a time, so
// this is the same or less concurrency than before, never more. Stated explicitly although it is
// xUnit's default, so the assembly's choice stays visible where it always was.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerClass)]
