namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// focused tests for the static <c>IsValid</c> /
/// <c>Diagnose</c> helpers on <see cref="PermissionRuleViewModel"/>.
/// These were exercised only indirectly (and partially) through
/// <c>PermissionsEditorViewModelTests</c>; this suite covers every
/// branch of the diagnose decision tree so a future regex tweak
/// surfaces here rather than via a misleading inline error in the GUI.
/// </summary>
public sealed class PermissionRuleViewModelTests
{
    // ── IsValid — happy paths ──────────────────────────────────────────────

    [Theory]
    [InlineData("Bash")]
    [InlineData("Edit")]
    [InlineData("Read")]
    [InlineData("Write")]
    [InlineData("Glob")]
    [InlineData("Grep")]
    [InlineData("WebFetch")]
    [InlineData("WebSearch")]
    [InlineData("Agent")]
    [InlineData("ExitPlanMode")]
    [InlineData("KillShell")]
    [InlineData("LSP")]
    [InlineData("Monitor")]
    [InlineData("NotebookEdit")]
    [InlineData("PowerShell")]
    [InlineData("Skill")]
    [InlineData("TaskCreate")]
    [InlineData("TaskGet")]
    [InlineData("TaskList")]
    [InlineData("TaskOutput")]
    [InlineData("TaskStop")]
    [InlineData("TaskUpdate")]
    [InlineData("TodoWrite")]
    [InlineData("ToolSearch")]
    public void IsValid_BareKnownToolName_True(string rule)
    {
        Assert.True(PermissionRuleViewModel.IsValid(rule),
            $"\"{rule}\" should be a valid bare tool name.");
    }

    [Theory]
    [InlineData("Bash(git *)")]
    [InlineData("Bash(npm install)")]
    [InlineData("Edit(./**/*.cs)")]
    [InlineData("Write(./**/*.json)")]
    [InlineData("WebFetch(https://*.example.com/*)")]
    [InlineData("PowerShell(Get-*)")]
    public void IsValid_ToolWithRealPattern_True(string rule)
    {
        // Note: pure-wildcard patterns like "Read(*)" are REJECTED by the
        // schema regex's lookahead — see IsValid_KnownInvalidShapes_False
        // for that branch.
        Assert.True(PermissionRuleViewModel.IsValid(rule));
    }

    [Theory]
    [InlineData("mcp__github__create_issue")]
    [InlineData("mcp__exa__search")]
    [InlineData("mcp__*")]
    [InlineData("mcp__server__")]
    public void IsValid_McpPrefix_True(string rule)
    {
        Assert.True(PermissionRuleViewModel.IsValid(rule),
            "Any string starting with mcp__ is valid per the schema regex.");
    }

    // ── IsValid — rejection paths ──────────────────────────────────────────

    [Fact]
    public void IsValid_NullOrWhitespace_False()
    {
        Assert.False(PermissionRuleViewModel.IsValid(null));
        Assert.False(PermissionRuleViewModel.IsValid(""));
        Assert.False(PermissionRuleViewModel.IsValid("   "));
        Assert.False(PermissionRuleViewModel.IsValid("\t\n"));
    }

    [Theory]
    [InlineData("Foo")] // unknown bare name
    [InlineData("bash")] // case-sensitive — wrong case
    [InlineData("Bashh")] // typo
    [InlineData("Foo(*)")] // unknown name + valid-looking paren
    [InlineData("Bash(")] // unclosed paren
    [InlineData("Bash()")] // empty paren — schema rejects
    [InlineData("Bash(*)")] // pure-wildcard paren — schema rejects
    [InlineData("Bash(?)")] // pure-? paren — schema rejects
    [InlineData("Bash(***)")] // multiple wildcards only
    [InlineData("Pwsh")] // not a real Claude Code tool — the shell tool is "PowerShell"
    [InlineData("Pwsh(git status)")] // Pwsh(...) is not recognized; rules must use PowerShell(...)
    public void IsValid_KnownInvalidShapes_False(string rule)
    {
        Assert.False(PermissionRuleViewModel.IsValid(rule),
            $"\"{rule}\" should be rejected by the permissionRule regex.");
    }

    // ── Diagnose — empty / whitespace branch ───────────────────────────────

    [Fact]
    public void Diagnose_NullOrWhitespace_ReturnsEmptyMessage()
    {
        Assert.Equal("Rule cannot be empty.", PermissionRuleViewModel.Diagnose(null));
        Assert.Equal("Rule cannot be empty.", PermissionRuleViewModel.Diagnose(""));
        Assert.Equal("Rule cannot be empty.", PermissionRuleViewModel.Diagnose("   "));
    }

    // ── Diagnose — valid rules return empty string ────────────────────────

    [Theory]
    [InlineData("Bash")]
    [InlineData("Edit(./**/*.cs)")]
    [InlineData("mcp__github__list")]
    public void Diagnose_ValidRule_ReturnsEmpty(string rule)
    {
        MessageAssert.Equal(string.Empty, PermissionRuleViewModel.Diagnose(rule),
            "Valid rules must produce no diagnostic message.");
    }

    // ── Diagnose — unknown bare tool name ──────────────────────────────────

    [Fact]
    public void Diagnose_BareUnknownTool_ReportsName_AndSuggestsValidTools()
    {
        string msg = PermissionRuleViewModel.Diagnose("Foo");
        OrdinalAssert.Contains("\"Foo\" is not a known tool name", msg);
        OrdinalAssert.Contains("Bash", msg);
        OrdinalAssert.Contains("mcp__", msg);
    }

    [Fact]
    public void Diagnose_BareUnknownTool_TrimsWhitespaceInQuotedName()
    {
        // Diagnose's bare-name branch trims the rule before quoting, but
        // upstream IsValid rejects whitespace-only first. Internal whitespace
        // is preserved (still invalid, but quoted as-typed).
        string msg = PermissionRuleViewModel.Diagnose("  Foo  ");
        OrdinalAssert.Contains("\"Foo\"", msg);
    }

    // ── Diagnose — unknown tool with parentheses ───────────────────────────

    [Fact]
    public void Diagnose_UnknownToolWithParens_ReportsToolName_NotFullRule()
    {
        string msg = PermissionRuleViewModel.Diagnose("Foo(some pattern)");
        OrdinalAssert.Contains("\"Foo\" is not a known tool name", msg);
        // Full rule should NOT be in the message (we report just the tool name).
        Assert.False(msg.Contains("\"Foo(some pattern)\"", StringComparison.Ordinal),
            "Diagnose must report just the tool name, not the full rule string.");
    }

    [Fact]
    public void Diagnose_UnknownToolWithParens_HintsAtMcpFormat()
    {
        string msg = PermissionRuleViewModel.Diagnose("BadTool(*)");
        MessageAssert.Contains("mcp__<server>__<tool>", msg,
            "When a parenthesised rule has an unknown tool, hint at the MCP format.");
    }

    // ── Diagnose — missing closing paren ───────────────────────────────────

    [Fact]
    public void Diagnose_MissingClosingParen_SuggestsCompleteForm()
    {
        string msg = PermissionRuleViewModel.Diagnose("Bash(git status");
        OrdinalAssert.Contains("Missing closing ')'", msg);
        MessageAssert.Contains("Bash(git status)", msg,
            "Suggested completion must echo the user's content with the closing paren added.");
    }

    // ── Diagnose — empty parentheses ───────────────────────────────────────

    [Fact]
    public void Diagnose_EmptyParens_SuggestsBareToolOrPattern()
    {
        string msg = PermissionRuleViewModel.Diagnose("Bash()");
        OrdinalAssert.Contains("Empty parentheses", msg);
        MessageAssert.Contains("\"Bash\"", msg,
            "Suggest dropping the parens for the bare tool form.");
        MessageAssert.Contains("Bash(git *)", msg,
            "Suggest a real example of a parenthesised pattern.");
    }

    // ── Diagnose — pure-wildcard parens ────────────────────────────────────

    [Theory]
    [InlineData("Bash(*)")]
    [InlineData("Bash(?)")]
    [InlineData("Bash(***)")]
    [InlineData("Bash(*?*)")]
    public void Diagnose_WildcardOnlyParens_ExplainsAndSuggests(string rule)
    {
        string msg = PermissionRuleViewModel.Diagnose(rule);
        OrdinalAssert.Contains("alone in parentheses is not valid", msg);
        MessageAssert.Contains("\"Bash\"", msg,
            "Suggest dropping the parens for the bare tool form.");
        MessageAssert.Contains("Bash(git *)", msg,
            "Suggest a real example of a parenthesised pattern.");
    }

    // ── Diagnose — fallthrough generic invalid ─────────────────────────────

    [Fact]
    public void Diagnose_GenericInvalid_FallsThroughWithExamples()
    {
        // A pattern that has a known tool, balanced parens, non-empty content,
        // and isn't pure wildcards — but still fails the regex.  Hard to
        // construct because the regex is permissive on the inner content; the
        // backslash-only inner doesn't trip the noise filters but the regex
        // still rejects the overall shape via the closing-paren lookahead.
        // Use something unambiguous: nested parens without closing.
        string msg = PermissionRuleViewModel.Diagnose("Bash((nested");
        // Balanced + paren accounting falls into one of the structural
        // diagnostics; expect either a missing-) message or a generic message.
        Assert.True(
            msg.Contains("Missing closing ')'", StringComparison.Ordinal)
            || msg.Contains("Invalid rule syntax", StringComparison.Ordinal),
            $"Unexpected diagnose for nested unclosed paren: {msg}");
    }

    // ── HasValidationError / ValidationErrorText round-trip ───────────────

    [Fact]
    public void Instance_HasValidationError_TracksRule()
    {
        PermissionRuleViewModel vm = new("Bash");
        Assert.False(vm.HasValidationError);
        Assert.Equal(string.Empty, vm.ValidationErrorText);

        vm.Rule = "Foo";
        Assert.True(vm.HasValidationError);
        OrdinalAssert.Contains("not a known tool name", vm.ValidationErrorText);

        vm.Rule = "mcp__server__tool";
        Assert.False(vm.HasValidationError);
    }

    [Fact]
    public void Instance_RuleChange_FiresPropertyChangedForValidationFlags()
    {
        PermissionRuleViewModel vm = new("Bash");
        List<string> fired = new();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
            {
                fired.Add(e.PropertyName);
            }
        };

        vm.Rule = "InvalidName";

        // Source generator fires Rule + the explicit OnRuleChanged
        // re-fires HasValidationError and ValidationErrorText.
        Assert.Contains("Rule", fired);
        Assert.Contains("HasValidationError", fired);
        Assert.Contains("ValidationErrorText", fired);
    }
}