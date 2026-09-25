using Bennewitz.Ninja.AgentForge.Sdk.Memory;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// pure-data wrapper for one Tier 1
/// user-memory category group.  Tests pin every category branch of the
/// HumanLabel + Tooltip switches plus CountLabel formatting and IsEmpty.
/// </summary>
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

    [Theory]
    [InlineData(UserMemoryCategory.PrimaryMemory)]
    [InlineData(UserMemoryCategory.ProjectMemory)]
    [InlineData(UserMemoryCategory.Subagent)]
    [InlineData(UserMemoryCategory.SlashCommand)]
    [InlineData(UserMemoryCategory.Hook)]
    [InlineData(UserMemoryCategory.Plan)]
    [InlineData(UserMemoryCategory.Rule)]
    [InlineData(UserMemoryCategory.Skill)]
    [InlineData(UserMemoryCategory.CrossToolMemory)]
    public void HumanLabel_EveryCategory_ReturnsNonEmptyLocalisedLabel(UserMemoryCategory category)
    {
        UserMemoryGroupViewModel group = new(category, new List<UserMemoryFile>());
        Assert.False(string.IsNullOrEmpty(group.HumanLabel),
            $"Category {category} must have a localised HumanLabel.");
    }

    [Fact]
    public void HumanLabel_UnknownCategory_FallsBackToEnumToString()
    {
        // Cast an out-of-range int to hit the default branch.
        UserMemoryGroupViewModel group = new((UserMemoryCategory)999, new List<UserMemoryFile>());
        Assert.Equal("999", group.HumanLabel);
    }

    // ── Tooltip switch (line 54 default) ────────────────────────────

    [Theory]
    [InlineData(UserMemoryCategory.PrimaryMemory)]
    [InlineData(UserMemoryCategory.ProjectMemory)]
    [InlineData(UserMemoryCategory.Subagent)]
    [InlineData(UserMemoryCategory.SlashCommand)]
    [InlineData(UserMemoryCategory.Hook)]
    [InlineData(UserMemoryCategory.Plan)]
    [InlineData(UserMemoryCategory.Rule)]
    [InlineData(UserMemoryCategory.Skill)]
    [InlineData(UserMemoryCategory.CrossToolMemory)]
    public void Tooltip_EveryCategory_ReturnsNonEmptyDescription(UserMemoryCategory category)
    {
        UserMemoryGroupViewModel group = new(category, new List<UserMemoryFile>());
        Assert.False(string.IsNullOrEmpty(group.Tooltip),
            $"Category {category} must have a localised Tooltip.");
    }

    [Fact]
    public void Tooltip_UnknownCategory_ReturnsEmpty()
    {
        UserMemoryGroupViewModel group = new((UserMemoryCategory)999, new List<UserMemoryFile>());
        Assert.Equal(string.Empty, group.Tooltip);
    }

    // ── CountLabel + IsEmpty (line 35 + 58) ─────────────────────────

    [Fact]
    public void CountLabel_ContainsFileCount()
    {
        UserMemoryGroupViewModel group = new(
            UserMemoryCategory.Skill,
            [File(UserMemoryCategory.Skill), File(UserMemoryCategory.Skill, "beta")]);
        MessageAssert.Contains("2", group.CountLabel,
            "CountLabel must include the file count.");
    }

    [Fact]
    public void CountLabel_ZeroFiles_StillFormats()
    {
        UserMemoryGroupViewModel group = new(UserMemoryCategory.Skill, new List<UserMemoryFile>());
        OrdinalAssert.Contains("0", group.CountLabel);
    }

    [Fact]
    public void IsEmpty_NoFiles_ReturnsTrue()
    {
        UserMemoryGroupViewModel group = new(UserMemoryCategory.Subagent, new List<UserMemoryFile>());
        Assert.True(group.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithFiles_ReturnsFalse()
    {
        UserMemoryGroupViewModel group = new(
            UserMemoryCategory.Subagent,
            [File(UserMemoryCategory.Subagent)]);
        Assert.False(group.IsEmpty);
    }
}