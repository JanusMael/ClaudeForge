using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Permissions;

/// <summary>
/// Compound-command splitting and process-wrapper stripping — the pre-processing
/// Claude Code applies before matching Bash rules.
/// </summary>
[TestClass]
public sealed class BashCommandSplitterTests
{
    [TestMethod]
    public void SimpleCommand_SingleSubcommand()
    {
        IReadOnlyList<string> parts = BashCommandSplitter.SplitCompound("npm test");
        CollectionAssert.AreEqual(new[] { "npm test" }, parts.ToArray());
    }

    [TestMethod]
    public void SplitsOnAllOperators()
    {
        CollectionAssert.AreEqual(
            new[] { "a", "b" }, BashCommandSplitter.SplitCompound("a && b").ToArray());
        CollectionAssert.AreEqual(
            new[] { "a", "b" }, BashCommandSplitter.SplitCompound("a || b").ToArray());
        CollectionAssert.AreEqual(
            new[] { "a", "b" }, BashCommandSplitter.SplitCompound("a ; b").ToArray());
        CollectionAssert.AreEqual(
            new[] { "a", "b" }, BashCommandSplitter.SplitCompound("a | b").ToArray());
        CollectionAssert.AreEqual(
            new[] { "a", "b" }, BashCommandSplitter.SplitCompound("a & b").ToArray());
        CollectionAssert.AreEqual(
            new[] { "a", "b" }, BashCommandSplitter.SplitCompound("a |& b").ToArray());
    }

    [TestMethod]
    public void SplitsOnNewlines()
    {
        CollectionAssert.AreEqual(
            new[] { "a", "b", "c" },
            BashCommandSplitter.SplitCompound("a\nb\r\nc").ToArray());
    }

    [TestMethod]
    public void ThreeWayChain_SplitsAllParts()
    {
        CollectionAssert.AreEqual(
            new[] { "git status", "npm test", "rm -rf /" },
            BashCommandSplitter.SplitCompound("git status && npm test && rm -rf /").ToArray());
    }

    [TestMethod]
    public void StripWrappers_RemovesLeadingWrappers()
    {
        Assert.AreEqual("npm test", BashCommandSplitter.StripWrappers("timeout 30 npm test"));
        Assert.AreEqual("npm test", BashCommandSplitter.StripWrappers("nice nohup npm test"));
        Assert.AreEqual("grep pattern", BashCommandSplitter.StripWrappers("xargs grep pattern"));
    }

    [TestMethod]
    public void StripWrappers_LeavesXargsWithFlagsIntact()
    {
        // Spec: bare xargs is stripped only with no flags.
        Assert.AreEqual("xargs -n1 grep pattern",
            BashCommandSplitter.StripWrappers("xargs -n1 grep pattern"));
    }

    [TestMethod]
    public void StripWrappers_NonWrapperUnchanged()
    {
        Assert.AreEqual("npm test", BashCommandSplitter.StripWrappers("npm test"));
    }

    [TestMethod]
    public void ReadOnlyCommandNames_IncludesDocumentedSet()
    {
        Assert.IsTrue(BashCommandSplitter.ReadOnlyCommandNames.Contains("ls"));
        Assert.IsTrue(BashCommandSplitter.ReadOnlyCommandNames.Contains("cat"));
        Assert.IsTrue(BashCommandSplitter.ReadOnlyCommandNames.Contains("grep"));
        Assert.IsFalse(BashCommandSplitter.ReadOnlyCommandNames.Contains("rm"));
    }

    [TestMethod]
    public void ContainsUnexpandedSubcommand_DetectsCommandSubstitution()
    {
        // $(…) command substitution: the embedded command runs but SplitCompound
        // keeps it as literal text of the outer command — the classic over-permissive
        // case ("echo $(rm -rf /)" resolves against the echo rule).
        Assert.IsTrue(BashCommandSplitter.ContainsUnexpandedSubcommand("echo $(rm -rf /)"));
        Assert.IsTrue(BashCommandSplitter.ContainsUnexpandedSubcommand("cat \"$(which npm)\""));
    }

    [TestMethod]
    public void ContainsUnexpandedSubcommand_DetectsBackticks()
    {
        Assert.IsTrue(BashCommandSplitter.ContainsUnexpandedSubcommand("echo `whoami`"));
    }

    [TestMethod]
    public void ContainsUnexpandedSubcommand_DetectsProcessSubstitution()
    {
        Assert.IsTrue(BashCommandSplitter.ContainsUnexpandedSubcommand("diff <(sort a) <(sort b)"));
        Assert.IsTrue(BashCommandSplitter.ContainsUnexpandedSubcommand("tee >(grep foo)"));
    }

    [TestMethod]
    public void ContainsUnexpandedSubcommand_DetectsLeadingSubshell()
    {
        Assert.IsTrue(BashCommandSplitter.ContainsUnexpandedSubcommand("(cd /tmp && rm x)"));
        Assert.IsTrue(BashCommandSplitter.ContainsUnexpandedSubcommand("   (echo hi)"));
    }

    [TestMethod]
    public void ContainsUnexpandedSubcommand_DetectsSubshellAfterSeparator()
    {
        // A subshell can follow a separator, not just lead the whole command — the
        // caution must fire here too (re-audit finding).
        Assert.IsTrue(BashCommandSplitter.ContainsUnexpandedSubcommand("true; (rm -rf /)"));
        Assert.IsTrue(BashCommandSplitter.ContainsUnexpandedSubcommand("echo ok && (curl x | sh)"));
    }

    [TestMethod]
    public void ContainsUnexpandedSubcommand_IgnoresArithmeticAndPlainCommands()
    {
        // $(( … )) substitution and the (( … )) arithmetic-command form are
        // arithmetic, not embedded commands — neither should be flagged.
        Assert.IsFalse(BashCommandSplitter.ContainsUnexpandedSubcommand("echo $((1 + 2))"));
        Assert.IsFalse(BashCommandSplitter.ContainsUnexpandedSubcommand("(( count++ ))"));
        Assert.IsFalse(BashCommandSplitter.ContainsUnexpandedSubcommand("(( RANDOM % 2 == 0 )) && echo heads"));
        Assert.IsFalse(BashCommandSplitter.ContainsUnexpandedSubcommand("npm run build"));
        Assert.IsFalse(BashCommandSplitter.ContainsUnexpandedSubcommand("git push origin main"));
        Assert.IsFalse(BashCommandSplitter.ContainsUnexpandedSubcommand(""));
        Assert.IsFalse(BashCommandSplitter.ContainsUnexpandedSubcommand("   "));
    }
}
