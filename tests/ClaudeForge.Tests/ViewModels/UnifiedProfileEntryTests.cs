using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

[TestClass]
public sealed class UnifiedProfileEntryTests
{
    // -----------------------------------------------------------------------
    // Global sentinel
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Global_HasCliTrue()
    {
        Assert.IsTrue(UnifiedProfileEntry.Global.HasCli);
    }

    [TestMethod]
    public void Global_HasDesktopTrue()
    {
        Assert.IsTrue(UnifiedProfileEntry.Global.HasDesktop);
    }

    [TestMethod]
    public void Global_IsGlobal_IsTrue()
    {
        Assert.IsTrue(UnifiedProfileEntry.Global.IsGlobal);
    }

    [TestMethod]
    public void GlobalName_MatchesMainWindowViewModelSentinel()
    {
        // The sentinel string must be identical so that persisted profile names
        // round-trip correctly through SelectedProfile (string) and SelectedProfileEntry.
        Assert.AreEqual(MainWindowViewModel.GlobalProfileSentinel, UnifiedProfileEntry.GlobalName);
    }

    // -----------------------------------------------------------------------
    // IsGlobal
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IsGlobal_FalseForCliOnlyProfile()
    {
        UnifiedProfileEntry entry = new("work", HasCli: true, HasDesktop: false);
        Assert.IsFalse(entry.IsGlobal);
    }

    [TestMethod]
    public void IsGlobal_FalseForDesktopOnlyProfile()
    {
        UnifiedProfileEntry entry = new("work", HasCli: false, HasDesktop: true);
        Assert.IsFalse(entry.IsGlobal);
    }

    [TestMethod]
    public void IsGlobal_FalseForSharedProfile()
    {
        UnifiedProfileEntry entry = new("work", HasCli: true, HasDesktop: true);
        Assert.IsFalse(entry.IsGlobal);
    }

    // -----------------------------------------------------------------------
    // ShowCliChiclet
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ShowCliChiclet_TrueWhenHasCliAndNotGlobal()
    {
        UnifiedProfileEntry entry = new("work", HasCli: true, HasDesktop: false);
        Assert.IsTrue(entry.ShowCliChiclet);
    }

    [TestMethod]
    public void ShowCliChiclet_TrueForSharedNonGlobalProfile()
    {
        UnifiedProfileEntry entry = new("work", HasCli: true, HasDesktop: true);
        Assert.IsTrue(entry.ShowCliChiclet);
    }

    [TestMethod]
    public void ShowCliChiclet_FalseWhenHasCliButIsGlobal()
    {
        // Global entry never shows a chiclet even though it represents "both products".
        Assert.IsFalse(UnifiedProfileEntry.Global.ShowCliChiclet);
    }

    [TestMethod]
    public void ShowCliChiclet_FalseWhenDesktopOnly()
    {
        UnifiedProfileEntry entry = new("home", HasCli: false, HasDesktop: true);
        Assert.IsFalse(entry.ShowCliChiclet);
    }

    // -----------------------------------------------------------------------
    // ShowDesktopChiclet
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ShowDesktopChiclet_TrueWhenHasDesktopAndNotGlobal()
    {
        UnifiedProfileEntry entry = new("home", HasCli: false, HasDesktop: true);
        Assert.IsTrue(entry.ShowDesktopChiclet);
    }

    [TestMethod]
    public void ShowDesktopChiclet_TrueForSharedNonGlobalProfile()
    {
        UnifiedProfileEntry entry = new("work", HasCli: true, HasDesktop: true);
        Assert.IsTrue(entry.ShowDesktopChiclet);
    }

    [TestMethod]
    public void ShowDesktopChiclet_FalseWhenHasDesktopButIsGlobal()
    {
        Assert.IsFalse(UnifiedProfileEntry.Global.ShowDesktopChiclet);
    }

    [TestMethod]
    public void ShowDesktopChiclet_FalseWhenCliOnly()
    {
        UnifiedProfileEntry entry = new("work", HasCli: true, HasDesktop: false);
        Assert.IsFalse(entry.ShowDesktopChiclet);
    }

    // -----------------------------------------------------------------------
    // ToString
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ToString_ReturnsName()
    {
        UnifiedProfileEntry entry = new("personal", HasCli: true, HasDesktop: false);
        Assert.AreEqual("personal", entry.ToString());
    }

    [TestMethod]
    public void ToString_GlobalReturnsGlobalName()
    {
        Assert.AreEqual(UnifiedProfileEntry.GlobalName, UnifiedProfileEntry.Global.ToString());
    }
}