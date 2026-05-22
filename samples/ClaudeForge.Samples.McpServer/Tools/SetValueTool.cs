using Bennewitz.Ninja.ClaudeForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Samples.McpServer.Tools;

/// <summary>
/// <c>set-value &lt;path&gt; &lt;value&gt;</c> — writes a string value at the
/// given path to the client's <see cref="IClaudeConfigClient.DefaultScope"/>.
/// Demonstrates the generic escape hatch
/// <see cref="IClaudeConfigClient.SetValue{T}(string, T)"/>.
/// </summary>
/// <remarks>
/// Mutations land in memory; <c>save</c> writes them to disk.
/// </remarks>
internal static class SetValueTool
{
    public static Task<int> RunAsync(IClaudeConfigClient client, string[] args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (args.Length < 3)
        {
            Console.Error.WriteLine("error: set-value requires <path> <value>.");
            return Task.FromResult(1);
        }

        client.SetValue(args[1], args[2]);
        Console.WriteLine($"set {args[1]} = {args[2]}");
        return Task.FromResult(0);
    }
}