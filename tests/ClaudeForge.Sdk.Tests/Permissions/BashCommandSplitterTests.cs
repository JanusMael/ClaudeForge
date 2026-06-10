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
}
