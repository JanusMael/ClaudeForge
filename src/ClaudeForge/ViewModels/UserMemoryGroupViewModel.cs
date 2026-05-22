using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>One Tier 1 category group rendered as an Expander with its files inside.</summary>
public sealed class UserMemoryGroupViewModel
{
    public UserMemoryGroupViewModel(UserMemoryCategory category, IReadOnlyList<UserMemoryFile> files)
    {
        Category = category;
        Files = files;
    }

    public UserMemoryCategory Category { get; }
    public IReadOnlyList<UserMemoryFile> Files { get; }

    /// <summary>Localised header label per category.</summary>
    public string HumanLabel => Category switch
    {
        UserMemoryCategory.PrimaryMemory => Strings.LabelMemoryCategoryPrimary,
        UserMemoryCategory.ProjectMemory => Strings.LabelMemoryCategoryProject,
        UserMemoryCategory.Subagent => Strings.LabelMemoryCategorySubagents,
        UserMemoryCategory.SlashCommand => Strings.LabelMemoryCategoryCommands,
        UserMemoryCategory.Hook => Strings.LabelMemoryCategoryHooks,
        UserMemoryCategory.Plan => Strings.LabelMemoryCategoryPlans,
        UserMemoryCategory.Rule => Strings.LabelMemoryCategoryRules,
        UserMemoryCategory.Skill => Strings.LabelMemoryCategorySkills,
        UserMemoryCategory.CrossToolMemory => Strings.LabelMemoryCategoryCrossTool,
        var _ => Category.ToString(),
    };

    /// <summary>Inline summary "(<i>n</i> file<i>s</i>)" for the Expander header.</summary>
    public string CountLabel => string.Format(Strings.LabelFileCountFmt, Files.Count);

    /// <summary>
    /// One-sentence description shown as a hover tooltip on the Expander
    /// header.  Surfaces what the category contains and where Claude reads
    /// from, so the user can audit at a glance without opening individual
    /// files.
    /// </summary>
    public string Tooltip => Category switch
    {
        UserMemoryCategory.PrimaryMemory => Strings.TipMemoryCategoryPrimary,
        UserMemoryCategory.ProjectMemory => Strings.TipMemoryCategoryProject,
        UserMemoryCategory.Subagent => Strings.TipMemoryCategorySubagents,
        UserMemoryCategory.SlashCommand => Strings.TipMemoryCategoryCommands,
        UserMemoryCategory.Hook => Strings.TipMemoryCategoryHooks,
        UserMemoryCategory.Plan => Strings.TipMemoryCategoryPlans,
        UserMemoryCategory.Rule => Strings.TipMemoryCategoryRules,
        UserMemoryCategory.Skill => Strings.TipMemoryCategorySkills,
        UserMemoryCategory.CrossToolMemory => Strings.TipMemoryCategoryCrossTool,
        var _ => string.Empty,
    };

    /// <summary><see langword="true"/> when the group has no files; the View renders a placeholder row.</summary>
    public bool IsEmpty => Files.Count == 0;
}