using Bennewitz.Ninja.ClaudeForge.Avalonia.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Avalonia.Tests.Permissions;

/// <summary>
/// The guided builder emits valid syntax per tool, glosses it, gates Add on
/// validity, and routes built rules to the sink.
/// </summary>
[TestClass]
public sealed class GuidedRuleBuilderViewModelTests
{
    private static GuidedRuleBuilderViewModel New(
        out FakeSink sink, FakePathPicker? picker = null, string[]? servers = null)
    {
        sink = new FakeSink();
        return new GuidedRuleBuilderViewModel(sink, picker, servers);
    }

    [TestMethod]
    public void Bash_Prefix_EmitsColonStar()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.MatchPrefix = true;
        vm.CommandText = "git commit";
        // Canonical colon form (Bash(npm run test:*) per Claude's docs).
        Assert.AreEqual("Bash(git commit:*)", vm.PreviewRule);
        Assert.IsTrue(vm.IsValid);
    }

    [TestMethod]
    public void Bash_CollapsesInternalWhitespace()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.MatchPrefix = true;
        vm.CommandText = "npm run  build"; // accidental double space
        // Collapsed to a single space so the rule matches the real command.
        Assert.AreEqual("Bash(npm run build:*)", vm.PreviewRule);
    }

    [TestMethod]
    public void DefaultTool_IsFirstInOrder_Read()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        // Dropdown is file→shell→web→mcp→agent, so the default selection is the
        // first entry (Read), not Bash.
        Assert.AreEqual(PermissionBuilderTool.Read, vm.SelectedTool);
        Assert.AreEqual(PermissionBuilderTool.Read, vm.AvailableTools[0]);
    }

    [TestMethod]
    public void PathHints_DefaultWildcards_ToggleAnchors_ResetOnToolChange()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Read;
        Assert.IsTrue(vm.ShowHintToggle);
        Assert.IsTrue(vm.ShowPathGlobHints, "Path tools default to the Wildcards group.");
        Assert.IsFalse(vm.ShowPathAnchorHints);

        vm.SelectAnchorHintsCommand.Execute(null);
        Assert.IsFalse(vm.ShowPathGlobHints);
        Assert.IsTrue(vm.ShowPathAnchorHints, "Anchors segment switches to the Anchors group.");
        // Both groups are laid out (opacity-toggled), so the box keeps a stable size.
        Assert.AreEqual(0.0, vm.GlobGroupOpacity);
        Assert.AreEqual(1.0, vm.AnchorGroupOpacity);

        vm.SelectWildcardHintsCommand.Execute(null);
        Assert.IsTrue(vm.ShowPathGlobHints, "Wildcards segment switches back.");

        // Changing tool resets the hint box to the default Wildcards group.
        vm.SelectAnchorHintsCommand.Execute(null);
        vm.SelectedTool = PermissionBuilderTool.Edit;
        Assert.IsTrue(vm.ShowPathGlobHints);
        Assert.IsFalse(vm.ShowPathAnchorHints);
    }

    [TestMethod]
    public void ShellTool_ShowsHintBox_WithoutToggle()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        Assert.IsTrue(vm.ShowAnyHint);
        Assert.IsTrue(vm.ShowShellWildcardHint);
        Assert.IsFalse(vm.ShowHintToggle, "Shell has a single hint group — no toggle.");
    }

    [TestMethod]
    public void Agent_HidesHintBox()
    {
        // Agent is the only tool with no special-token hint.
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Agent;
        Assert.IsFalse(vm.ShowAnyHint);
    }

    [TestMethod]
    public void WebFetchAndMcp_ShowSingleHint_NoToggle()
    {
        GuidedRuleBuilderViewModel vm = New(out _);

        vm.SelectedTool = PermissionBuilderTool.WebFetch;
        Assert.IsTrue(vm.ShowAnyHint);
        Assert.IsTrue(vm.ShowWebHint);
        Assert.IsFalse(vm.ShowHintToggle, "WebFetch has a single hint — no toggle.");

        vm.SelectedTool = PermissionBuilderTool.Mcp;
        Assert.IsTrue(vm.ShowAnyHint);
        Assert.IsTrue(vm.ShowMcpHint);
        Assert.IsFalse(vm.ShowHintToggle, "MCP has a single hint — no toggle.");
    }

    [TestMethod]
    public void Bash_Exact_EmitsNoWildcard()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.MatchPrefix = false;
        vm.CommandText = "npm run build";
        Assert.AreEqual("Bash(npm run build)", vm.PreviewRule);
    }

    [TestMethod]
    public void Bash_EmptyCommand_EmitsBareTool()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "";
        Assert.AreEqual("Bash", vm.PreviewRule);
        Assert.IsTrue(vm.IsValid);
    }

    [TestMethod]
    public void Read_Recursive_AppendsDoubleStar()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Read;
        vm.PathText = "src";
        vm.Recursive = true;
        Assert.AreEqual("Read(src/**)", vm.PreviewRule);
    }

    [TestMethod]
    public void Read_Exact_KeepsPath()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Read;
        vm.PathText = "./.env";
        vm.Recursive = false;
        Assert.AreEqual("Read(./.env)", vm.PreviewRule);
    }

    [TestMethod]
    public void WebFetch_EmitsDomainSpecifier()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.WebFetch;
        vm.Domain = "example.com";
        Assert.AreEqual("WebFetch(domain:example.com)", vm.PreviewRule);
    }

    [TestMethod]
    public void Mcp_AllTools_EmitsServerOnly()
    {
        GuidedRuleBuilderViewModel vm = New(out _, servers: ["github"]);
        vm.SelectedTool = PermissionBuilderTool.Mcp;
        vm.SelectedMcpServer = "github";
        vm.McpAllTools = true;
        Assert.AreEqual("mcp__github", vm.PreviewRule);
    }

    [TestMethod]
    public void Mcp_SpecificTool_EmitsServerAndTool()
    {
        GuidedRuleBuilderViewModel vm = New(out _, servers: ["github"]);
        vm.SelectedTool = PermissionBuilderTool.Mcp;
        vm.SelectedMcpServer = "github";
        vm.McpAllTools = false;
        vm.McpTool = "create_issue";
        Assert.AreEqual("mcp__github__create_issue", vm.PreviewRule);
    }

    [TestMethod]
    public void Mcp_NoServer_IsInvalid_AndAddDisabled()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Mcp;
        vm.SelectedMcpServer = null;
        Assert.AreEqual(string.Empty, vm.PreviewRule);
        Assert.IsFalse(vm.IsValid);
        Assert.IsFalse(vm.AddAllowCommand.CanExecute(null));
    }

    [TestMethod]
    public void Agent_EmitsNamedRule()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Agent;
        vm.AgentName = "Explore";
        Assert.AreEqual("Agent(Explore)", vm.PreviewRule);
    }

    [TestMethod]
    public void Gloss_ReflectsPrefixVsExact()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "git commit";
        vm.MatchPrefix = true;
        StringAssert.Contains(vm.PlainEnglishGloss, "git commit");
        Assert.AreNotEqual(string.Empty, vm.PlainEnglishGloss);
    }

    [TestMethod]
    public void AddAllow_RoutesRuleToSink()
    {
        GuidedRuleBuilderViewModel vm = New(out FakeSink sink);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "npm test";
        vm.MatchPrefix = false;
        vm.AddAllowCommand.Execute(null);
        Assert.AreEqual(1, sink.Allow.Count);
        Assert.AreEqual("Bash(npm test)", sink.Allow[0].Value);
        Assert.AreEqual(0, sink.Deny.Count);
    }

    [TestMethod]
    public void AddAllow_SetsTransientConfirmation()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "npm test";
        vm.MatchPrefix = false;
        Assert.AreEqual(string.Empty, vm.LastAddMessage);
        vm.AddAllowCommand.Execute(null);
        // Confirmation mentions the rule that was added (auto-clears later).
        StringAssert.Contains(vm.LastAddMessage, "Bash(npm test)");
    }

    [TestMethod]
    public void AddAllow_SurfacesSinkCollision()
    {
        GuidedRuleBuilderViewModel vm = New(out FakeSink sink);
        sink.NextCollision = new PermissionCollision(
            PermissionCollisionKind.Conflict,
            PermissionRule.Parse("Bash(npm test)"),
            PermissionBucket.Deny);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "npm test";
        vm.MatchPrefix = false;
        vm.AddAllowCommand.Execute(null);
        Assert.AreNotEqual(string.Empty, vm.CollisionWarning);
        StringAssert.Contains(vm.CollisionWarning, "Bash(npm test)");
    }

    [TestMethod]
    public async Task BrowseFile_SetsPathFromPicker()
    {
        var picker = new FakePathPicker(file: "/picked/file.txt");
        GuidedRuleBuilderViewModel vm = New(out _, picker);
        vm.SelectedTool = PermissionBuilderTool.Read;
        await vm.BrowseFileCommand.ExecuteAsync(null);
        Assert.AreEqual("/picked/file.txt", vm.PathText);
    }

    [TestMethod]
    public void ShowFlags_TrackSelectedTool()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        Assert.IsTrue(vm.ShowCommandInput);
        Assert.IsFalse(vm.ShowPathInput);

        vm.SelectedTool = PermissionBuilderTool.Read;
        Assert.IsTrue(vm.ShowPathInput);
        Assert.IsFalse(vm.ShowCommandInput);

        vm.SelectedTool = PermissionBuilderTool.WebFetch;
        Assert.IsTrue(vm.ShowDomainInput);
    }
}
