using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

public sealed class UnifiedProfileEntryTests
{
    // -----------------------------------------------------------------------
    // Global sentinel
    // -----------------------------------------------------------------------

    [Fact]
    public void Global_HasCliTrue()
    {
        Assert.True(UnifiedProfileEntry.Global.HasCli);
    }

    [Fact]
    public void Global_HasDesktopTrue()
    {
        Assert.True(UnifiedProfileEntry.Global.HasDesktop);
    }

    [Fact]
    public void Global_IsGlobal_IsTrue()
    {
        Assert.True(UnifiedProfileEntry.Global.IsGlobal);
    }

    [Fact]
    public void GlobalName_MatchesMainWindowViewModelSentinel()
    {
        // The sentinel string must be identical so that persisted profile names
        // round-trip correctly through SelectedProfile (string) and SelectedProfileEntry.
        // MSTEST0032: both are consts, so this folds to always-true. Catching the day
        // someone changes one and not the other is exactly why the assert is here.
#pragma warning disable MSTEST0032
        Assert.Equal(MainWindowViewModel.GlobalProfileSentinel, UnifiedProfileEntry.GlobalName);
#pragma warning restore MSTEST0032
    }

    // -----------------------------------------------------------------------
    // IsGlobal
    // -----------------------------------------------------------------------

    [Fact]
    public void IsGlobal_FalseForCliOnlyProfile()
    {
        UnifiedProfileEntry entry = new("work", HasCli: true, HasDesktop: false);
        Assert.False(entry.IsGlobal);
    }

    [Fact]
    public void IsGlobal_FalseForDesktopOnlyProfile()
    {
        UnifiedProfileEntry entry = new("work", HasCli: false, HasDesktop: true);
        Assert.False(entry.IsGlobal);
    }

    [Fact]
    public void IsGlobal_FalseForSharedProfile()
    {
        UnifiedProfileEntry entry = new("work", HasCli: true, HasDesktop: true);
        Assert.False(entry.IsGlobal);
    }

    // -----------------------------------------------------------------------
    // ShowCliChiclet
    // -----------------------------------------------------------------------

    [Fact]
    public void ShowCliChiclet_TrueWhenHasCliAndNotGlobal()
    {
        UnifiedProfileEntry entry = new("work", HasCli: true, HasDesktop: false);
        Assert.True(entry.ShowCliChiclet);
    }

    [Fact]
    public void ShowCliChiclet_TrueForSharedNonGlobalProfile()
    {
        UnifiedProfileEntry entry = new("work", HasCli: true, HasDesktop: true);
        Assert.True(entry.ShowCliChiclet);
    }

    [Fact]
    public void ShowCliChiclet_FalseWhenHasCliButIsGlobal()
    {
        // Global entry never shows a chiclet even though it represents "both products".
        Assert.False(UnifiedProfileEntry.Global.ShowCliChiclet);
    }

    [Fact]
    public void ShowCliChiclet_FalseWhenDesktopOnly()
    {
        UnifiedProfileEntry entry = new("home", HasCli: false, HasDesktop: true);
        Assert.False(entry.ShowCliChiclet);
    }

    // -----------------------------------------------------------------------
    // ShowDesktopChiclet
    // -----------------------------------------------------------------------

    [Fact]
    public void ShowDesktopChiclet_TrueWhenHasDesktopAndNotGlobal()
    {
        UnifiedProfileEntry entry = new("home", HasCli: false, HasDesktop: true);
        Assert.True(entry.ShowDesktopChiclet);
    }

    [Fact]
    public void ShowDesktopChiclet_TrueForSharedNonGlobalProfile()
    {
        UnifiedProfileEntry entry = new("work", HasCli: true, HasDesktop: true);
        Assert.True(entry.ShowDesktopChiclet);
    }

    [Fact]
    public void ShowDesktopChiclet_FalseWhenHasDesktopButIsGlobal()
    {
        Assert.False(UnifiedProfileEntry.Global.ShowDesktopChiclet);
    }

    [Fact]
    public void ShowDesktopChiclet_FalseWhenCliOnly()
    {
        UnifiedProfileEntry entry = new("work", HasCli: true, HasDesktop: false);
        Assert.False(entry.ShowDesktopChiclet);
    }

    // -----------------------------------------------------------------------
    // ToString
    // -----------------------------------------------------------------------

    [Fact]
    public void ToString_ReturnsName()
    {
        UnifiedProfileEntry entry = new("personal", HasCli: true, HasDesktop: false);
        Assert.Equal("personal", entry.ToString());
    }

    [Fact]
    public void ToString_GlobalReturnsGlobalName()
    {
        Assert.Equal(UnifiedProfileEntry.GlobalName, UnifiedProfileEntry.Global.ToString());
    }
}