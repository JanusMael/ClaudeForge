using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Permissions;

/// <summary>
/// Guards the single-source permission tool-name taxonomy (<see cref="PermissionTools"/>).
/// The list was formerly hand-maintained in three places (the SDK regex, the GUI
/// regex, and the GUI's <c>KnownToolNames</c> set) and drifted (the proven
/// <c>Pwsh</c>/<c>Monitor</c> drift). These tests lock it to the bundled schema and
/// to the regex behaviour, so any future drift fails CI.
/// </summary>
public class PermissionToolsTests
{
    [Fact]
    public void Names_MatchTheSchemaPermissionRuleAlternation()
    {
        // CORE REQUIREMENT: a list that mirrors the schema must be schema-driven, not
        // a hand-maintained mirror. This locks PermissionTools.Names to the bundled
        // schema's $defs.permissionRule alternation — a tool added to (or removed from)
        // the schema that doesn't match the SDK constant fails here.
        //
        // Read the schema straight from the Core assembly's embedded resource:
        // SchemaRegistry's byte accessor is internal to Core, and the permissionRule
        // pattern lives in the base schema (preserved across the overlay merge). Use a
        // public Core type (ConfigScope) to locate the Core assembly without an
        // InternalsVisibleTo dependency.
        System.Reflection.Assembly core = typeof(ConfigScope).Assembly;
        string? resName = null;
        foreach (string n in core.GetManifestResourceNames())
        {
            if (n.EndsWith("claude-code-settings.json", System.StringComparison.Ordinal))
            {
                resName = n;
                break;
            }
        }

        MessageAssert.NotNull(resName, "Bundled claude-code-settings.json embedded resource must exist in Core.");

        string json;
        using (System.IO.Stream stream = core.GetManifestResourceStream(resName!)!)
        using (System.IO.StreamReader reader = new(stream))
        {
            json = reader.ReadToEnd();
        }

        System.Text.Json.Nodes.JsonNode root = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        string? pattern = root["$defs"]?["permissionRule"]?["pattern"]?.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(pattern), "$defs.permissionRule.pattern must exist.");

        // Extract the leading tool-name alternation: ^((Agent|Bash|...|Write)(...
        System.Text.RegularExpressions.Match m =
            System.Text.RegularExpressions.Regex.Match(pattern!, @"\(\(([A-Za-z|]+)\)");
        Assert.True(m.Success, $"Could not extract the tool-name alternation from: {pattern}");
        string[] schemaNames = m.Groups[1].Value.Split('|');

        MessageAssert.SameElements(
            new System.Collections.Generic.List<string>(PermissionTools.Names),
            schemaNames,
            "PermissionTools.Names must match the schema's permissionRule tool-name alternation "
            + "exactly. If the schema changed, update PermissionTools.Names (the single source).");
    }

    [Fact]
    public void RulePattern_AcceptsEveryKnownTool_RejectsUnknownAndAllWildcard()
    {
        foreach (string tool in PermissionTools.Names)
        {
            Assert.True(PermissionTools.RuleRegex.IsMatch(tool), $"Bare tool '{tool}' must be valid.");
        }

        Assert.False(PermissionTools.RuleRegex.IsMatch("NotARealTool"), "Unknown tool must be rejected.");
        Assert.False(
            PermissionTools.RuleRegex.IsMatch("Bash(*)"),
            "All-wildcard parens content must be rejected.");
        Assert.True(PermissionTools.RuleRegex.IsMatch("Bash(git status)"), "A real pattern must be accepted.");
        Assert.True(PermissionTools.RuleRegex.IsMatch("mcp__server__tool"), "mcp__ rules must be accepted.");
    }

    [Theory]
    [InlineData("Bash(*)\n")]
    [InlineData("Bash(?)\n")]
    [InlineData("Read(*?*)\n")]
    public void RulePattern_RejectsAllWildcard_EvenWithTrailingNewline(string rule)
    {
        // The former lookahead (?=.*[^)*?]) scanned past the closing paren, so the
        // trailing newline that `$` tolerates satisfied it, and these were accepted.
        // An exhaustive comparison (53.8M inputs) found this shape as the ONLY
        // behavioural difference between the lookahead and the current pattern.
        Assert.False(
            PermissionTools.RuleRegex.IsMatch(rule),
            "All-wildcard parens content must be rejected even when a newline follows.");
    }

    [Fact]
    public void RuleRegex_IsLinearTime_AndHasNoWallClockTimeout()
    {
        // The GUI validates every keystroke with this regex. It used to carry a 100 ms
        // match timeout, which fired under CPU load on 9-character input and turned
        // valid rules into "Rule validation timed out" (failing 2 of 6 loaded suite
        // runs). Linearity now comes from the engine, and a timeout would only put
        // the load-dependent failure back.
        Assert.True(
            PermissionTools.RuleRegex.Options.HasFlag(System.Text.RegularExpressions.RegexOptions.NonBacktracking),
            "RuleRegex must run on the NonBacktracking engine, which is linear in the input length.");
        Assert.Equal(System.Text.RegularExpressions.Regex.InfiniteMatchTimeout, PermissionTools.RuleRegex.MatchTimeout);
    }
}
