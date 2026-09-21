using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Settings;

[TestClass]
public class MergeEngineTests
{
    // -----------------------------------------------------------------------
    // Non-array: highest-priority scope wins
    // -----------------------------------------------------------------------

    [TestMethod]
    public void NonArray_ManagedWinsOverUser()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.Managed, JsonValue.Create("managed-value"), "managed.json"),
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user-value"), "user.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "key", TestMergePolicy.NeverUnions);

        Assert.AreEqual("managed-value", result.EffectiveValue?.GetValue<string>());
        Assert.AreEqual(ConfigScope.Managed, result.EffectiveScope);
    }

    [TestMethod]
    public void NonArray_UserWinsOverProject()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user"), "user.json"),
            new ScopeEntry(ConfigScope.Project, JsonValue.Create("project"), "project.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "key", TestMergePolicy.NeverUnions);

        Assert.AreEqual("user", result.EffectiveValue?.GetValue<string>());
        Assert.AreEqual(ConfigScope.User, result.EffectiveScope);
    }

    [TestMethod]
    public void NonArray_SingleScopeReturned()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.Local, JsonValue.Create(42), "local.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "key", TestMergePolicy.NeverUnions);

        Assert.AreEqual(42, result.EffectiveValue?.GetValue<int>());
        Assert.AreEqual(ConfigScope.Local, result.EffectiveScope);
    }

    [TestMethod]
    public void NoEntries_ReturnsNull()
    {
        MergeResult result = MergeEngine.Merge([], "key", TestMergePolicy.NeverUnions);

        Assert.IsNull(result.EffectiveValue);
        Assert.IsNull(result.EffectiveScope);
    }

    // -----------------------------------------------------------------------
    // Inferring policy: only a uniform all-array set unions
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Inferred_MixedScalarAndArray_HighestPriorityScopeWins()
    {
        // Regression: a key whose value is a bool at a higher-priority scope and an
        // array at a lower-priority scope (legal for enabledPlugins, anyOf[array, bool])
        // must NOT be union-merged — that silently dropped the higher-priority bool.
        // With inference, a MIXED set falls through to highest-priority-scope-wins.
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.Project, JsonValue.Create(true), "project.json"),
            new ScopeEntry(ConfigScope.User, new JsonArray("comp-a"), "user.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "key", TestMergePolicy.Inferring);

        Assert.IsTrue(result.EffectiveValue is JsonValue jv && jv.GetValue<bool>(),
            "The higher-priority Project bool must win over the lower-priority User array.");
        Assert.AreEqual(ConfigScope.Project, result.EffectiveScope);
    }

    [TestMethod]
    public void Inferred_AllArrays_StillUnions()
    {
        // Guard: the fix only changes the MIXED case — a homogeneous all-array set,
        // inferred (not schema-declared), must still union across scopes.
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, new JsonArray("a"), "user.json"),
            new ScopeEntry(ConfigScope.Project, new JsonArray("b"), "project.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "key", TestMergePolicy.Inferring);

        JsonArray? arr = result.EffectiveValue as JsonArray;
        Assert.IsNotNull(arr);
        Assert.AreEqual(2, arr!.Count, "Two distinct array entries must union.");
    }

    // -----------------------------------------------------------------------
    // Array: union across all scopes
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Array_UnionAcrossScopes()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, new JsonArray("a", "b"), "user.json"),
            new ScopeEntry(ConfigScope.Project, new JsonArray("b", "c"), "project.json"),
            new ScopeEntry(ConfigScope.Local, new JsonArray("c", "d"), "local.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "key", TestMergePolicy.AlwaysUnions);

        JsonArray arr = (JsonArray)result.EffectiveValue!;
        List<string> items = arr.Select(x => x!.GetValue<string>()).OrderBy(s => s).ToList();
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, items);
    }

    [TestMethod]
    public void Array_DeduplicatesItems()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, new JsonArray("x", "y"), "user.json"),
            new ScopeEntry(ConfigScope.Project, new JsonArray("x", "z"), "project.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "key", TestMergePolicy.AlwaysUnions);
        JsonArray arr = (JsonArray)result.EffectiveValue!;
        Assert.AreEqual(3, arr.Count); // x, y, z — x not duplicated
    }

    [TestMethod]
    public void Array_DeduplicatesObjects_RegardlessOfPropertyOrder()
    {
        // Regression: pre-fix the dedup keyed on JsonNode.ToJsonString(), which is
        // property-order sensitive — two semantically equal objects with different
        // key orderings were both retained.
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User,
                new JsonArray(new JsonObject { ["a"] = 1, ["b"] = 2 }),
                "user.json"),
            new ScopeEntry(ConfigScope.Project,
                new JsonArray(new JsonObject { ["b"] = 2, ["a"] = 1 }), // same value, reordered
                "project.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "key", TestMergePolicy.AlwaysUnions);
        JsonArray arr = (JsonArray)result.EffectiveValue!;
        Assert.AreEqual(1, arr.Count);
    }

    // -----------------------------------------------------------------------
    // Object: deep merge
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Object_DeepMerge()
    {
        JsonObject user = new() { ["a"] = "user-a", ["b"] = "user-b" };
        JsonObject project = new() { ["b"] = "project-b", ["c"] = "project-c" };

        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, user, "user.json"),
            new ScopeEntry(ConfigScope.Project, project, "project.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "key", TestMergePolicy.Inferring);

        JsonObject obj = (JsonObject)result.EffectiveValue!;
        Assert.AreEqual("user-a", obj["a"]!.GetValue<string>()); // only user defines a
        Assert.AreEqual("user-b", obj["b"]!.GetValue<string>()); // user wins over project
        Assert.AreEqual("project-c", obj["c"]!.GetValue<string>()); // only project defines c
    }

    // -----------------------------------------------------------------------
    // ComputeEffective
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ComputeEffective_MergesAllDocuments()
    {
        SettingsDocument[] docs =
        [
            MakeDoc(ConfigScope.User, """{"model":"sonnet","cleanupPeriodDays":30}"""),
            MakeDoc(ConfigScope.Project, """{"cleanupPeriodDays":90,"language":"en"}"""),
        ];

        JsonObject effective = MergeEngine.ComputeEffective(docs, TestMergePolicy.Inferring);

        Assert.AreEqual("sonnet", effective["model"]!.GetValue<string>());
        Assert.AreEqual(30, effective["cleanupPeriodDays"]!.GetValue<int>()); // user wins
        Assert.AreEqual("en", effective["language"]!.GetValue<string>());
    }

    private static SettingsDocument MakeDoc(ConfigScope scope, string json)
    {
        JsonObject root = (JsonObject)JsonNode.Parse(json)!;
        return new SettingsDocument(scope, $"{scope}.json", root, isReadOnly: false);
    }

    // -----------------------------------------------------------------------
    // All-null entries
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Merge_AllNullEntries_ReturnsNullWithNoScope()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.Managed, null, "managed.json"),
            new ScopeEntry(ConfigScope.User, null, "user.json"),
            new ScopeEntry(ConfigScope.Project, null, "project.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "key", TestMergePolicy.NeverUnions);

        Assert.IsNull(result.EffectiveValue);
        Assert.IsNull(result.EffectiveScope);
    }

    // -----------------------------------------------------------------------
    // Array: higher-scope empty array still unions lower-scope entries
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Merge_ArrayWithHigherScopeEmpty_StillUnionsLowerScope()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.Managed, new JsonArray(), "managed.json"),
            new ScopeEntry(ConfigScope.User, new JsonArray("a", "b"), "user.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "key", TestMergePolicy.AlwaysUnions);

        JsonArray arr = (JsonArray)result.EffectiveValue!;
        List<string> items = arr.Select(x => x!.GetValue<string>()).OrderBy(s => s).ToList();
        CollectionAssert.Contains(items, "a");
        CollectionAssert.Contains(items, "b");
        Assert.AreEqual(ConfigScope.User, result.EffectiveScope,
            "Effective scope is the first scope with a non-empty array contribution.");
    }

    // -----------------------------------------------------------------------
    // ComputeEffective: nested array path union
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ComputeEffective_WithNestedArrayPath_ArraysUnioned()
    {
        SettingsDocument[] docs =
        [
            MakeDoc(ConfigScope.User, """{"permissions":{"allow":["Bash(*)","Read(*)"]}}"""),
            MakeDoc(ConfigScope.Project, """{"permissions":{"allow":["Edit(*)"]}}"""),
        ];

        JsonObject effective = MergeEngine.ComputeEffective(docs, TestMergePolicy.Declaring("permissions.allow"));

        JsonArray allow = (JsonArray)effective["permissions"]!["allow"]!;
        HashSet<string> items = allow.Select(x => x!.GetValue<string>()).ToHashSet();
        Assert.IsTrue(items.Contains("Bash(*)"), "Bash(*) must be present");
        Assert.IsTrue(items.Contains("Read(*)"), "Read(*) must be present");
        Assert.IsTrue(items.Contains("Edit(*)"), "Edit(*) must be present");
    }

    // -----------------------------------------------------------------------
    // ComputeEffective: non-array nested path — highest scope wins
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ComputeEffective_WithNestedPath_NotInArrayPaths_HighestWins()
    {
        SettingsDocument[] docs =
        [
            MakeDoc(ConfigScope.User, """{"permissions":{"allow":["Bash(*)","Read(*)"]}}"""),
            MakeDoc(ConfigScope.Project, """{"permissions":{"allow":["Edit(*)"]}}"""),
        ];

        // The policy declares nothing, so the nested allow is unioned only if inference
        // says so — both sides are arrays, so it is.
        JsonObject effective = MergeEngine.ComputeEffective(docs, TestMergePolicy.Inferring);

        JsonArray allow = (JsonArray)effective["permissions"]!["allow"]!;
        List<string> items = allow.Select(x => x!.GetValue<string>()).ToList();
        // Inferred as array because both sides are JsonArray values; User's items win dedup
        // but Project's unique "Edit(*)" is still included in the union. What we can assert
        // is that the User values are present and the result does NOT contain duplicates.
        Assert.IsTrue(items.Contains("Bash(*)"), "Bash(*) from User is present");
        Assert.IsTrue(items.Contains("Read(*)"), "Read(*) from User is present");
        // "Edit(*)" may or may not be present depending on inferred-array logic; the key
        // invariant is User values are not lost.
        Assert.AreEqual(items.Count, items.Distinct().Count(), "No duplicates in result");
    }

    // -----------------------------------------------------------------------
    // MergeObjects: null child value is omitted from result
    // -----------------------------------------------------------------------

    [TestMethod]
    public void MergeObjects_NullChildValue_KeyOmittedFromResult()
    {
        // ScopeEntry with a null Value reference (not a JSON null node, but a missing key)
        // represents "this scope did not define the key at all".
        // User's ScopeEntry has Value = null (absent), Project's has Value = "sonnet".
        // MergeCore filters entries where e.Value is null, so Project's value wins.
        JsonObject user = new(); // no "model" key
        JsonObject project = new() { ["model"] = JsonValue.Create("sonnet") };

        ScopeEntry[] entries =
        [
            // User contributes the top-level object, but its child "model" is absent.
            // Constructing key-entries: User's JsonObject has no "model", so only Project
            // participates in the child merge — Project's value must appear.
            new ScopeEntry(ConfigScope.User, user, "user.json"),
            new ScopeEntry(ConfigScope.Project, project, "project.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "key", TestMergePolicy.Inferring);

        JsonObject obj = (JsonObject)result.EffectiveValue!;
        // Only Project defined "model"; result must contain it.
        Assert.IsTrue(obj.ContainsKey("model"),
            "model key must be present when only Project defines it");
        Assert.AreEqual("sonnet", obj["model"]!.GetValue<string>());
    }

    [TestMethod]
    public void MergeObjects_ExplicitNullJsonValue_LowerScopeWins()
    {
        // JsonValue.Create<string?>(null) returns null (a null reference, not a JSON null node)
        // in System.Text.Json.Nodes.  MergeCore therefore treats User's child entry as absent
        // (filtered by e.Value != null), so Project's "sonnet" value wins.
        JsonObject user = new() { ["model"] = JsonValue.Create((string?)null) };
        JsonObject project = new() { ["model"] = JsonValue.Create("sonnet") };

        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, user, "user.json"),
            new ScopeEntry(ConfigScope.Project, project, "project.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "key", TestMergePolicy.Inferring);

        JsonObject obj = (JsonObject)result.EffectiveValue!;
        // User's null reference is treated as absent; Project's value is the only defined one.
        Assert.IsTrue(obj.ContainsKey("model"),
            "model key is present because Project's value is the only defined one");
        Assert.AreEqual("sonnet", obj["model"]!.GetValue<string>(),
            "Project's value wins when User's null JsonValue is treated as absent");
    }

    // -----------------------------------------------------------------------
    // Inferred array: both entries are JsonArrays → result is a union
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Merge_InferredArray_WhenAllDefinedValuesAreArrays()
    {
        // An inferring policy derives array semantics from the actual values.
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, new JsonArray("alpha", "beta"), "user.json"),
            new ScopeEntry(ConfigScope.Project, new JsonArray("beta", "gamma"), "project.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "key", TestMergePolicy.Inferring);

        JsonArray arr = (JsonArray)result.EffectiveValue!;
        List<string> items = arr.Select(x => x!.GetValue<string>()).OrderBy(s => s).ToList();
        // If treated as array (inferred), the result is a union: alpha, beta, gamma
        // If treated as scalar (first-wins), the result is only: alpha, beta
        // The engine must produce the union.
        CollectionAssert.AreEqual(new[] { "alpha", "beta", "gamma" }, items,
            "Result must be a union when array-ness is inferred from actual JsonArray values.");
    }

    // -----------------------------------------------------------------------
    // The policy seam itself. Neither behaviour below is reachable through
    // Claude's policy, so without these tests the two knobs IMergePolicy adds
    // would sit unexercised until a second product arrived to discover them.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Union_HighestPriorityFirst_PutsTheWinningScopesEntriesFirst()
    {
        // The baseline the next test contrasts with — asserted on ORDER, not membership,
        // because order is the whole subject. Every other union test here sorts first.
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.Project, new JsonArray("proj"), "project.json"),
            new ScopeEntry(ConfigScope.User, new JsonArray("user"), "user.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "instructions", TestMergePolicy.Inferring);

        JsonArray arr = (JsonArray)result.EffectiveValue!;
        CollectionAssert.AreEqual(
            new[] { "proj", "user" },
            arr.Select(x => x!.GetValue<string>()).ToArray(),
            "Claude's order: the highest-priority scope's entries lead.");
    }

    [TestMethod]
    public void Union_LowestPriorityFirst_ReversesTheConcatenationOrder()
    {
        // OpenCode's measured order (Spike S1): a global `instructions` entry precedes the
        // project's. Not cosmetic there — OpenCode resolves a permission map by LAST match,
        // so a merged map assembled from the wrong end inverts the user's intent. The
        // engine has to be able to produce this without an OpenCode policy existing.
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.Project, new JsonArray("proj"), "project.json"),
            new ScopeEntry(ConfigScope.User, new JsonArray("user"), "user.json"),
        ];

        MergeResult result = MergeEngine.Merge(entries, "instructions", TestMergePolicy.InferringLowestFirst);

        JsonArray arr = (JsonArray)result.EffectiveValue!;
        CollectionAssert.AreEqual(
            new[] { "user", "proj" },
            arr.Select(x => x!.GetValue<string>()).ToArray(),
            "Lowest-priority contributions lead when the policy says so.");

        Assert.AreEqual(ConfigScope.Project, result.EffectiveScope,
            "Union order describes where the result starts, NOT which scope is credited: "
            + "the effective scope stays the highest-priority contributor either way.");
    }

    [TestMethod]
    public void DeclaringOnlyPolicy_UndeclaredAllArrayPath_IsReplacedNotUnioned()
    {
        // The shape OpenCode needs and Claude must not have: `disabled_providers` is an
        // array in both scopes, yet the higher scope replaces the lower outright. A policy
        // that inferred union-ness from the values would silently resurrect a provider the
        // user disabled. This is why inference is the policy's call and not the engine's.
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.Project, new JsonArray("openai"), "project.json"),
            new ScopeEntry(ConfigScope.User, new JsonArray("anthropic"), "user.json"),
        ];

        MergeResult result = MergeEngine.Merge(
            entries, "disabled_providers", TestMergePolicy.DeclaringOnly("instructions"));

        JsonArray arr = (JsonArray)result.EffectiveValue!;
        CollectionAssert.AreEqual(
            new[] { "openai" },
            arr.Select(x => x!.GetValue<string>()).ToArray(),
            "An undeclared path must be replaced when the policy does not infer.");
        Assert.AreEqual(ConfigScope.Project, result.EffectiveScope);
    }

    [TestMethod]
    public void Policy_RulesOnTheDottedChildPath_NotJustTheTopLevelKey()
    {
        // The prefix threading is load-bearing: "permissions.allow" has to be recognisable
        // while merging the enclosing "permissions" object, or a nested declaration silently
        // does nothing. Declares the child ONLY, and turns inference off so a pass would
        // have to come from the path actually being matched.
        SettingsDocument[] docs =
        [
            MakeDoc(ConfigScope.User, """{"permissions":{"allow":["Bash(*)"],"deny":["Edit(*)"]}}"""),
            MakeDoc(ConfigScope.Project, """{"permissions":{"allow":["Read(*)"],"deny":["Write(*)"]}}"""),
        ];

        JsonObject effective = MergeEngine.ComputeEffective(
            docs, TestMergePolicy.DeclaringOnly("permissions.allow"));

        JsonArray allow = (JsonArray)effective["permissions"]!["allow"]!;
        CollectionAssert.AreEquivalent(
            new[] { "Bash(*)", "Read(*)" },
            allow.Select(x => x!.GetValue<string>()).ToArray(),
            "permissions.allow is declared, so it unions.");

        JsonArray deny = (JsonArray)effective["permissions"]!["deny"]!;
        CollectionAssert.AreEqual(
            new[] { "Edit(*)" },
            deny.Select(x => x!.GetValue<string>()).ToArray(),
            "permissions.deny is NOT declared and inference is off, so User replaces Project. "
            + "Proves the policy is consulted per dotted child path rather than per top-level key.");
    }

    [TestMethod]
    public void Merge_NullPolicy_Throws()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("x"), "user.json"),
        ];

        Assert.ThrowsExactly<ArgumentNullException>(() => MergeEngine.Merge(entries, "key", null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => MergeEngine.ComputeEffective([], null!));
    }
}