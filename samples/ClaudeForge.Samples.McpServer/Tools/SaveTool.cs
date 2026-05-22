using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Samples.McpServer.Tools;

/// <summary>
/// <c>save</c> — runs schema validation and persists dirty documents to disk.
/// Demonstrates <see cref="IClaudeConfigClient.SaveAsync"/> and the
/// <see cref="SchemaValidationException"/> error path.
/// </summary>
internal static class SaveTool
{
    public static async Task<int> RunAsync(IClaudeConfigClient client, string[] args, CancellationToken ct)
    {
        _ = args;
        try
        {
            await client.SaveAsync(force: false, ct).ConfigureAwait(false);
            Console.WriteLine("saved.");
            return 0;
        }
        catch (SchemaValidationException ex)
        {
            // Surface validation errors as a structured listing — an MCP host
            // would route these into the tool's response schema. Here we just
            // print one per line.
            await Console.Error.WriteLineAsync($"validation failed ({ex.Errors.Count} errors):");
            foreach (SchemaValidationError err in ex.Errors)
            {
                await Console.Error.WriteLineAsync($"  {err.DisplayPath}: {err.Message}");
            }

            return 3;
        }
    }
}