// Resolution is pure in-memory: the fake sources hold lists, with no static mutable state and no
// process-global seam. The filesystem source tests do touch disk, but each one creates its own
// randomly-named temp directory and reads nothing outside it — no shared root, no environment
// variable, no working-directory dependency. That is the same safest case AgentForge.Jsonc.Tests
// documents, so method-level parallelism applies here for the same reason.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
