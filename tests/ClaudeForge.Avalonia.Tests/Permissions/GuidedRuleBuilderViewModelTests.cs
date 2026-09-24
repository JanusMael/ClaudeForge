using Bennewitz.Ninja.ClaudeForge.Avalonia.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Avalonia.Tests.Permissions;

/// <summary>
/// The guided builder emits valid syntax per tool, glosses it, gates Add on
/// validity, and routes built rules to the sink.
/// </summary>
public sealed class GuidedRuleBuilderViewModelTests
{
    private static GuidedRuleBuilderViewModel New(
        out FakeSink sink, FakePathPicker? picker = null, string[]? servers = null)
    {
        sink = new FakeSink();
        return new GuidedRuleBuilderViewModel(sink, picker, servers);
    }

    [Fact]
    public void Bash_Prefix_EmitsColonStar()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.MatchPrefix = true;
        vm.CommandText = "git commit";
        // Canonical colon form (Bash(npm run test:*) per Claude's docs).
        Assert.Equal("Bash(git commit:*)", vm.PreviewRule);
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void Bash_CollapsesInternalWhitespace()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.MatchPrefix = true;
        vm.CommandText = "npm run  build"; // accidental double space
        // Collapsed to a single space so the rule matches the real command.
        Assert.Equal("Bash(npm run build:*)", vm.PreviewRule);
    }

    [Fact]
    public void DefaultTool_IsFirstInOrder_Read()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        // Dropdown is file→shell→web→mcp→agent, so the default selection is the
        // first entry (Read), not Bash.
        Assert.Equal(PermissionBuilderTool.Read, vm.SelectedTool);
        Assert.Equal(PermissionBuilderTool.Read, vm.AvailableTools[0]);
    }

    [Fact]
    public void PathHints_DefaultWildcards_ToggleAnchors_ResetOnToolChange()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Read;
        Assert.True(vm.ShowHintToggle);
        Assert.True(vm.ShowPathGlobHints, "Path tools default to the Wildcards group.");
        Assert.False(vm.ShowPathAnchorHints);

        vm.SelectAnchorHintsCommand.Execute(null);
        Assert.False(vm.ShowPathGlobHints);
        Assert.True(vm.ShowPathAnchorHints, "Anchors segment switches to the Anchors group.");
        // Both groups are laid out (opacity-toggled), so the box keeps a stable size.
        Assert.Equal(0.0, vm.GlobGroupOpacity);
        Assert.Equal(1.0, vm.AnchorGroupOpacity);

        vm.SelectWildcardHintsCommand.Execute(null);
        Assert.True(vm.ShowPathGlobHints, "Wildcards segment switches back.");

        // Changing tool resets the hint box to the default Wildcards group.
        vm.SelectAnchorHintsCommand.Execute(null);
        vm.SelectedTool = PermissionBuilderTool.Edit;
        Assert.True(vm.ShowPathGlobHints);
        Assert.False(vm.ShowPathAnchorHints);
    }

    [Fact]
    public void ShellTool_ShowsHintBox_WithoutToggle()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        Assert.True(vm.ShowAnyHint);
        Assert.True(vm.ShowShellWildcardHint);
        Assert.False(vm.ShowHintToggle, "Shell has a single hint group — no toggle.");
    }

    [Fact]
    public void Agent_HidesHintBox()
    {
        // Agent is the only tool with no special-token hint.
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Agent;
        Assert.False(vm.ShowAnyHint);
    }

    [Fact]
    public void WebFetchAndMcp_ShowSingleHint_NoToggle()
    {
        GuidedRuleBuilderViewModel vm = New(out _);

        vm.SelectedTool = PermissionBuilderTool.WebFetch;
        Assert.True(vm.ShowAnyHint);
        Assert.True(vm.ShowWebHint);
        Assert.False(vm.ShowHintToggle, "WebFetch has a single hint — no toggle.");

        vm.SelectedTool = PermissionBuilderTool.Mcp;
        Assert.True(vm.ShowAnyHint);
        Assert.True(vm.ShowMcpHint);
        Assert.False(vm.ShowHintToggle, "MCP has a single hint — no toggle.");
    }

    [Fact]
    public void Bash_Exact_EmitsNoWildcard()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.MatchPrefix = false;
        vm.CommandText = "npm run build";
        Assert.Equal("Bash(npm run build)", vm.PreviewRule);
    }

    [Fact]
    public void Bash_EmptyCommand_EmitsBareTool()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "";
        Assert.Equal("Bash", vm.PreviewRule);
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void Read_Recursive_AppendsDoubleStar()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Read;
        vm.PathText = "src";
        vm.Recursive = true;
        Assert.Equal("Read(src/**)", vm.PreviewRule);
    }

    [Fact]
    public void Read_Exact_KeepsPath()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Read;
        vm.PathText = "./.env";
        vm.Recursive = false;
        Assert.Equal("Read(./.env)", vm.PreviewRule);
    }

    [Fact]
    public void WebFetch_EmitsDomainSpecifier()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.WebFetch;
        vm.Domain = "example.com";
        Assert.Equal("WebFetch(domain:example.com)", vm.PreviewRule);
    }

    [Fact]
    public void Mcp_AllTools_EmitsServerOnly()
    {
        GuidedRuleBuilderViewModel vm = New(out _, servers: ["github"]);
        vm.SelectedTool = PermissionBuilderTool.Mcp;
        vm.SelectedMcpServer = "github";
        vm.McpAllTools = true;
        Assert.Equal("mcp__github", vm.PreviewRule);
    }

    [Fact]
    public void Mcp_SpecificTool_EmitsServerAndTool()
    {
        GuidedRuleBuilderViewModel vm = New(out _, servers: ["github"]);
        vm.SelectedTool = PermissionBuilderTool.Mcp;
        vm.SelectedMcpServer = "github";
        vm.McpAllTools = false;
        vm.McpTool = "create_issue";
        Assert.Equal("mcp__github__create_issue", vm.PreviewRule);
    }

    [Fact]
    public void Mcp_NoServer_IsInvalid_AndAddDisabled()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Mcp;
        vm.SelectedMcpServer = null;
        Assert.Equal(string.Empty, vm.PreviewRule);
        Assert.False(vm.IsValid);
        Assert.False(vm.AddAllowCommand.CanExecute(null));
    }

    [Fact]
    public void Agent_EmitsNamedRule()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Agent;
        vm.AgentName = "Explore";
        Assert.Equal("Agent(Explore)", vm.PreviewRule);
    }

    [Fact]
    public void Gloss_ReflectsPrefixVsExact()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "git commit";
        vm.MatchPrefix = true;
        Assert.Contains("git commit", vm.PlainEnglishGloss);
        Assert.NotEqual(string.Empty, vm.PlainEnglishGloss);
    }

    [Fact]
    public void AddAllow_RoutesRuleToSink()
    {
        GuidedRuleBuilderViewModel vm = New(out FakeSink sink);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "npm test";
        vm.MatchPrefix = false;
        vm.AddAllowCommand.Execute(null);
        Assert.Single(sink.Allow);
        Assert.Equal("Bash(npm test)", sink.Allow[0].Value);
        Assert.Empty(sink.Deny);
    }

    [Fact]
    public void AddAllow_SetsTransientConfirmation()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.CommandText = "npm test";
        vm.MatchPrefix = false;
        Assert.Equal(string.Empty, vm.LastAddMessage);
        vm.AddAllowCommand.Execute(null);
        // Confirmation mentions the rule that was added (auto-clears later).
        Assert.Contains("Bash(npm test)", vm.LastAddMessage);
    }

    [Fact]
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
        Assert.NotEqual(string.Empty, vm.CollisionWarning);
        Assert.Contains("Bash(npm test)", vm.CollisionWarning);
    }

    [Fact]
    public async Task BrowseFile_SetsPathFromPicker()
    {
        var picker = new FakePathPicker(file: "/picked/file.txt");
        GuidedRuleBuilderViewModel vm = New(out _, picker);
        vm.SelectedTool = PermissionBuilderTool.Read;
        await vm.BrowseFileCommand.ExecuteAsync(null);
        Assert.Equal("/picked/file.txt", vm.PathText);
    }

    [Fact]
    public void ShowFlags_TrackSelectedTool()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        Assert.True(vm.ShowCommandInput);
        Assert.False(vm.ShowPathInput);

        vm.SelectedTool = PermissionBuilderTool.Read;
        Assert.True(vm.ShowPathInput);
        Assert.False(vm.ShowCommandInput);

        vm.SelectedTool = PermissionBuilderTool.WebFetch;
        Assert.True(vm.ShowDomainInput);
    }

    // ── AF8: whitespace inside quotes is preserved (only unquoted runs collapse) ─

    [Fact]
    public void Bash_PreservesWhitespaceInsideQuotes()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.MatchPrefix = false;
        // The double space is INSIDE quotes — part of the argument — so it must be
        // preserved, while the accidental double space between tokens collapses.
        vm.CommandText = "grep  \"foo   bar\"  .";
        Assert.Equal("Bash(grep \"foo   bar\" .)", vm.PreviewRule);
    }

    [Fact]
    public void Bash_PreservesWhitespaceInsideSingleQuotes()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Bash;
        vm.MatchPrefix = false;
        vm.CommandText = "echo 'a   b'";
        Assert.Equal("Bash(echo 'a   b')", vm.PreviewRule);
    }

    [Fact]
    public void CollapseUnquotedWhitespace_Cases()
    {
        Assert.Equal("a b c", GuidedRuleBuilderViewModel.CollapseUnquotedWhitespace("a   b\t\tc"));
        Assert.Equal("a \"b   c\" d", GuidedRuleBuilderViewModel.CollapseUnquotedWhitespace("a   \"b   c\"   d"));
        Assert.Equal("", GuidedRuleBuilderViewModel.CollapseUnquotedWhitespace("   "));
    }

    // ── AF8: a path of only wildcards is flagged, not silently over-broad ───────

    [Fact]
    public void Read_BareWildcardPath_IsInvalidWithSpecificMessage()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Read;

        foreach (string bare in new[] { "*", "**", "*/**", "/**" })
        {
            vm.PathText = bare;
            vm.Recursive = false;
            Assert.False(vm.IsValid, $"'{bare}' should be flagged as a bare-wildcard path");
            Assert.Equal(
                Bennewitz.Ninja.ClaudeForge.Avalonia.Localization.Strings.PermBuilderBareWildcardPath,
                vm.ValidationMessage);
        }
    }

    [Fact]
    public void Read_RecursiveTurningPathBare_IsFlagged()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Read;
        vm.PathText = "*";
        vm.Recursive = true; // → "*/**", still all-wildcards
        Assert.False(vm.IsValid);
    }

    [Fact]
    public void Read_RealPattern_StaysValid()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Read;
        vm.PathText = "*.env"; // has a literal segment
        Assert.True(vm.IsValid);
        Assert.Equal("Read(*.env)", vm.PreviewRule);
    }

    [Fact]
    public void IsBareWildcardPath_Cases()
    {
        Assert.True(GuidedRuleBuilderViewModel.IsBareWildcardPath("*"));
        Assert.True(GuidedRuleBuilderViewModel.IsBareWildcardPath("**"));
        Assert.True(GuidedRuleBuilderViewModel.IsBareWildcardPath("*/**"));
        Assert.False(GuidedRuleBuilderViewModel.IsBareWildcardPath("*.ts"));
        Assert.False(GuidedRuleBuilderViewModel.IsBareWildcardPath("src/**"));
        Assert.False(GuidedRuleBuilderViewModel.IsBareWildcardPath(""));
    }

    // ── AF8: gloss agrees with the previewed rule (not just the toggle) ─────────

    [Fact]
    public void Gloss_PathEndingInDoubleStar_ReadsRecursive_EvenWithToggleOff()
    {
        GuidedRuleBuilderViewModel vm = New(out _);
        vm.SelectedTool = PermissionBuilderTool.Read;
        vm.Recursive = false;
        vm.PathText = "src/**"; // recursive by the pattern, not the toggle
        Assert.Equal("Read(src/**)", vm.PreviewRule);
        // The gloss describes recursion and names the base directory, not "exact".
        Assert.Contains("src", vm.PlainEnglishGloss);
        Assert.Equal(
            string.Format(
                Bennewitz.Ninja.ClaudeForge.Avalonia.Localization.Strings.PermBuilderGlossPathRecursive,
                "src"),
            vm.PlainEnglishGloss);
    }
}
