using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

// A copy of AgentForge.Sdk.Tests' TestMergePolicy (plans/00008 step 4 copies helpers rather than
// moving them, so each test project keeps its own).
namespace Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Tests.TestSupport;

/// <summary>
/// The merge policy this assembly's tests hand to a workspace or to
/// <see cref="TestConfigClient"/>. Local for the same reason that client is local: the real
/// policy is <c>ClaudeMergePolicy</c> in <c>ClaudeForge.Sdk.Claude</c>, and referencing a
/// product from an <c>AgentForge.*</c> project is the inversion
/// <c>AssemblyLayeringTests</c> exists to catch.
/// </summary>
/// <remarks>
/// Unions a path when every scope holds an array there and nothing else — the behaviour the
/// engine had before merge rules became a product's own statement. Deliberately not a copy
/// of Claude's declared path list: a second copy in the test tree would drift. Tests that
/// care which paths Claude declares belong in <c>ClaudeForge.Sdk.Claude.Tests</c> against
/// the real policy.
/// </remarks>
internal sealed class TestMergePolicy : IMergePolicy
{
    /// <summary>Shared instance — the policy is stateless.</summary>
    public static readonly TestMergePolicy Instance = new();

    /// <inheritdoc/>
    public bool UnionsAt(string path, bool everyValueIsArray)
    {
        return everyValueIsArray;
    }

    /// <inheritdoc/>
    public MergeUnionOrder UnionOrder => MergeUnionOrder.HighestPriorityFirst;
}
