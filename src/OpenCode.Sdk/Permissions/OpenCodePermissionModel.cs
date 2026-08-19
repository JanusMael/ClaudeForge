using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Permissions;

namespace Bennewitz.Ninja.OpenCode.Sdk.Permissions;

/// <summary>One <c>pattern → action</c> entry within a tool's rule set.</summary>
/// <param name="Pattern">The glob, as written. Not expanded or normalised — it is round-tripped verbatim.</param>
/// <param name="Action">Allow, Ask or Deny. Never <see cref="PermissionOutcome.Default"/>: that is a resolution result, not something a rule can say.</param>
public sealed record OpenCodePermissionRule(string Pattern, PermissionOutcome Action);

/// <summary>
/// A tool's permission setting: either one action for every invocation, or an <b>ordered</b>
/// list of pattern rules.
/// </summary>
/// <param name="SingleAction">Set when the tool's value was a bare action string.</param>
/// <param name="Rules">Set when the tool's value was an object. Order is preserved and load-bearing.</param>
public sealed record OpenCodeToolPermission(
    PermissionOutcome? SingleAction,
    IReadOnlyList<OpenCodePermissionRule> Rules)
{
    /// <summary>A tool whose value was a bare action.</summary>
    public static OpenCodeToolPermission Single(PermissionOutcome action) => new(action, []);

    /// <summary>A tool whose value was a pattern object.</summary>
    public static OpenCodeToolPermission Patterns(IReadOnlyList<OpenCodePermissionRule> rules)
        => new(null, rules);
}

/// <summary>
/// OpenCode's <c>permission</c> configuration, and the resolution rules that make it mean
/// something.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>KEY ORDER IS SEMANTICS, NOT FORMATTING.</b> The vendor spec is explicit: within a
/// permission object the <b>last</b> matching rule wins, so broad rules go first and narrow
/// rules last. An editor that re-serialises this map alphabetically, or that removes and
/// re-adds a key, <b>inverts the user's rules</b> — silently, with no error and no visible
/// diff beyond the ordering. Everything here preserves the order it parsed.
/// </para>
/// <para>
/// This is also why merging is dangerous. Spike S1 measured a lower layer's
/// <c>{"npm *": "deny"}</c> merging under a project's <c>{"*": "ask", "git *": "allow"}</c> to
/// give <c>{"npm *": "deny", "*": "ask", "git *": "allow"}</c> — the lower layer's keys land
/// first, so the broad <c>"*": "ask"</c> now sits <i>after</i> the narrow deny and
/// <c>npm install</c> resolves to <b>ask</b>. The user's deny was defeated by merge ordering
/// alone, with no edit to either file. <see cref="FindShadowedRules"/> exists to surface that.
/// </para>
/// <para>
/// <b>Two shapes, both valid.</b> The whole <c>permission</c> value may be a single action
/// string applying to every tool, or an object keyed by tool. Five named tools accept only a
/// bare action and reject the object form — see <see cref="ActionOnlyTools"/>.
/// </para>
/// </remarks>
public sealed class OpenCodePermissionModel
{
    /// <summary>
    /// Tools whose schema type is the action string alone, so a pattern object is invalid.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Five, not four.</b> The plan lists <c>todowrite</c>, <c>question</c>,
    /// <c>webfetch</c> and <c>websearch</c>; the bundled schema also types
    /// <c>doom_loop</c> as action-only. Read from the schema, not the prose.
    /// <para>
    /// They are action-only because none of them takes an argument worth pattern-matching:
    /// there is no meaningful glob for "write a todo".
    /// </para>
    /// </remarks>
    public static IReadOnlySet<string> ActionOnlyTools { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "todowrite",
            "question",
            "webfetch",
            "websearch",
            "doom_loop",
        };

    private OpenCodePermissionModel(
        PermissionOutcome? global,
        IReadOnlyList<KeyValuePair<string, OpenCodeToolPermission>> tools)
    {
        GlobalAction = global;
        Tools = tools;
    }

    /// <summary>
    /// Set when the whole <c>permission</c> value was a bare action — <c>"permission": "ask"</c>
    /// — which applies to every tool.
    /// </summary>
    public PermissionOutcome? GlobalAction { get; }

    /// <summary>
    /// Per-tool settings <b>in file order</b>. A list of pairs rather than a dictionary,
    /// because a dictionary would invite reordering and this order carries meaning.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, OpenCodeToolPermission>> Tools { get; }

    /// <summary>An empty model — no <c>permission</c> key at all.</summary>
    public static OpenCodePermissionModel Empty { get; } = new(null, []);

    /// <summary>
    /// Parse a <c>permission</c> node. Returns <see cref="Empty"/> for <see langword="null"/>.
    /// </summary>
    /// <exception cref="FormatException">
    /// The node is a shape OpenCode would reject: an unknown action string, a non-action
    /// value where an action belongs, or a pattern object on an action-only tool. Parsing
    /// throws rather than dropping the offending entry, because a permission rule that
    /// silently disappears is a rule the user believes is protecting them.
    /// </exception>
    public static OpenCodePermissionModel Parse(JsonNode? node)
    {
        if (node is null)
        {
            return Empty;
        }

        if (node is JsonValue)
        {
            return new OpenCodePermissionModel(ParseAction(node, "permission"), []);
        }

        if (node is not JsonObject obj)
        {
            throw new FormatException(
                "'permission' must be an action string or an object keyed by tool.");
        }

        List<KeyValuePair<string, OpenCodeToolPermission>> tools = [];
        foreach (KeyValuePair<string, JsonNode?> pair in obj)
        {
            tools.Add(new KeyValuePair<string, OpenCodeToolPermission>(
                pair.Key,
                ParseTool(pair.Key, pair.Value)));
        }

        return new OpenCodePermissionModel(null, tools);
    }

    /// <summary>
    /// What OpenCode would decide for <paramref name="input"/> on <paramref name="tool"/>.
    /// </summary>
    /// <param name="tool">Tool name, e.g. <c>bash</c>.</param>
    /// <param name="input">The command line or path being checked.</param>
    /// <returns>
    /// The matched rule's action, or <see cref="PermissionOutcome.Default"/> when nothing
    /// matched — which says only that no rule decided it, never that it is permitted.
    /// </returns>
    /// <remarks>
    /// <b>Last match wins</b>, so this scans from the end. A first-match implementation looks
    /// identical for a single rule and inverts a well-written config — broad-first,
    /// narrow-last is precisely the idiom the vendor documents.
    /// </remarks>
    public OpenCodePermissionDecision Resolve(string tool, string input)
    {
        ArgumentException.ThrowIfNullOrEmpty(tool);
        ArgumentNullException.ThrowIfNull(input);

        if (GlobalAction is { } global)
        {
            return new OpenCodePermissionDecision(global, MatchedTool: null, MatchedRule: null);
        }

        foreach (KeyValuePair<string, OpenCodeToolPermission> entry in Tools)
        {
            if (!string.Equals(entry.Key, tool, StringComparison.Ordinal))
            {
                continue;
            }

            if (entry.Value.SingleAction is { } action)
            {
                return new OpenCodePermissionDecision(action, entry.Key, MatchedRule: null);
            }

            for (int i = entry.Value.Rules.Count - 1; i >= 0; i--)
            {
                OpenCodePermissionRule rule = entry.Value.Rules[i];
                if (OpenCodeGlob.IsMatch(rule.Pattern, input))
                {
                    return new OpenCodePermissionDecision(rule.Action, entry.Key, rule);
                }
            }
        }

        return new OpenCodePermissionDecision(
            PermissionOutcome.Default, MatchedTool: null, MatchedRule: null);
    }

    /// <summary>
    /// Rules that can never fire because a later, broader rule on the same tool covers
    /// everything they match.
    /// </summary>
    /// <remarks>
    /// This is the merge-inversion hazard made visible. After layers combine, a lower layer's
    /// narrow <c>"npm *": "deny"</c> can end up before a higher layer's broad <c>"*": "ask"</c>
    /// — and since the last match wins, the deny becomes unreachable. Nothing errors; the rule
    /// is simply inert. Surfacing it is the only way a user finds out before it matters.
    /// </remarks>
    public IReadOnlyList<OpenCodeShadowedRule> FindShadowedRules()
    {
        List<OpenCodeShadowedRule> shadowed = [];

        foreach (KeyValuePair<string, OpenCodeToolPermission> entry in Tools)
        {
            IReadOnlyList<OpenCodePermissionRule> rules = entry.Value.Rules;
            for (int i = 0; i < rules.Count; i++)
            {
                for (int later = i + 1; later < rules.Count; later++)
                {
                    // A later rule shadows an earlier one when it matches everything the
                    // earlier one does. Testing the earlier PATTERN against the later one is
                    // a sound approximation: "*" matches "npm *", so "*" shadows it.
                    if (OpenCodeGlob.IsMatch(rules[later].Pattern, rules[i].Pattern))
                    {
                        shadowed.Add(new OpenCodeShadowedRule(entry.Key, rules[i], rules[later]));
                        break;
                    }
                }
            }
        }

        return shadowed;
    }

    private static OpenCodeToolPermission ParseTool(string tool, JsonNode? value)
    {
        if (value is JsonValue)
        {
            return OpenCodeToolPermission.Single(ParseAction(value, tool));
        }

        if (value is not JsonObject obj)
        {
            throw new FormatException(
                $"'permission.{tool}' must be an action string or an object of pattern rules.");
        }

        if (ActionOnlyTools.Contains(tool))
        {
            throw new FormatException(
                $"'permission.{tool}' takes an action string only — it accepts no pattern "
                + "rules, because it has no argument worth matching.");
        }

        List<OpenCodePermissionRule> rules = [];
        foreach (KeyValuePair<string, JsonNode?> pair in obj)
        {
            rules.Add(new OpenCodePermissionRule(pair.Key, ParseAction(pair.Value, $"{tool}.{pair.Key}")));
        }

        return OpenCodeToolPermission.Patterns(rules);
    }

    private static PermissionOutcome ParseAction(JsonNode? node, string where)
    {
        string? text = node is JsonValue value && value.TryGetValue(out string? s) ? s : null;

        return text switch
        {
            "allow" => PermissionOutcome.Allow,
            "ask" => PermissionOutcome.Ask,
            "deny" => PermissionOutcome.Deny,
            _ => throw new FormatException(
                $"'{where}' must be \"allow\", \"ask\" or \"deny\"" +
                (text is null ? "." : $", not \"{text}\"."))
        };
    }
}

/// <summary>What the model decided, and which rule decided it.</summary>
/// <param name="Outcome">The resolved outcome. <see cref="PermissionOutcome.Default"/> when nothing matched.</param>
/// <param name="MatchedTool">The tool key whose entry decided it, or <see langword="null"/>.</param>
/// <param name="MatchedRule">The rule that matched, or <see langword="null"/> for a bare action or no match.</param>
public sealed record OpenCodePermissionDecision(
    PermissionOutcome Outcome,
    string? MatchedTool,
    OpenCodePermissionRule? MatchedRule);

/// <summary>A rule that can never fire, and the later rule that covers it.</summary>
/// <param name="Tool">The tool whose rule set contains both.</param>
/// <param name="Rule">The unreachable rule.</param>
/// <param name="ShadowedBy">The later, broader rule that always wins first.</param>
public sealed record OpenCodeShadowedRule(
    string Tool,
    OpenCodePermissionRule Rule,
    OpenCodePermissionRule ShadowedBy);
