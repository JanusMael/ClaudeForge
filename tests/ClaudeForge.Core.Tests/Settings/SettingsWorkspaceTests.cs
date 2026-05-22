using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Settings;

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
        SettingsWorkspace workspace = new([doc]);

        Assert.ThrowsException<InvalidOperationException>(() =>
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

    private static SettingsWorkspace MakeWorkspace(params (ConfigScope Scope, string Json)[] entries)
    {
        IEnumerable<SettingsDocument> docs = entries.Select(e =>
        {
            JsonObject root = (JsonObject)JsonNode.Parse(e.Json)!;
            return new SettingsDocument(e.Scope, $"{e.Scope}.json", root, isReadOnly: false);
        });
        return new SettingsWorkspace(docs);
    }
}