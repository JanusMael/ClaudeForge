using Bennewitz.Ninja.ClaudeForge.Avalonia.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Avalonia.Tests.Permissions;

/// <summary>
/// The dry-run tester resolves a candidate through the SDK resolver in both
/// single-scope and merged modes, reflecting the source's (possibly unsaved)
/// rules and attributing the decision.
/// </summary>
public sealed class PermissionTesterViewModelTests
{
    private static readonly PermissionMatchContext Ctx =
        new("/proj", "/proj", "/home/alice");

    private static PermissionTesterViewModel New(FakeSource source) => new(source, Ctx);

    [Fact]
    public void SingleScope_DenyBeatsAllow()
    {
        var src = new FakeSource
        {
            Editing = FakeSource.Scope(ConfigScope.User,
                allow: ["Bash(git *)"], deny: ["Bash(git push *)"]),
        };
        PermissionTesterViewModel vm = New(src);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "git push origin main";

        Assert.True(vm.HasResult);
        Assert.Equal(PermissionOutcome.Deny, vm.Outcome);
    }

    [Fact]
    public void SingleScope_AllowWhenMatched()
    {
        var src = new FakeSource
        {
            Editing = FakeSource.Scope(ConfigScope.User, allow: ["Bash(npm *)"]),
        };
        PermissionTesterViewModel vm = New(src);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "npm test";

        Assert.Equal(PermissionOutcome.Allow, vm.Outcome);
    }

    [Fact]
    public void Default_WhenNoRuleMatches()
    {
        var src = new FakeSource { DefaultMode = PermissionDefaultMode.AcceptEdits };
        PermissionTesterViewModel vm = New(src);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "obscure-cmd";

        Assert.Equal(PermissionOutcome.Default, vm.Outcome);
        OrdinalAssert.Contains("AcceptEdits", vm.Explanation);
    }

    [Fact]
    public void ColonStarRule_MatchesBareCommand()
    {
        // The reported case: Bash(git push:*) should match candidate "git push".
        var src = new FakeSource
        {
            Editing = FakeSource.Scope(ConfigScope.User, allow: ["Bash(git push:*)"]),
        };
        PermissionTesterViewModel vm = New(src);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "git push";
        Assert.Equal(PermissionOutcome.Allow, vm.Outcome);
    }

    [Fact]
    public void MergedView_DenyInManagedScope_Wins()
    {
        var src = new FakeSource();
        src.All.Add(FakeSource.Scope(ConfigScope.Managed, deny: ["Bash(git push *)"]));
        src.All.Add(FakeSource.Scope(ConfigScope.User, allow: ["Bash(git push *)"]));

        PermissionTesterViewModel vm = New(src);
        vm.UseMergedView = true;
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "git push origin main";

        Assert.Equal(PermissionOutcome.Deny, vm.Outcome);
        OrdinalAssert.Contains("Managed", vm.Explanation);
        // The dedicated scope callout names the owning scope in merged view.
        Assert.True(vm.HasMatchedScope);
        Assert.Equal("Managed", vm.MatchedScopeLabel);
    }

    [Fact]
    public void SingleScopeView_ShowsEditingScope()
    {
        var src = new FakeSource
        {
            EditingScope = ConfigScope.User,
            Editing = FakeSource.Scope(ConfigScope.User, allow: ["Bash(npm *)"]),
        };
        PermissionTesterViewModel vm = New(src);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "npm test";
        Assert.Equal(PermissionOutcome.Allow, vm.Outcome);
        // The scope is always shown; single-scope falls back to the editing scope.
        Assert.True(vm.HasMatchedScope);
        Assert.Equal("User", vm.MatchedScopeLabel);
    }

    [Fact]
    public void Default_StillShowsEditingScope()
    {
        var src = new FakeSource { EditingScope = ConfigScope.Project };
        PermissionTesterViewModel vm = New(src);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "obscure-cmd";
        Assert.Equal(PermissionOutcome.Default, vm.Outcome);
        Assert.Equal("Project", vm.MatchedScopeLabel);
    }

    [Fact]
    public void ReflectsCurrentSourceState()
    {
        // The source is the single source of truth; updating it + recomputing
        // changes the verdict — modeling unsaved-edit reflection.
        var src = new FakeSource
        {
            Editing = FakeSource.Scope(ConfigScope.User),
        };
        PermissionTesterViewModel vm = New(src);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "npm test";
        Assert.Equal(PermissionOutcome.Default, vm.Outcome);

        src.Editing = FakeSource.Scope(ConfigScope.User, allow: ["Bash(npm *)"]);
        vm.Recompute();
        Assert.Equal(PermissionOutcome.Allow, vm.Outcome);
    }

    [Fact]
    public void ReadOnlyNote_ShownForBuiltInReadOnlyCommand()
    {
        var src = new FakeSource();
        PermissionTesterViewModel vm = New(src);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "ls -la";
        Assert.NotEqual(string.Empty, vm.ReadOnlyNote);

        vm.CommandText = "rm -rf /";
        Assert.Equal(string.Empty, vm.ReadOnlyNote);
    }

    [Fact]
    public void EmptyInput_ResolvesBareToolProbe()
    {
        // Empty command = "what happens when Bash is used at all?" — a bare-tool
        // probe that resolves immediately (Default with no rules), rather than a
        // blank pane. A bare Bash allow rule would flip it to Allow.
        var src = new FakeSource();
        PermissionTesterViewModel vm = New(src);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "";
        Assert.True(vm.HasResult);
        Assert.Equal(PermissionOutcome.Default, vm.Outcome);

        src.Editing = FakeSource.Scope(ConfigScope.User, allow: ["Bash"]);
        vm.Recompute();
        Assert.Equal(PermissionOutcome.Allow, vm.Outcome);
    }

    [Fact]
    public void EmptyMcpServer_NoResult()
    {
        // MCP is the one tool with no meaning without a server, so it stays blank.
        PermissionTesterViewModel vm = New(new FakeSource());
        vm.SelectedTool = PermissionBuilderTool.Mcp;
        vm.McpServer = "";
        Assert.False(vm.HasResult);
    }

    [Fact]
    public void Compound_DenyFromSubcommand()
    {
        var src = new FakeSource
        {
            Editing = FakeSource.Scope(ConfigScope.User,
                allow: ["Bash(npm *)"], deny: ["Bash(rm *)"]),
        };
        PermissionTesterViewModel vm = New(src);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "npm test && rm -rf /";

        Assert.Equal(PermissionOutcome.Deny, vm.Outcome);
    }
}
