using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;

namespace Bennewitz.Ninja.ClaudeForge.Samples.McpServer.Tools;

/// <summary>
/// Permissions accessor demos:
/// <list type="bullet">
///   <item><c>permissions-add-allow &lt;rule&gt;</c></item>
///   <item><c>permissions-list-allow</c></item>
/// </list>
/// Demonstrates the strongly-typed <see cref="IPermissionsAccessor"/> +
/// <see cref="PermissionRule.Parse"/> validation surface.
/// </summary>
internal static class PermissionsTool
{
    public static Task<int> AddAllowAsync(IClaudeConfigClient client, string[] args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (args.Length < 2)
        {
            Console.Error.WriteLine("error: permissions-add-allow requires a rule string.");
            return Task.FromResult(1);
        }

        if (!PermissionRule.TryParse(args[1], out PermissionRule? rule))
        {
            Console.Error.WriteLine($"error: '{args[1]}' is not a valid permission rule.");
            return Task.FromResult(2);
        }

        client.Permissions.AddAllow(rule);
        Console.WriteLine($"added: {rule.Value}");
        return Task.FromResult(0);
    }

    public static Task<int> ListAllowAsync(IClaudeConfigClient client, string[] args, CancellationToken ct)
    {
        _ = args;
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<PermissionRule> rules = client.Permissions.Allow;
        if (rules.Count == 0)
        {
            Console.WriteLine("(no allow rules)");
            return Task.FromResult(0);
        }

        foreach (PermissionRule rule in rules)
        {
            Console.WriteLine(rule.Value);
        }

        return Task.FromResult(0);
    }
}