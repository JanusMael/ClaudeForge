using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Settings;

[TestClass]
public class SettingsWorkspaceTests
{
    [TestMethod]
    public void GetLayeredValue_ReturnsAllEntries()
    {
        SettingsWorkspace workspace = MakeWorkspace(
            (ConfigScope.User, """{"model":"sonnet"}"""),
            (ConfigScope.Project, """{"model":"haiku"}"""));

        LayeredValue layered = workspace.GetLayeredValue("model");

        Assert.AreEqual(2, layered.Entries.Count);
        Assert.IsTrue(layered.IsOverridden);
    }

    [TestMethod]
    public void GetLayeredValue_EffectiveValue_ProjectWinsOverUser()
    {
        // ConfigScope priority (lower numeric value = higher priority):
        //   Managed (0) > Local (1) > Project (2) > User (3)
        // So Project's "haiku" must beat User's "sonnet" — Project is the
        // narrower scope that intentionally overrides the user-global default.
        SettingsWorkspace workspace = MakeWorkspace(
            (ConfigScope.User, """{"model":"sonnet"}"""),
            (ConfigScope.Project, """{"model":"haiku"}"""));

        LayeredValue layered = workspace.GetLayeredValue("model");

        Assert.AreEqual("haiku", layered.EffectiveValue!.GetValue<string>());
        Assert.AreEqual(ConfigScope.Project, layered.EffectiveScope);
    }

    [TestMethod]
    public void SetValue_MarksDocumentDirty()
    {
        SettingsWorkspace workspace = MakeWorkspace(
            (ConfigScope.User, """{}"""));

        workspace.SetValue("model", JsonValue.Create("opus"), ConfigScope.User);

        Assert.IsTrue(workspace.Documents.Single(d => d.Scope == ConfigScope.User).IsDirty);
    }

    [TestMethod]
    public void RemoveValue_RemovesKeyFromScope()
    {
        SettingsWorkspace workspace = MakeWorkspace(
            (ConfigScope.User, """{"model":"sonnet"}"""));

        workspace.RemoveValue("model", ConfigScope.User);

        LayeredValue layered = workspace.GetLayeredValue("model");
        Assert.AreEqual(0, layered.Entries.Count);
    }

    [TestMethod]
    public void RemoveValue_AbsentKey_IsNoOp_DoesNotFireChanged()
    {
        SettingsWorkspace workspace = MakeWorkspace(
            (ConfigScope.User, """{}""")); // key never set

        int eventCount = 0;
        workspace.Changed += (_, _) => eventCount++;

        workspace.RemoveValue("model", ConfigScope.User); // key absent → should be a no-op

        Assert.AreEqual(0, eventCount, "Changed must not fire when key was not present");
        Assert.IsFalse(workspace.Documents[0].IsDirty, "document must not be marked dirty");
    }

    [TestMethod]
    public void RemoveValue_AbsentKey_DoesNotAffectHasActualChanges()
    {
        // Simulates: user opens page, clicks Reset on a field that was never set at this scope.
        // The document should remain clean (HasActualChanges = false) after the no-op remove.
        SettingsWorkspace workspace = MakeWorkspace(
            (ConfigScope.User, """{}"""));

        workspace.RemoveValue("model", ConfigScope.User);

        Assert.IsFalse(workspace.Documents[0].HasActualChanges());
    }

    [TestMethod]
    public void SetValue_ReadOnlyScope_Throws()
    {
        SettingsDocument doc = new(ConfigScope.Managed, "/managed.json", new JsonObject(), isReadOnly: true);
        SettingsWorkspace workspace = new([doc], TestMergePolicy.Inferring);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            workspace.SetValue("model", JsonValue.Create("x"), ConfigScope.Managed));
    }

    [TestMethod]
    public void ComputeEffective_ProducesFullMerge()
    {
        // ConfigScope priority (lower numeric value = higher priority):
        //   Managed (0) > Local (1) > Project (2) > User (3)
        // So when both User and Project define the same key, Project wins.
        // Per-key:
        //   model              — only User defines it → "sonnet"
        //   language           — both define it → Project's "fr" wins
        //   cleanupPeriodDays  — only Project defines it → 60
        SettingsWorkspace workspace = MakeWorkspace(
            (ConfigScope.User, """{"model":"sonnet","language":"en"}"""),
            (ConfigScope.Project, """{"language":"fr","cleanupPeriodDays":60}"""));

        JsonObject effective = workspace.ComputeEffective();

        Assert.AreEqual("sonnet", effective["model"]!.GetValue<string>());
        Assert.AreEqual("fr", effective["language"]!.GetValue<string>()); // project wins
        Assert.AreEqual(60, effective["cleanupPeriodDays"]!.GetValue<int>());
    }

    // ───────────────────────────────────────────────────────────────────────
    //  The workspace uses the policy it was HANDED
    //
    //  Claude's list of union-merged paths used to be a private static field on
    //  SettingsWorkspace, so every workspace in the process merged Claude's way whatever
    //  product opened it. The two tests below are the pair that would have caught that:
    //  identical documents, two different policies, two different effective values. If a
    //  hardcoded rule ever returns, one of them goes red.
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetLayeredValue_UnionsWhenThePolicyDeclaresThePath()
    {
        SettingsWorkspace workspace = MakeWorkspace(
            TestMergePolicy.Declaring("tools"),
            (ConfigScope.Project, """{"tools":["b"]}"""),
            (ConfigScope.User, """{"tools":["a"]}"""));

        LayeredValue value = workspace.GetLayeredValue("tools");

        JsonArray tools = (JsonArray)value.EffectiveValue!;
        CollectionAssert.AreEquivalent(
            new[] { "b", "a" },
            tools.Select(t => t!.GetValue<string>()).ToArray(),
            "A declared path unions both scopes' contributions.");
    }

    [TestMethod]
    public void GetLayeredValue_ReplacesWhenThePolicyDoesNotUnion()
    {
        // Same documents, same key, a policy that never unions: the highest-priority scope
        // replaces the rest, and the lower scope's entry is absent rather than appended.
        SettingsWorkspace workspace = MakeWorkspace(
            TestMergePolicy.NeverUnions,
            (ConfigScope.Project, """{"tools":["b"]}"""),
            (ConfigScope.User, """{"tools":["a"]}"""));

        LayeredValue value = workspace.GetLayeredValue("tools");

        JsonArray tools = (JsonArray)value.EffectiveValue!;
        CollectionAssert.AreEqual(
            new[] { "b" },
            tools.Select(t => t!.GetValue<string>()).ToArray(),
            "Without a union rule, Project replaces User outright — this is OpenCode's "
            + "documented behaviour for most of its array keys, so the workspace must be "
            + "able to express it.");
    }

    [TestMethod]
    public void Constructor_NullPolicy_Throws()
    {
        // A defaulted policy is what would let a new product silently inherit Claude's
        // rules, so omitting one is a programmer error rather than a shrug.
        SettingsDocument doc = new(ConfigScope.User, "/user.json", new JsonObject(), isReadOnly: false);

        Assert.ThrowsExactly<ArgumentNullException>(() => new SettingsWorkspace([doc], null!));
    }

    private static SettingsWorkspace MakeWorkspace(params (ConfigScope Scope, string Json)[] entries)
    {
        return MakeWorkspace(TestMergePolicy.Inferring, entries);
    }

    private static SettingsWorkspace MakeWorkspace(
        IMergePolicy policy,
        params (ConfigScope Scope, string Json)[] entries)
    {
        IEnumerable<SettingsDocument> docs = entries.Select(e =>
        {
            JsonObject root = (JsonObject)JsonNode.Parse(e.Json)!;
            return new SettingsDocument(e.Scope, $"{e.Scope}.json", root, isReadOnly: false);
        });
        return new SettingsWorkspace(docs, policy);
    }
}