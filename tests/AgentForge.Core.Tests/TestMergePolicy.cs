using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

// Root test namespace on purpose: every sub-namespace here (Schema, Backup, FileIO, …)
// resolves it by walking outward, so no test file needs a using for it.
namespace Bennewitz.Ninja.AgentForge.Core.Tests;

/// <summary>
/// A merge policy for tests in this assembly, which must stay product-free —
/// <c>ClaudeMergePolicy</c> lives in <c>ClaudeForge.Sdk.Claude</c> and referencing it here
/// would be the layering inversion <c>AssemblyLayeringTests</c> exists to catch.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <b>not</b> a copy of Claude's path list. A second copy of that list in the
/// test tree would drift from the real one and start asserting its own past. These tests
/// name the paths they care about instead, which also makes each test state the rule it
/// depends on.
/// </para>
/// <para>
/// Tests that assert something about <i>Claude's</i> rules — which paths Claude declares —
/// belong in <c>ClaudeForge.Sdk.Claude.Tests</c> against <c>ClaudeMergePolicy</c> itself.
/// </para>
/// </remarks>
internal sealed class TestMergePolicy : IMergePolicy
{
    private readonly HashSet<string> _unionPaths;
    private readonly bool _inferFromValues;
    private readonly bool _unionEverything;

    private TestMergePolicy(
        IEnumerable<string> unionPaths,
        bool inferFromValues,
        MergeUnionOrder order,
        bool unionEverything = false)
    {
        _unionPaths = new HashSet<string>(unionPaths, StringComparer.Ordinal);
        _inferFromValues = inferFromValues;
        _unionEverything = unionEverything;
        UnionOrder = order;
    }

    /// <summary>
    /// Unions any path whose every scope value is an array, and nothing else. Mirrors the
    /// behaviour the engine had when a caller passed no array-path set at all.
    /// </summary>
    public static TestMergePolicy Inferring { get; } =
        new([], inferFromValues: true, MergeUnionOrder.HighestPriorityFirst);

    /// <summary>Never unions — every path is won outright by the highest-priority scope.</summary>
    public static TestMergePolicy NeverUnions { get; } =
        new([], inferFromValues: false, MergeUnionOrder.HighestPriorityFirst);

    /// <summary>Unions every path, whatever its values look like.</summary>
    public static TestMergePolicy AlwaysUnions { get; } =
        new([], inferFromValues: false, MergeUnionOrder.HighestPriorityFirst, unionEverything: true);

    /// <summary>
    /// <see cref="Inferring"/>, but concatenating unions from the lowest-priority scope up.
    /// Exercises the order OpenCode was measured to use (Spike S1) without an OpenCode
    /// policy existing yet.
    /// </summary>
    public static TestMergePolicy InferringLowestFirst { get; } =
        new([], inferFromValues: true, MergeUnionOrder.LowestPriorityFirst);

    /// <summary>
    /// Unions exactly the named paths — plus any all-array path, matching how a real policy
    /// combines a declared list with inference.
    /// </summary>
    public static TestMergePolicy Declaring(params string[] paths)
    {
        return new TestMergePolicy(paths, inferFromValues: true, MergeUnionOrder.HighestPriorityFirst);
    }

    /// <summary>
    /// Unions exactly the named paths and infers nothing, so an all-array path that is not
    /// named is replaced rather than unioned — the shape OpenCode needs.
    /// </summary>
    public static TestMergePolicy DeclaringOnly(params string[] paths)
    {
        return new TestMergePolicy(paths, inferFromValues: false, MergeUnionOrder.HighestPriorityFirst);
    }

    /// <inheritdoc/>
    public bool UnionsAt(string path, bool everyValueIsArray)
    {
        return _unionEverything
               || _unionPaths.Contains(path)
               || (_inferFromValues && everyValueIsArray);
    }

    /// <inheritdoc/>
    public MergeUnionOrder UnionOrder { get; }
}
