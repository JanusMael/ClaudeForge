using System.Text.Json;
using Bennewitz.Ninja.OpenCode.Sdk.Permissions;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// <see cref="OpenCodePermissionModel.ActionOnlyTools"/> still matches the bundled schema.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>That set is hardcoded, and Phase 13 exists to refresh these schemas.</b> A refresh that
/// adds a sixth action-only tool, or promotes an existing one to accept pattern rules, would leave
/// the editor enforcing last release's rule — and the failure is silent in the worse direction:
/// the permission grid would happily offer pattern rules for a tool OpenCode rejects, so the user
/// writes a config that is thrown out, with the editor insisting it is fine.
/// </para>
/// <para>
/// The schema is read from the source tree rather than through <c>BundledResource</c>, which is
/// internal to <c>AgentForge.Core</c> and not visible here. Reading the file that is embedded is
/// equivalent for drift purposes, and <c>BundledOpenCodeSchemaTests</c> separately asserts the
/// embedding itself.
/// </para>
/// </remarks>
[TestClass]
public sealed class ActionOnlyToolsSchemaDriftTests
{
    private static string SchemaPath()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(
                dir, "src", "AgentForge.Core", "Assets", "Schemas", "opencode-config.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate the bundled OpenCode schema from '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    /// The named tool properties of <c>PermissionConfig</c>'s object arm, split by which
    /// <c>$ref</c> types them.
    /// </summary>
    private static (IReadOnlySet<string> ActionOnly, IReadOnlySet<string> RuleCapable) FromSchema()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(SchemaPath()));

        JsonElement permissionConfig = doc.RootElement
            .GetProperty("$defs")
            .GetProperty("PermissionConfig");

        JsonElement objectArm = default;
        foreach (JsonElement arm in permissionConfig.GetProperty("anyOf").EnumerateArray())
        {
            if (arm.TryGetProperty("type", out JsonElement t) && t.GetString() == "object")
            {
                objectArm = arm;
                break;
            }
        }

        Assert.AreNotEqual(
            JsonValueKind.Undefined,
            objectArm.ValueKind,
            "PermissionConfig has no object arm. The union's shape changed, so this test is reading "
            + "the schema wrongly rather than reporting drift.");

        HashSet<string> actionOnly = new(StringComparer.Ordinal);
        HashSet<string> ruleCapable = new(StringComparer.Ordinal);

        foreach (JsonProperty property in objectArm.GetProperty("properties").EnumerateObject())
        {
            if (!property.Value.TryGetProperty("$ref", out JsonElement refElement))
            {
                continue;
            }

            string reference = refElement.GetString() ?? string.Empty;
            if (reference.EndsWith("PermissionActionConfig", StringComparison.Ordinal))
            {
                actionOnly.Add(property.Name);
            }
            else if (reference.EndsWith("PermissionRuleConfig", StringComparison.Ordinal))
            {
                ruleCapable.Add(property.Name);
            }
        }

        return (actionOnly, ruleCapable);
    }

    [TestMethod]
    public void ActionOnlyTools_MatchesTheSchemaExactly()
    {
        (IReadOnlySet<string> schemaActionOnly, IReadOnlySet<string> schemaRuleCapable) = FromSchema();

        Assert.IsTrue(
            schemaActionOnly.Count > 0,
            "Found no PermissionActionConfig-typed tools in the schema. Either the schema changed "
            + "shape or this test stopped reading it — either way it guards nothing.");

        List<string> missing = [.. schemaActionOnly
            .Except(OpenCodePermissionModel.ActionOnlyTools)
            .Order(StringComparer.Ordinal)];
        List<string> extra = [.. OpenCodePermissionModel.ActionOnlyTools
            .Except(schemaActionOnly)
            .Order(StringComparer.Ordinal)];

        Assert.IsTrue(
            missing.Count == 0,
            $"The schema types these as action-only but ActionOnlyTools does not: "
            + $"{string.Join(", ", missing)}. The grid would offer pattern rules for them, so the "
            + "user writes a config OpenCode rejects while the editor says it is fine.");

        Assert.IsTrue(
            extra.Count == 0,
            $"ActionOnlyTools lists these but the schema does not: {string.Join(", ", extra)}. The "
            + "grid would refuse pattern rules the schema allows, and silently drop any that "
            + "already exist in the user's file.");

        // The two sets must not overlap, or a tool is being typed both ways and one of the two
        // readings above is wrong.
        Assert.AreEqual(
            0,
            schemaActionOnly.Intersect(schemaRuleCapable).Count(),
            "A tool is typed both action-only and rule-capable in the schema.");
    }

    /// <remarks>
    /// The nested case the agent editor depends on. <c>Config.permission</c> and
    /// <c>AgentConfig.permission</c> are both a bare <c>$ref</c> to the same definition, which is
    /// what makes the permission grid reusable as a child editor — the factory matches on the
    /// property NAME, path-insensitively, and this is the assertion that keeps that safe.
    /// </remarks>
    [TestMethod]
    public void AgentPermissionAndGlobalPermission_AreTheSameDefinition()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(SchemaPath()));
        JsonElement defs = doc.RootElement.GetProperty("$defs");

        string global = defs
            .GetProperty("Config").GetProperty("properties").GetProperty("permission")
            .GetProperty("$ref").GetString() ?? string.Empty;

        string agent = defs
            .GetProperty("AgentConfig").GetProperty("properties").GetProperty("permission")
            .GetProperty("$ref").GetString() ?? string.Empty;

        Assert.AreEqual(
            "#/$defs/PermissionConfig",
            global,
            "The global permission key stopped being a direct $ref to PermissionConfig.");

        Assert.AreEqual(
            global,
            agent,
            "An agent's permission override is no longer the same shape as the global permission "
            + "value. The agent editor reuses the permission grid on the strength of them being "
            + "identical; if they diverge, the child editor is now editing a shape it was not "
            + "built for — and would write a valid-looking value of the wrong type.");
    }
}
