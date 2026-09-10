using Bennewitz.Ninja.AgentForge.Core.Schema;
using Serilog;

namespace Bennewitz.Ninja.OpenCodeForge.Services;

/// <summary>
/// Runtime switches parsed from the command line at startup.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>Program.ApplySchemaSourceOverride</c> when a second flag arrived, which is
/// exactly what that method's own comment said to do: <i>"One flag, deliberately not a flag
/// framework … when a second flag arrives, extract it then — and the shape to copy is
/// ClaudeForge's, not this."</i>
/// </para>
/// <para>
/// ⭐ <b>Simpler than ClaudeForge's on one axis, and that is not an oversight.</b> That one
/// parses BEFORE Serilog is configured, because <c>--culture</c> affects logging itself — so it
/// has to queue rejections in a deferred-warnings list and flush them once the log exists. This
/// app has no flag that affects logging, so it parses after and logs immediately. If a
/// culture-shaped flag is ever added here, the deferral comes with it.
/// </para>
/// <para>
/// ⚠ <b>Flags PEEK, they do not consume.</b> Every argument still reaches Avalonia. A two-token
/// flag whose value is rejected must leave both tokens alone rather than swallow them, because a
/// rejected value may itself be a valid argument to something else.
/// </para>
/// <para>
/// <b>Adding a flag</b> means touching <see cref="Initialize"/>, <see cref="ListActive"/>,
/// <see cref="ResetForTesting"/> and the <c>--debug-help</c> text in <see cref="HelpText"/> —
/// all four, or the flag works but is invisible to the person trying to find it.
/// </para>
/// </remarks>
internal static class DebugFlags
{
    /// <summary>
    /// <see langword="true"/> when <c>--simulate-update</c> was passed: the update check
    /// synthesises a newer release instead of calling GitHub.
    /// </summary>
    /// <remarks>
    /// Exercises the banner and the About-dialog button in one session, without publishing a
    /// release. The synthesised link lands on a 404 by design — there is no such tag.
    /// </remarks>
    internal static bool SimulateUpdate { get; private set; }

    /// <summary>Whether <see cref="Initialize"/> has run, so <c>--debug-help</c> can report.</summary>
    private static bool _initialized;

    /// <summary>What <c>--debug-help</c> prints.</summary>
    private const string HelpText = """
        OpenCodeForge debug flags:
          --schema-source <bundled|fetched>  Force one branch of the schema loading chain.
                                             'fetched' is FATAL if the fetch fails — it does
                                             not fall back, because a run that silently used
                                             bundled would prove nothing.
          --simulate-update                  Pretend a newer release exists, so the update
                                             banner and the About dialog's check can be
                                             exercised without publishing one.
          --debug-help                       Print this and continue.
        """;

    /// <summary>
    /// Parse <paramref name="args"/> and configure the process.
    /// </summary>
    /// <remarks>
    /// Runs after logging is configured, so rejections are logged where someone will see them.
    /// Never throws: an unrecognised or malformed flag warns and the app starts normally.
    /// </remarks>
    internal static void Initialize(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _initialized = true;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (string.Equals(arg, "--simulate-update", StringComparison.OrdinalIgnoreCase))
            {
                SimulateUpdate = true;
                Log.Information(
                    "[DebugFlags] --simulate-update: the update check will synthesise a newer "
                    + "release and will not call GitHub");
                continue;
            }

            if (string.Equals(arg, "--debug-help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--help-debug", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("[DebugFlags] {Help}", HelpText);
                continue;
            }

            // ── Two-token flags ────────────────────────────────────────────────────
            // The index advances only on a RECOGNISED value. Advancing on a rejected
            // one would consume an argument this app does not own.
            if (string.Equals(arg, "--schema-source", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    Log.Warning(
                        "[DebugFlags] --schema-source needs a value (bundled or fetched). "
                        + "Using the normal loading chain.");
                    continue;
                }

                ApplySchemaSource(args[i + 1]);
                continue;
            }
        }
    }

    private static void ApplySchemaSource(string value)
    {
        if (string.Equals(value, "bundled", StringComparison.OrdinalIgnoreCase))
        {
            SchemaRegistry.ProcessSourceOverride = SchemaSourceOverride.Bundled;
            Log.Information("[DebugFlags] --schema-source bundled: the network will not be used");
        }
        else if (string.Equals(value, "fetched", StringComparison.OrdinalIgnoreCase))
        {
            SchemaRegistry.ProcessSourceOverride = SchemaSourceOverride.Fetched;
            Log.Information(
                "[DebugFlags] --schema-source fetched: a failed fetch will be FATAL, not fall back");
        }
        else
        {
            Log.Warning(
                "[DebugFlags] --schema-source {Value} rejected: expected bundled or fetched. "
                + "Using the normal loading chain.", value);
        }
    }

    /// <summary>A one-line summary of what is active, for the startup log.</summary>
    /// <returns>An empty string when nothing is set, so the caller can skip the line.</returns>
    internal static string ListActive()
    {
        List<string> active = [];

        if (SimulateUpdate)
        {
            active.Add("--simulate-update");
        }

        if (SchemaRegistry.ProcessSourceOverride is { } source)
        {
            active.Add($"--schema-source {source.ToString().ToLowerInvariant()}");
        }

        return active.Count == 0 ? string.Empty : string.Join(' ', active);
    }

    /// <summary>Clear every flag so a test starts from a known state.</summary>
    /// <remarks>
    /// ⚠ Also clears <see cref="SchemaRegistry.ProcessSourceOverride"/>, which is
    /// PROCESS-GLOBAL. A test that sets it must be <c>[DoNotParallelize]</c>d — leaving it set
    /// makes every concurrently-constructed registry in the assembly load from the forced
    /// source, which is how two tests once passed alone and failed together.
    /// </remarks>
    internal static void ResetForTesting()
    {
        SimulateUpdate = false;
        SchemaRegistry.ProcessSourceOverride = null;
        _initialized = false;
    }

    /// <summary>Whether <see cref="Initialize"/> has run this process.</summary>
    internal static bool IsInitialized => _initialized;
}
