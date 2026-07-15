using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Permissions;

/// <summary>
/// Guards the single-source permission tool-name taxonomy (<see cref="PermissionTools"/>).
/// The list was formerly hand-maintained in three places (the SDK regex, the GUI
/// regex, and the GUI's <c>KnownToolNames</c> set) and drifted (the proven
/// <c>Pwsh</c>/<c>Monitor</c> drift). These tests lock it to the bundled schema and
/// to the regex behaviour, so any future drift fails CI.
/// </summary>
[TestClass]
public class PermissionToolsTests
{
    [TestMethod]
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

        Assert.IsNotNull(resName, "Bundled claude-code-settings.json embedded resource must exist in Core.");

        string json;
        using (System.IO.Stream stream = core.GetManifestResourceStream(resName!)!)
        using (System.IO.StreamReader reader = new(stream))
        {
            json = reader.ReadToEnd();
        }

        System.Text.Json.Nodes.JsonNode root = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        string? pattern = root["$defs"]?["permissionRule"]?["pattern"]?.GetValue<string>();
        Assert.IsFalse(string.IsNullOrEmpty(pattern), "$defs.permissionRule.pattern must exist.");

        // Extract the leading tool-name alternation: ^((Agent|Bash|...|Write)(...
        System.Text.RegularExpressions.Match m =
            System.Text.RegularExpressions.Regex.Match(pattern!, @"\(\(([A-Za-z|]+)\)");
        Assert.IsTrue(m.Success, $"Could not extract the tool-name alternation from: {pattern}");
        string[] schemaNames = m.Groups[1].Value.Split('|');

        CollectionAssert.AreEquivalent(
            new System.Collections.Generic.List<string>(PermissionTools.Names),
            schemaNames,
            "PermissionTools.Names must match the schema's permissionRule tool-name alternation "
            + "exactly. If the schema changed, update PermissionTools.Names (the single source).");
    }

    [TestMethod]
    public void RulePattern_AcceptsEveryKnownTool_RejectsUnknownAndAllWildcard()
    {
        foreach (string tool in PermissionTools.Names)
        {
            Assert.IsTrue(PermissionTools.RuleRegex.IsMatch(tool), $"Bare tool '{tool}' must be valid.");
        }

        Assert.IsFalse(PermissionTools.RuleRegex.IsMatch("NotARealTool"), "Unknown tool must be rejected.");
        Assert.IsFalse(
            PermissionTools.RuleRegex.IsMatch("Bash(*)"),
            "All-wildcard parens content must be rejected by the strict lookahead.");
        Assert.IsTrue(PermissionTools.RuleRegex.IsMatch("Bash(git status)"), "A real pattern must be accepted.");
        Assert.IsTrue(PermissionTools.RuleRegex.IsMatch("mcp__server__tool"), "mcp__ rules must be accepted.");
    }
}
