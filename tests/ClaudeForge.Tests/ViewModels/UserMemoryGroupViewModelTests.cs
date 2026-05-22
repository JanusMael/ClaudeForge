using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// pure-data wrapper for one Tier 1
/// user-memory category group.  Tests pin every category branch of the
/// HumanLabel + Tooltip switches plus CountLabel formatting and IsEmpty.
/// </summary>
[TestClass]
public sealed class UserMemoryGroupViewModelTests
{
    private static UserMemoryFile File(UserMemoryCategory category, string name = "alpha")
    {
        return new UserMemoryFile(
            AbsolutePath: "/home/u/.claude/" + name + ".md",
            Category: category,
            DisplayName: name,
            SizeBytes: 42,
            LastWriteUtc: new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc),
            Subtitle: "first descriptive line");
    }

    // ── HumanLabel switch (lines 20-32) ─────────────────────────────

    [TestMethod]
    [DataRow(UserMemoryCategory.PrimaryMemory)]
    [DataRow(UserMemoryCategory.ProjectMemory)]
    [DataRow(UserMemoryCategory.Subagent)]
    [DataRow(UserMemoryCategory.SlashCommand)]
    [DataRow(UserMemoryCategory.Hook)]
    [DataRow(UserMemoryCategory.Plan)]
    [DataRow(UserMemoryCategory.Rule)]
    [DataRow(UserMemoryCategory.Skill)]
    [DataRow(UserMemoryCategory.CrossToolMemory)]
    public void HumanLabel_EveryCategory_ReturnsNonEmptyLocalisedLabel(UserMemoryCategory category)
    {
        UserMemoryGroupViewModel group = new(category, new List<UserMemoryFile>());
        Assert.IsFalse(string.IsNullOrEmpty(group.HumanLabel),
            $"Category {category} must have a localised HumanLabel.");
    }

    [TestMethod]
    public void HumanLabel_UnknownCategory_FallsBackToEnumToString()
    {
        // Cast an out-of-range int to hit the default branch.
        UserMemoryGroupViewModel group = new((UserMemoryCategory)999, new List<UserMemoryFile>());
        Assert.AreEqual("999", group.HumanLabel);
    }

    // ── Tooltip switch (line 54 default) ────────────────────────────

    [TestMethod]
    [DataRow(UserMemoryCategory.PrimaryMemory)]
    [DataRow(UserMemoryCategory.ProjectMemory)]
    [DataRow(UserMemoryCategory.Subagent)]
    [DataRow(UserMemoryCategory.SlashCommand)]
    [DataRow(UserMemoryCategory.Hook)]
    [DataRow(UserMemoryCategory.Plan)]
    [DataRow(UserMemoryCategory.Rule)]
    [DataRow(UserMemoryCategory.Skill)]
    [DataRow(UserMemoryCategory.CrossToolMemory)]
    public void Tooltip_EveryCategory_ReturnsNonEmptyDescription(UserMemoryCategory category)
    {
        UserMemoryGroupViewModel group = new(category, new List<UserMemoryFile>());
        Assert.IsFalse(string.IsNullOrEmpty(group.Tooltip),
            $"Category {category} must have a localised Tooltip.");
    }

    [TestMethod]
    public void Tooltip_UnknownCategory_ReturnsEmpty()
    {
        UserMemoryGroupViewModel group = new((UserMemoryCategory)999, new List<UserMemoryFile>());
        Assert.AreEqual(string.Empty, group.Tooltip);
    }

    // ── CountLabel + IsEmpty (line 35 + 58) ─────────────────────────

    [TestMethod]
    public void CountLabel_ContainsFileCount()
    {
        UserMemoryGroupViewModel group = new(
            UserMemoryCategory.Skill,
            [File(UserMemoryCategory.Skill), File(UserMemoryCategory.Skill, "beta")]);
        StringAssert.Contains(group.CountLabel, "2",
            "CountLabel must include the file count.");
    }

    [TestMethod]
    public void CountLabel_ZeroFiles_StillFormats()
    {
        UserMemoryGroupViewModel group = new(UserMemoryCategory.Skill, new List<UserMemoryFile>());
        StringAssert.Contains(group.CountLabel, "0");
    }

    [TestMethod]
    public void IsEmpty_NoFiles_ReturnsTrue()
    {
        UserMemoryGroupViewModel group = new(UserMemoryCategory.Subagent, new List<UserMemoryFile>());
        Assert.IsTrue(group.IsEmpty);
    }

    [TestMethod]
    public void IsEmpty_WithFiles_ReturnsFalse()
    {
        UserMemoryGroupViewModel group = new(
            UserMemoryCategory.Subagent,
            [File(UserMemoryCategory.Subagent)]);
        Assert.IsFalse(group.IsEmpty);
    }
}