namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.ViewModels;

[TestClass]
public class PathPropertyEditorViewModelTests
{
    private static FakeEditorSchema Schema()
    {
        return new FakeEditorSchema("settings.configPath", EditorValueType.Path);
    }

    private static FakeEditorValue Empty()
    {
        return new FakeEditorValue("settings.configPath");
    }

    private static FakeEditorValue WithUser(string v)
    {
        return new FakeEditorValue("settings.configPath").With(FakeEditorScope.User, v);
    }

    // ── Construction ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Initial_Value_IsNull_NotModified()
    {
        PathPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        Assert.IsNull(vm.Value);
        Assert.IsFalse(vm.IsModified);
        Assert.IsFalse(vm.IsValueSet);
    }

    // ── LoadFromValue ─────────────────────────────────────────────────────────

    [TestMethod]
    public void LoadFromValue_SetsPath()
    {
        PathPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(WithUser("/home/user/.config"), FakeEditorScope.User);

        Assert.AreEqual("/home/user/.config", vm.Value);
        Assert.IsTrue(vm.IsModified);
        Assert.IsTrue(vm.IsValueSet);
    }

    [TestMethod]
    public void LoadFromValue_Empty_ValueStaysNull()
    {
        PathPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(Empty(), FakeEditorScope.User);

        Assert.IsNull(vm.Value);
        Assert.IsFalse(vm.IsModified);
    }

    // ── ToValue ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void ToValue_ReturnsNull_WhenNotSet()
    {
        PathPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        Assert.IsNull(vm.ToValue());
    }

    [TestMethod]
    public void ToValue_ReturnsPath_WhenSet()
    {
        PathPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.Value = "/usr/local/bin";
        Assert.AreEqual("/usr/local/bin", vm.ToValue());
    }

    // ── Browse callback ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task BrowseCommand_SetsValue_WhenDialogReturnsPath()
    {
        PathPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User,
            browseDialog: () => Task.FromResult<string?>("/picked/path"));

        await vm.BrowseCommand.ExecuteAsync(null);

        Assert.AreEqual("/picked/path", vm.Value);
    }

    [TestMethod]
    public async Task BrowseCommand_DoesNotSetValue_WhenDialogReturnsCancelled()
    {
        PathPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User,
            browseDialog: () => Task.FromResult<string?>(null));
        vm.Value = "/original";

        await vm.BrowseCommand.ExecuteAsync(null);

        Assert.AreEqual("/original", vm.Value);
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ResetToInherited_ClearsValue()
    {
        PathPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.Value = "/some/path";

        vm.ResetToInheritedCommand.Execute(null);

        Assert.IsNull(vm.Value);
        Assert.IsFalse(vm.IsModified);
    }

    // ── OtherScopesWithData + InheritedFromScope ──────────────────

    [TestMethod]
    public void LoadFromValue_OtherScopesWithData_PopulatedFromDefiningScopes()
    {
        FakeEditorValue value = new FakeEditorValue("settings.configPath")
                                .With(FakeEditorScope.User, "/home/user/config")
                                .With(FakeEditorScope.Project, "./project-config");
        PathPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.AreEqual(1, vm.OtherScopesWithData.Count);
        Assert.AreEqual("project", vm.OtherScopesWithData[0].Id);
    }

    [TestMethod]
    public void LoadFromValue_InheritedFromScope_PopulatedWhenEditingScopeEmpty()
    {
        FakeEditorValue value = new FakeEditorValue("settings.configPath").With(FakeEditorScope.User, "/home/user/config");
        PathPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.Local);
        vm.LoadFromValue(value, FakeEditorScope.Local);

        Assert.IsNull(vm.Value);
        Assert.AreEqual("user", vm.InheritedFromScope?.Id);
        Assert.AreEqual("/home/user/config", vm.InheritedDisplay);
        Assert.IsTrue(vm.HasInheritedFromOtherScope);
    }
}