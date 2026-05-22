using System.Text;
using System.Text.RegularExpressions;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Services;

/// <summary>
/// Translates JsonSchema.Net validation errors into user-actionable messages.
/// Each schema-error pattern that bites real users gets a dedicated branch
/// here; everything else falls through to the raw <see cref="SchemaValidationError.Message"/>.
/// </summary>
/// <remarks>
/// Extracted from <c>MainWindowViewModel</c> on 2026-05-01 so the
/// translation table can be exercised by direct unit tests instead of
/// going through the full save-flow integration. The MWVM keeps a thin
/// passthrough at the call site.
/// </remarks>
internal static partial class SchemaErrorMessages
{
    /// <summary>
    /// Render a list of validation errors as a human-readable block,
    /// grouped by file and prefixed by a one-line count summary.
    /// </summary>
    public static string Format(IReadOnlyList<SchemaValidationError> errors)
    {
        StringBuilder sb = new();
        sb.AppendLine(errors.Count == 1
            ? "1 validation error was found. Fix the issue below and try saving again."
            : $"{errors.Count} validation errors were found. Fix the issues below and try saving again.");

        foreach (IGrouping<string, SchemaValidationError> group in errors.GroupBy(e => e.FilePath))
        {
            sb.AppendLine();
            sb.AppendLine(Path.GetFileName(group.Key) + ":");
            foreach (SchemaValidationError err in group)
            {
                sb.AppendLine($"  • {err.DisplayPath}: {Friendly(err)}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Pre-rendered single-error format used by <see cref="Format"/>; also
    /// the seam tests target directly to lock individual error translations.
    /// </summary>
    public static string Friendly(SchemaValidationError err)
    {
        string[] segments = err.InstancePath.TrimStart('/').Split('/');

        // Permission rule errors — path format: /permissions/(allow|deny|ask)/<index>
        if (segments.Length >= 3 &&
            string.Equals(segments[0], "permissions", StringComparison.OrdinalIgnoreCase) &&
            segments[1] is "allow" or "deny" or "ask")
        {
            return "Invalid permission rule syntax. "
                   + "Rules must be a known tool name (e.g. Bash, Edit, Read, Write) "
                   + "optionally followed by a specific argument in parentheses. "
                   + "Note: Bash(*) is not valid — use just Bash to allow all invocations, "
                   + "or specify a real pattern like Bash(git *) or Bash(npm run *).";
        }

        // Unknown hook event — path format: /hooks/<EventName> with the
        // raw JsonSchema.Net message "All values fail against the false
        // schema" coming from additionalProperties:false on the /hooks
        // object. Bites users who pick a name the schema doesn't know
        // (the GUI's KnownEventTypes list previously offered tool-suffixed
        // pseudo-events like "PreBashToolUse" that Claude Code never
        // accepted; legacy on-disk configs may still carry them). The most
        // common intent is "fire on a specific tool", which is expressed by
        // the matcher field on PreToolUse/PostToolUse — detect the
        // Pre<Tool>ToolUse / Post<Tool>ToolUse pattern and offer that
        // exact rewrite as the suggested fix.
        if (segments.Length == 2 &&
            string.Equals(segments[0], "hooks", StringComparison.OrdinalIgnoreCase) &&
            err.Message.Contains("false schema", StringComparison.OrdinalIgnoreCase))
        {
            string eventName = segments[1];
            Match match = MyRegex().Match(eventName);
            if (match.Success && match.Groups[2].Value is { Length: > 0 } tool)
            {
                return $"'{eventName}' is not a recognised hook event. "
                       + $"To run a hook before/after a specific tool, use '{match.Groups[1].Value}ToolUse' "
                       + $"with matcher '{tool}' instead.";
            }

            return $"'{eventName}' is not a recognised hook event. "
                   + "Pick one of the standard event names from the editor's event list "
                   + "(PreToolUse, PostToolUse, Stop, SessionStart, etc.).";
        }

        return err.Message;
    }

    [GeneratedRegex(@"^(Pre|Post)(\w+?)ToolUse$", RegexOptions.CultureInvariant)]
    private static partial Regex MyRegex();
}