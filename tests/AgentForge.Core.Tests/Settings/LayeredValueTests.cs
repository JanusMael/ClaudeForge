using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Settings;

public class LayeredValueTests
{
    // -----------------------------------------------------------------------
    // IsManagedLocked
    // -----------------------------------------------------------------------

    [Fact]
    public void IsManagedLocked_WhenManagedScopePresent_ReturnsTrue()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.Managed, JsonValue.Create("policy"), "/managed.json"),
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user"), "/user.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.True(layered.IsManagedLocked);
    }

    [Fact]
    public void IsManagedLocked_WhenNoManagedScope_ReturnsFalse()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user"), "/user.json"),
            new ScopeEntry(ConfigScope.Project, JsonValue.Create("project"), "/project.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.False(layered.IsManagedLocked);
    }

    // -----------------------------------------------------------------------
    // IsOverridden
    // -----------------------------------------------------------------------

    [Fact]
    public void IsOverridden_SingleEntry_ReturnsFalse()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("sonnet"), "/user.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.False(layered.IsOverridden);
    }

    [Fact]
    public void IsOverridden_MultipleEntries_ReturnsTrue()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("sonnet"), "/user.json"),
            new ScopeEntry(ConfigScope.Project, JsonValue.Create("haiku"), "/project.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.True(layered.IsOverridden);
    }

    // -----------------------------------------------------------------------
    // GetValueAt
    // -----------------------------------------------------------------------

    [Fact]
    public void GetValueAt_PresentScope_ReturnsValue()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user-val"), "/user.json"),
            new ScopeEntry(ConfigScope.Project, JsonValue.Create("project-val"), "/project.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.Equal("user-val", layered.GetValueAt(ConfigScope.User)!.GetValue<string>());
        Assert.Equal("project-val", layered.GetValueAt(ConfigScope.Project)!.GetValue<string>());
    }

    [Fact]
    public void GetValueAt_AbsentScope_ReturnsNull()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user-val"), "/user.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.Null(layered.GetValueAt(ConfigScope.Local));
    }

    // -----------------------------------------------------------------------
    // IsDefinedAt
    // -----------------------------------------------------------------------

    [Fact]
    public void IsDefinedAt_PresentScope_ReturnsTrue()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user-val"), "/user.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.True(layered.IsDefinedAt(ConfigScope.User));
    }

    [Fact]
    public void IsDefinedAt_AbsentScope_ReturnsFalse()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user-val"), "/user.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.False(layered.IsDefinedAt(ConfigScope.Project));
    }

    // -----------------------------------------------------------------------
    // Entries ordering — highest-priority scope first
    // -----------------------------------------------------------------------

    [Fact]
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
        Assert.Equal(ConfigScope.Local, layered.Entries[0].Scope);
        Assert.Equal(ConfigScope.Project, layered.Entries[1].Scope);
        Assert.Equal(ConfigScope.User, layered.Entries[2].Scope);
    }

    [Fact]
    public void Entries_SortedHighestPriorityFirst_WithManaged()
    {
        // When Managed is present it must be first (priority 0).
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("user"), "/user.json"),
            new ScopeEntry(ConfigScope.Managed, JsonValue.Create("managed"), "/managed.json"),
        ];

        LayeredValue layered = new("model", entries);

        Assert.Equal(ConfigScope.Managed, layered.Entries[0].Scope);
        Assert.Equal(ConfigScope.User, layered.Entries[1].Scope);
    }
}