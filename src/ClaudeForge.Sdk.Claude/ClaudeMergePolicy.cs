using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

/// <summary>
/// Claude's documented settings merge rules: the paths below union across scopes, every
/// other array-valued path unions too, and the highest-priority scope wins everything else.
/// </summary>
/// <remarks>
/// <para>
/// Serves <b>both</b> Claude products. Claude Desktop's config declares no array paths at
/// all — its only keys are <c>preferences</c> and <c>mcpServers</c>, both objects — so none
/// of the names below can match one of its documents, and sharing this policy reproduces
/// today's behaviour exactly rather than approximating it.
/// </para>
/// <para>
/// <b>This list lived in <c>SettingsWorkspace</c>, in the product-neutral core.</b> Nothing
/// forced it there; it was simply never asked whose rules it encoded. It is Claude's, so it
/// now sits beside Claude's clients, and a workspace is handed a policy instead of owning one.
/// </para>
/// <para>
/// <b>Union-ness is inferred for undeclared paths</b> — <see cref="UnionsAt"/> returns true
/// when every scope holds an array there, even if the path is absent from the list. That is
/// the pre-existing behaviour and it is deliberate for Claude: the list names what the
/// schema documents, and an undeclared array is more likely to be an omission than an
/// override-me scalar. It is also exactly what OpenCode must <i>not</i> do — replacing is
/// its default for arrays it has not listed (Spike S1) — which is why the inference is a
/// policy decision and not something the engine does on its own.
/// </para>
/// </remarks>
public sealed class ClaudeMergePolicy : IMergePolicy
{
    /// <summary>
    /// Shared instance. The policy is immutable and holds no per-client state, so both
    /// clients and every workspace they load can use one.
    /// </summary>
    public static readonly ClaudeMergePolicy Instance = new();

    // Array paths per Claude Code documentation — these MERGE across scopes rather than override.
    private static readonly HashSet<string> ArrayPaths = new(StringComparer.Ordinal)
    {
        "claudeMdExcludes",
        "availableModels",
        "httpHookAllowedEnvVars",
        "allowedHttpHookUrls",
        "permissions.allow",
        "permissions.deny",
        "permissions.ask",
        "permissions.additionalDirectories",
        "enabledMcpjsonServers",
        "disabledMcpjsonServers",
        "companyAnnouncements",
    };

    /// <inheritdoc/>
    public bool UnionsAt(string path, bool everyValueIsArray)
    {
        return ArrayPaths.Contains(path) || everyValueIsArray;
    }

    /// <inheritdoc/>
    public MergeUnionOrder UnionOrder => MergeUnionOrder.HighestPriorityFirst;
}
