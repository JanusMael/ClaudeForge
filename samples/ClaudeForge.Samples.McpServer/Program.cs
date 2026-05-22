using System.Text;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Samples.McpServer.Tools;
using Bennewitz.Ninja.ClaudeForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Samples.McpServer;

/// <summary>
/// Headless example consumer of the <c>ClaudeForge.Sdk</c> v1 surface.
/// </summary>
/// <remarks>
/// <para>
/// This project exists to prove
/// that the SDK is Avalonia-free and consumable from a strictly headless
/// context (an MCP server, a CLI, a daemon, etc.). It builds with
/// <c>net10.0</c> and references only <c>ClaudeForge.Sdk</c>; if any future
/// change pulls Avalonia into the SDK's transitive closure, this project will
/// start failing to build with package-version conflicts.
/// </para>
/// <para>
/// The "MCP server" framing is illustrative — the real wire protocol layer
/// (stdio JSON-RPC, capability negotiation, etc.) is out of scope for this
/// sample.
/// </para>
/// <para>
/// Two run modes:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Single-command mode</b> — invoked with command-line args. Dispatches
///     once, prints the result, exits. Useful for shell automation but each
///     invocation is a fresh process; in-memory mutations vanish on exit
///     unless the same invocation also runs <c>save</c>.
///   </item>
///   <item>
///     <b>REPL mode</b> — invoked with no args. Reads command lines from stdin,
///     dispatches each against the same long-lived
///     <see cref="IClaudeConfigClient"/> instance. This is the closer
///     analogue to an MCP server: multiple tool calls share state, and the
///     consumer chooses when to invoke <c>save</c>. Exit with <c>quit</c>,
///     <c>exit</c>, or EOF (Ctrl-D / closed pipe).
///   </item>
/// </list>
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Single ClaudeCodeClient per process — simulates an MCP server's
        // long-lived per-session client. The SDK is thread-safe so concurrent
        // tool calls in a real MCP host route through a single instance.
        using CancellationTokenSource ctsRoot = new();
        Console.CancelKeyPress += (_, e) =>
        {
            // Ctrl-C exits cleanly: cancel the root token, suppress process
            // termination so the using-block can dispose the client.
            e.Cancel = true;
            ctsRoot.Cancel();
        };

        using ClaudeCodeClient client = new(defaultScope: ConfigScope.User);

        try
        {
            await client.OpenAsync(projectRoot: null, ct: ctsRoot.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"error opening workspace: {ex.GetType().Name}: {ex.Message}");
            return 2;
        }

        return args.Length > 0
            ? await DispatchOnceAsync(client, args, ctsRoot.Token).ConfigureAwait(false)
            : await RunReplAsync(client, ctsRoot.Token).ConfigureAwait(false);
    }

    private static async Task<int> DispatchOnceAsync(
        IClaudeConfigClient client,
        string[] args,
        CancellationToken ct)
    {
        try
        {
            return await DispatchAsync(client, args, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.GetType().Name}: {ex.Message}");
            return 2;
        }
    }

    private static async Task<int> RunReplAsync(IClaudeConfigClient client, CancellationToken ct)
    {
        await Console.Error.WriteLineAsync(
            "ClaudeForge.Samples.McpServer REPL — type 'help' for commands, 'quit' to exit.");
        while (!ct.IsCancellationRequested)
        {
            await Console.Error.WriteAsync("> ");
            string? line;
            try
            {
                line = Console.ReadLine();
            }
            catch (IOException)
            {
                line = null;
            }

            if (line is null)
            {
                break; // EOF
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line is "quit" or "exit")
            {
                break;
            }

            if (line is "help" or "?")
            {
                PrintUsage();
                continue;
            }

            string[] parts = SplitArgs(line);

            try
            {
                await DispatchAsync(client, parts, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"error: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return 0;
    }

    private static Task<int> DispatchAsync(IClaudeConfigClient client, string[] args, CancellationToken ct)
    {
        return args[0] switch
        {
            "get-effective" => GetEffectiveTool.RunAsync(client, args, ct),
            "set-value" => SetValueTool.RunAsync(client, args, ct),
            "save" => SaveTool.RunAsync(client, args, ct),
            "permissions-add-allow" => PermissionsTool.AddAllowAsync(client, args, ct),
            "permissions-list-allow" => PermissionsTool.ListAllowAsync(client, args, ct),
            "list-backups" => BackupTool.ListAsync(client, args, ct),
            "create-backup" => BackupTool.CreateAsync(client, args, ct),
            "restore-backup" => BackupTool.RestoreAsync(client, args, ct),
            var _ => Task.FromResult(UsageError($"Unknown command: {args[0]}")),
        };
    }

    /// <summary>
    /// Tiny argv splitter for REPL input. Splits on whitespace; preserves
    /// quoted runs so values containing spaces survive — e.g.
    /// <c>set-value model "claude opus 4"</c>.
    /// </summary>
    private static string[] SplitArgs(string line)
    {
        List<string> result = new();
        StringBuilder buffer = new();
        bool inQuote = false;
        foreach (char ch in line)
        {
            if (ch == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && char.IsWhiteSpace(ch))
            {
                if (buffer.Length > 0)
                {
                    result.Add(buffer.ToString());
                    buffer.Clear();
                }

                continue;
            }

            buffer.Append(ch);
        }

        if (buffer.Length > 0)
        {
            result.Add(buffer.ToString());
        }

        return result.ToArray();
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Claude Config MCP server (sample) — exercises ClaudeForge.Sdk.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Commands:");
        Console.Error.WriteLine("  get-effective <path>");
        Console.Error.WriteLine("  set-value <path> <value>");
        Console.Error.WriteLine("  save");
        Console.Error.WriteLine("  permissions-add-allow <rule>");
        Console.Error.WriteLine("  permissions-list-allow");
        Console.Error.WriteLine("  list-backups <directory>");
        Console.Error.WriteLine("  create-backup <directory>");
        Console.Error.WriteLine("  restore-backup <archive-path>");
        Console.Error.WriteLine("  help | quit");
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        PrintUsage();
        return 1;
    }
}