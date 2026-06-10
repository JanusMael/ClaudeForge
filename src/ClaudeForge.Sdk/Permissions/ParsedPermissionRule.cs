using System.Diagnostics.CodeAnalysis;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;

/// <summary>
/// A permission rule decomposed into its structural parts — tool name, optional
/// specifier, and MCP fields — so the matching layer can evaluate it against a
/// candidate tool call.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is separate from <see cref="PermissionRule"/>.</b>
/// <see cref="PermissionRule.TryParse"/> is the editor's *shape gate*: it is
/// deliberately strict (it rejects, for example, <c>Bash(*)</c> because the
/// bundled schema regex requires non-wildcard content inside the parens) so the
/// guided editor steers users toward well-formed new rules. This type is the
/// *evaluation* parser: it is intentionally permissive so the dry-run tester can
/// faithfully decompose whatever already lives in a user's <c>settings.json</c>
/// — including forms Claude Code accepts but the strict gate would reject (e.g.
/// <c>Bash(*)</c>, which Claude Code treats as equivalent to bare <c>Bash</c>).
/// Editor validation and config evaluation are different jobs; conflating them
/// would either weaken input guidance or break faithful matching.
/// </para>
/// <para>
/// <b>Spec.</b> Rule shape and the MCP forms are defined by the Claude Code
/// permissions reference:
/// <see href="https://code.claude.com/docs/en/permissions">code.claude.com/docs/en/permissions</see>
/// (§"Permission rule syntax" and §"Tool-specific permission rules"). A rule is
/// <c>Tool</c>, <c>Tool(specifier)</c>, or an <c>mcp__</c>-prefixed identifier.
/// </para>
/// </remarks>
public sealed record ParsedPermissionRule
{
    private ParsedPermissionRule(
        string raw,
        string toolName,
        string? specifier,
        bool isBareTool,
        bool isMcp,
        string? mcpServer,
        string? mcpTool,
        bool mcpAllTools)
    {
        Raw = raw;
        ToolName = toolName;
        Specifier = specifier;
        IsBareTool = isBareTool;
        IsMcp = isMcp;
        McpServer = mcpServer;
        McpTool = mcpTool;
        McpAllTools = mcpAllTools;
    }

    /// <summary>The original, untrimmed rule string this was parsed from.</summary>
    public string Raw { get; }

    /// <summary>
    /// The tool name (<c>"Bash"</c>, <c>"Read"</c>, <c>"WebFetch"</c>, …). Empty
    /// string for <see cref="IsMcp"/> rules, which carry their identity in
    /// <see cref="McpServer"/> / <see cref="McpTool"/> instead.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// The content inside the parentheses (<c>"git push *"</c> for
    /// <c>Bash(git push *)</c>), or <see langword="null"/> for a bare tool name
    /// or an MCP rule.
    /// </summary>
    public string? Specifier { get; }

    /// <summary>
    /// <see langword="true"/> when the rule is a bare tool name with no
    /// specifier (e.g. <c>Bash</c>, <c>Read</c>). Per the spec a bare tool name
    /// matches every use of that tool.
    /// </summary>
    public bool IsBareTool { get; }

    /// <summary><see langword="true"/> for an <c>mcp__</c>-prefixed rule.</summary>
    public bool IsMcp { get; }

    /// <summary>
    /// For an MCP rule, the server name (<c>"puppeteer"</c> for
    /// <c>mcp__puppeteer__navigate</c>); otherwise <see langword="null"/>.
    /// </summary>
    public string? McpServer { get; }

    /// <summary>
    /// For an MCP rule that targets one specific tool, the tool name
    /// (<c>"navigate"</c> for <c>mcp__puppeteer__navigate</c>). <see langword="null"/>
    /// when the rule targets the whole server (<see cref="McpAllTools"/>).
    /// </summary>
    public string? McpTool { get; }

    /// <summary>
    /// <see langword="true"/> when an MCP rule matches every tool from its
    /// server — i.e. the <c>mcp__server</c> and <c>mcp__server__*</c> forms,
    /// which the spec treats as equivalent.
    /// </summary>
    public bool McpAllTools { get; }

    /// <summary>
    /// <see langword="true"/> when this rule matches every use of its tool —
    /// either a bare tool name or the <c>Tool(*)</c> form (the spec states
    /// <c>Bash(*)</c> is equivalent to bare <c>Bash</c>), or an MCP
    /// whole-server rule.
    /// </summary>
    public bool MatchesAllUses =>
        IsBareTool || (IsMcp && McpAllTools) || Specifier == "*";

    /// <summary>
    /// Structurally decompose <paramref name="raw"/>. Permissive by design (see
    /// the type remarks): it accepts any <c>Tool</c> / <c>Tool(specifier)</c> /
    /// <c>mcp__…</c> shape and never throws for content reasons.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown only when <paramref name="raw"/> is null, empty, or whitespace —
    /// i.e. structurally not a rule at all.
    /// </exception>
    public static ParsedPermissionRule Parse(string raw)
    {
        if (!TryParse(raw, out ParsedPermissionRule? parsed))
        {
            throw new ArgumentException(
                $"'{raw}' is empty or not a structural permission rule.", nameof(raw));
        }

        return parsed;
    }

    /// <summary>
    /// Attempt to structurally decompose <paramref name="raw"/>. Returns
    /// <see langword="false"/> only when the input is null/empty/whitespace.
    /// </summary>
    public static bool TryParse(string? raw, [NotNullWhen(true)] out ParsedPermissionRule? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string s = raw.Trim();

        // ── MCP form: mcp__server | mcp__server__* | mcp__server__tool ────────
        // Spec: code.claude.com/docs/en/permissions §"MCP". The bare-server and
        // "__*" forms both mean "all tools from this server"; a third segment
        // names one specific tool.
        if (s.StartsWith("mcp__", StringComparison.Ordinal))
        {
            // Split into at most three logical parts: "mcp", server, tool.
            // Tool names may themselves contain "__", so everything after the
            // second separator is rejoined as the tool segment.
            string afterPrefix = s["mcp__".Length..];
            string server;
            string? tool;
            int sep = afterPrefix.IndexOf("__", StringComparison.Ordinal);
            if (sep < 0)
            {
                server = afterPrefix;
                tool = null;
            }
            else
            {
                server = afterPrefix[..sep];
                tool = afterPrefix[(sep + "__".Length)..];
            }

            bool allTools = string.IsNullOrEmpty(tool) || tool == "*";
            parsed = new ParsedPermissionRule(
                raw: s,
                toolName: string.Empty,
                specifier: null,
                isBareTool: false,
                isMcp: true,
                mcpServer: server,
                mcpTool: allTools ? null : tool,
                mcpAllTools: allTools);
            return true;
        }

        // ── Tool(specifier) or bare Tool ─────────────────────────────────────
        int open = s.IndexOf('(');
        if (open < 0)
        {
            // Bare tool name — matches all uses of the tool.
            parsed = new ParsedPermissionRule(
                raw: s,
                toolName: s,
                specifier: null,
                isBareTool: true,
                isMcp: false,
                mcpServer: null,
                mcpTool: null,
                mcpAllTools: false);
            return true;
        }

        // Has an opening paren. Tool name is everything before it; specifier is
        // the content up to the last ')'. We tolerate a missing closing paren
        // (take the rest of the string) so a half-typed rule still decomposes
        // for live-preview purposes.
        string toolName = s[..open].Trim();
        int close = s.LastIndexOf(')');
        string specifier = close > open
            ? s[(open + 1)..close]
            : s[(open + 1)..];

        parsed = new ParsedPermissionRule(
            raw: s,
            toolName: toolName,
            specifier: specifier,
            isBareTool: false,
            isMcp: false,
            mcpServer: null,
            mcpTool: null,
            mcpAllTools: false);
        return true;
    }
}
