// Resolution is pure in-memory: the fake sources hold lists, with no static mutable state and no
// process-global seam. The filesystem source tests do touch disk, but each one creates its own
// randomly-named temp directory and reads nothing outside it — no shared root, no environment
// variable, no working-directory dependency. That is the same safest case JsonC.Tests
// documents, so parallelism applies here for the same reason.
//
// ⓘ Converted by hand from MSTest's method-level [assembly: Parallelize] (plans/00006). xUnit has
// no method-level mode: it runs test CLASSES in parallel and a class's methods one at a time, so
// this is the same or less concurrency than before, never more. Stated explicitly although it is
// xUnit's default, so the assembly's choice stays visible where it always was.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerClass)]
