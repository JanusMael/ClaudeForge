// Pure string-in / string-out with no shared state, no filesystem, and no
// process-global seams — the safest possible case for method-level parallelism.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
