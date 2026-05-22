using Bennewitz.Ninja.ClaudeForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Samples.McpServer.Tools;

/// <summary>
/// <c>get-effective &lt;path&gt;</c> — reads the merged effective value at the
/// given dotted path and prints it to stdout. Demonstrates
/// <see cref="IClaudeConfigClient.GetEffective{T}(string)"/>.
/// </summary>
internal static class GetEffectiveTool
{
    public static Task<int> RunAsync(IClaudeConfigClient client, string[] args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (args.Length < 2)
        {
            Console.Error.WriteLine("error: get-effective requires a path argument.");
            return Task.FromResult(1);
        }

        string path = args[1];
        string? value = client.GetEffective<string>(path);
        if (value is null)
        {
            Console.WriteLine("(unset)");
        }
        else
        {
            Console.WriteLine(value);
        }

        return Task.FromResult(0);
    }
}