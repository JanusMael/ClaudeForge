// Nothing here touches a control or a dispatcher — these are plain ObservableObjects — but the
// assembly stays serial alongside the other Avalonia-named test projects rather than opting into
// [assembly: Parallelize]. Only the pure-logic assemblies (Core.Tests, Sdk.Tests) are validated as
// parallel-safe, and an editor project is exactly the one that grows a headless test later.
[assembly: DoNotParallelize]
