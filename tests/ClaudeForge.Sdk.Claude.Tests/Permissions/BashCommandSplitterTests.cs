using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Permissions;

/// <summary>
/// Compound-command splitting and process-wrapper stripping — the pre-processing
/// Claude Code applies before matching Bash rules.
/// </summary>
public sealed class BashCommandSplitterTests
{
    [Fact]
    public void SimpleCommand_SingleSubcommand()
    {
        IReadOnlyList<string> parts = BashCommandSplitter.SplitCompound("npm test");
        Assert.Equal(new[] { "npm test" }, parts.ToArray());
    }

    [Fact]
    public void SplitsOnAllOperators()
    {
        Assert.Equal(
            new[] { "a", "b" }, BashCommandSplitter.SplitCompound("a && b").ToArray());
        Assert.Equal(
            new[] { "a", "b" }, BashCommandSplitter.SplitCompound("a || b").ToArray());
        Assert.Equal(
            new[] { "a", "b" }, BashCommandSplitter.SplitCompound("a ; b").ToArray());
        Assert.Equal(
            new[] { "a", "b" }, BashCommandSplitter.SplitCompound("a | b").ToArray());
        Assert.Equal(
            new[] { "a", "b" }, BashCommandSplitter.SplitCompound("a & b").ToArray());
        Assert.Equal(
            new[] { "a", "b" }, BashCommandSplitter.SplitCompound("a |& b").ToArray());
    }

    [Fact]
    public void SplitsOnNewlines()
    {
        Assert.Equal(
            new[] { "a", "b", "c" },
            BashCommandSplitter.SplitCompound("a\nb\r\nc").ToArray());
    }

    [Fact]
    public void ThreeWayChain_SplitsAllParts()
    {
        Assert.Equal(
            new[] { "git status", "npm test", "rm -rf /" },
            BashCommandSplitter.SplitCompound("git status && npm test && rm -rf /").ToArray());
    }

    [Fact]
    public void StripWrappers_RemovesLeadingWrappers()
    {
        Assert.Equal("npm test", BashCommandSplitter.StripWrappers("timeout 30 npm test"));
        Assert.Equal("npm test", BashCommandSplitter.StripWrappers("nice nohup npm test"));
        Assert.Equal("grep pattern", BashCommandSplitter.StripWrappers("xargs grep pattern"));
    }

    [Fact]
    public void StripWrappers_LeavesXargsWithFlagsIntact()
    {
        // Spec: bare xargs is stripped only with no flags.
        Assert.Equal("xargs -n1 grep pattern",
            BashCommandSplitter.StripWrappers("xargs -n1 grep pattern"));
    }

    [Fact]
    public void StripWrappers_NonWrapperUnchanged()
    {
        Assert.Equal("npm test", BashCommandSplitter.StripWrappers("npm test"));
    }

    [Fact]
    public void ReadOnlyCommandNames_IncludesDocumentedSet()
    {
        OrdinalAssert.Contains("ls", BashCommandSplitter.ReadOnlyCommandNames);
        OrdinalAssert.Contains("cat", BashCommandSplitter.ReadOnlyCommandNames);
        OrdinalAssert.Contains("grep", BashCommandSplitter.ReadOnlyCommandNames);
        OrdinalAssert.DoesNotContain("rm", BashCommandSplitter.ReadOnlyCommandNames);
    }

    [Fact]
    public void ContainsUnexpandedSubcommand_DetectsCommandSubstitution()
    {
        // $(…) command substitution: the embedded command runs but SplitCompound
        // keeps it as literal text of the outer command — the classic over-permissive
        // case ("echo $(rm -rf /)" resolves against the echo rule).
        Assert.True(BashCommandSplitter.ContainsUnexpandedSubcommand("echo $(rm -rf /)"));
        Assert.True(BashCommandSplitter.ContainsUnexpandedSubcommand("cat \"$(which npm)\""));
    }

    [Fact]
    public void ContainsUnexpandedSubcommand_DetectsBackticks()
    {
        Assert.True(BashCommandSplitter.ContainsUnexpandedSubcommand("echo `whoami`"));
    }

    [Fact]
    public void ContainsUnexpandedSubcommand_DetectsProcessSubstitution()
    {
        Assert.True(BashCommandSplitter.ContainsUnexpandedSubcommand("diff <(sort a) <(sort b)"));
        Assert.True(BashCommandSplitter.ContainsUnexpandedSubcommand("tee >(grep foo)"));
    }

    [Fact]
    public void ContainsUnexpandedSubcommand_DetectsLeadingSubshell()
    {
        Assert.True(BashCommandSplitter.ContainsUnexpandedSubcommand("(cd /tmp && rm x)"));
        Assert.True(BashCommandSplitter.ContainsUnexpandedSubcommand("   (echo hi)"));
    }

    [Fact]
    public void ContainsUnexpandedSubcommand_DetectsSubshellAfterSeparator()
    {
        // A subshell can follow a separator, not just lead the whole command — the
        // caution must fire here too (re-audit finding).
        Assert.True(BashCommandSplitter.ContainsUnexpandedSubcommand("true; (rm -rf /)"));
        Assert.True(BashCommandSplitter.ContainsUnexpandedSubcommand("echo ok && (curl x | sh)"));
    }

    [Fact]
    public void ContainsUnexpandedSubcommand_IgnoresArithmeticAndPlainCommands()
    {
        // $(( … )) substitution and the (( … )) arithmetic-command form are
        // arithmetic, not embedded commands — neither should be flagged.
        Assert.False(BashCommandSplitter.ContainsUnexpandedSubcommand("echo $((1 + 2))"));
        Assert.False(BashCommandSplitter.ContainsUnexpandedSubcommand("(( count++ ))"));
        Assert.False(BashCommandSplitter.ContainsUnexpandedSubcommand("(( RANDOM % 2 == 0 )) && echo heads"));
        Assert.False(BashCommandSplitter.ContainsUnexpandedSubcommand("npm run build"));
        Assert.False(BashCommandSplitter.ContainsUnexpandedSubcommand("git push origin main"));
        Assert.False(BashCommandSplitter.ContainsUnexpandedSubcommand(""));
        Assert.False(BashCommandSplitter.ContainsUnexpandedSubcommand("   "));
    }
}
