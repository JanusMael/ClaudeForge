using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Settings;

[TestClass]
public class LayeredValueTests
{
    // -----------------------------------------------------------------------
    // IsManagedLocked
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IsManagedLocked_WhenManagedScopePresent_ReturnsTrue()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.Managed, JsonValue.Create("policy"), "/managed.json"),
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user"), "/user.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.IsTrue(layered.IsManagedLocked);
    }

    [TestMethod]
    public void IsManagedLocked_WhenNoManagedScope_ReturnsFalse()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user"), "/user.json"),
            new ScopeEntry(ConfigScope.Project, JsonValue.Create("project"), "/project.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.IsFalse(layered.IsManagedLocked);
    }

    // -----------------------------------------------------------------------
    // IsOverridden
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IsOverridden_SingleEntry_ReturnsFalse()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("sonnet"), "/user.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.IsFalse(layered.IsOverridden);
    }

    [TestMethod]
    public void IsOverridden_MultipleEntries_ReturnsTrue()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("sonnet"), "/user.json"),
            new ScopeEntry(ConfigScope.Project, JsonValue.Create("haiku"), "/project.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.IsTrue(layered.IsOverridden);
    }

    // -----------------------------------------------------------------------
    // GetValueAt
    // -----------------------------------------------------------------------

    [TestMethod]
    public void GetValueAt_PresentScope_ReturnsValue()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user-val"), "/user.json"),
            new ScopeEntry(ConfigScope.Project, JsonValue.Create("project-val"), "/project.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.AreEqual("user-val", layered.GetValueAt(ConfigScope.User)!.GetValue<string>());
        Assert.AreEqual("project-val", layered.GetValueAt(ConfigScope.Project)!.GetValue<string>());
    }

    [TestMethod]
    public void GetValueAt_AbsentScope_ReturnsNull()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user-val"), "/user.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.IsNull(layered.GetValueAt(ConfigScope.Local));
    }

    // -----------------------------------------------------------------------
    // IsDefinedAt
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IsDefinedAt_PresentScope_ReturnsTrue()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user-val"), "/user.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.IsTrue(layered.IsDefinedAt(ConfigScope.User));
    }

    [TestMethod]
    public void IsDefinedAt_AbsentScope_ReturnsFalse()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user-val"), "/user.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.IsFalse(layered.IsDefinedAt(ConfigScope.Project));
    }

    // -----------------------------------------------------------------------
    // Entries ordering — highest-priority scope first
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Entries_SortedHighestPriorityFirst()
    {
        // Construct with Local, Project, User in insertion order;
        // the constructor must sort by scope value (lower = higher priority).
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.Local, JsonValue.Create("local"), "/local.json"),
            new ScopeEntry(ConfigScope.Project, JsonValue.Create("project"), "/project.json"),
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user"), "/user.json"),
        ];

        LayeredValue layered = new("model", entries);

        // Local (1) < Project (2) < User (3), so Local should be first.
        // (Scope priority was corrected post-Project-scope addition: Local
        // is the highest-priority user-editable scope, then Project, then
        // User. Lower numeric value = higher priority. See ConfigScope.cs.)
        Assert.AreEqual(ConfigScope.Local, layered.Entries[0].Scope);
        Assert.AreEqual(ConfigScope.Project, layered.Entries[1].Scope);
        Assert.AreEqual(ConfigScope.User, layered.Entries[2].Scope);
    }

    [TestMethod]
    public void Entries_SortedHighestPriorityFirst_WithManaged()
    {
        // When Managed is present it must be first (priority 0).
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user"), "/user.json"),
            new ScopeEntry(ConfigScope.Managed, JsonValue.Create("managed"), "/managed.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.AreEqual(ConfigScope.Managed, layered.Entries[0].Scope);
        Assert.AreEqual(ConfigScope.User, layered.Entries[1].Scope);
    }
}