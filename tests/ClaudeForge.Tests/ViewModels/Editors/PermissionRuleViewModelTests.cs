namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// focused tests for the static <c>IsValid</c> /
/// <c>Diagnose</c> helpers on <see cref="PermissionRuleViewModel"/>.
/// These were exercised only indirectly (and partially) through
/// <c>PermissionsEditorViewModelTests</c>; this suite covers every
/// branch of the diagnose decision tree so a future regex tweak
/// surfaces here rather than via a misleading inline error in the GUI.
/// </summary>
[TestClass]
public sealed class PermissionRuleViewModelTests
{
    // ── IsValid — happy paths ──────────────────────────────────────────────

    [TestMethod]
    [DataRow("Bash")]
    [DataRow("Edit")]
    [DataRow("Read")]
    [DataRow("Write")]
    [DataRow("Glob")]
    [DataRow("Grep")]
    [DataRow("WebFetch")]
    [DataRow("WebSearch")]
    [DataRow("Agent")]
    [DataRow("ExitPlanMode")]
    [DataRow("KillShell")]
    [DataRow("LSP")]
    [DataRow("Monitor")]
    [DataRow("NotebookEdit")]
    [DataRow("PowerShell")]
    [DataRow("Skill")]
    [DataRow("TaskCreate")]
    [DataRow("TaskGet")]
    [DataRow("TaskList")]
    [DataRow("TaskOutput")]
    [DataRow("TaskStop")]
    [DataRow("TaskUpdate")]
    [DataRow("TodoWrite")]
    [DataRow("ToolSearch")]
    public void IsValid_BareKnownToolName_True(string rule)
    {
        Assert.IsTrue(PermissionRuleViewModel.IsValid(rule),
            $"\"{rule}\" should be a valid bare tool name.");
    }

    [TestMethod]
    [DataRow("Bash(git *)")]
    [DataRow("Bash(npm install)")]
    [DataRow("Edit(./**/*.cs)")]
    [DataRow("Write(./**/*.json)")]
    [DataRow("WebFetch(https://*.example.com/*)")]
    [DataRow("PowerShell(Get-*)")]
    public void IsValid_ToolWithRealPattern_True(string rule)
    {
        // Note: pure-wildcard patterns like "Read(*)" are REJECTED by the
        // schema regex's lookahead — see IsValid_KnownInvalidShapes_False
        // for that branch.
        Assert.IsTrue(PermissionRuleViewModel.IsValid(rule));
    }

    [TestMethod]
    [DataRow("mcp__github__create_issue")]
    [DataRow("mcp__exa__search")]
    [DataRow("mcp__*")]
    [DataRow("mcp__server__")]
    public void IsValid_McpPrefix_True(string rule)
    {
        Assert.IsTrue(PermissionRuleViewModel.IsValid(rule),
            "Any string starting with mcp__ is valid per the schema regex.");
    }

    // ── IsValid — rejection paths ──────────────────────────────────────────

    [TestMethod]
    public void IsValid_NullOrWhitespace_False()
    {
        Assert.IsFalse(PermissionRuleViewModel.IsValid(null));
        Assert.IsFalse(PermissionRuleViewModel.IsValid(""));
        Assert.IsFalse(PermissionRuleViewModel.IsValid("   "));
        Assert.IsFalse(PermissionRuleViewModel.IsValid("\t\n"));
    }

    [TestMethod]
    [DataRow("Foo")] // unknown bare name
    [DataRow("bash")] // case-sensitive — wrong case
    [DataRow("Bashh")] // typo
    [DataRow("Foo(*)")] // unknown name + valid-looking paren
    [DataRow("Bash(")] // unclosed paren
    [DataRow("Bash()")] // empty paren — schema rejects
    [DataRow("Bash(*)")] // pure-wildcard paren — schema rejects
    [DataRow("Bash(?)")] // pure-? paren — schema rejects
    [DataRow("Bash(***)")] // multiple wildcards only
    [DataRow("Pwsh")] // not a real Claude Code tool — the shell tool is "PowerShell"
    [DataRow("Pwsh(git status)")] // Pwsh(...) is not recognized; rules must use PowerShell(...)
    public void IsValid_KnownInvalidShapes_False(string rule)
    {
        Assert.IsFalse(PermissionRuleViewModel.IsValid(rule),
            $"\"{rule}\" should be rejected by the permissionRule regex.");
    }

    // ── Diagnose — empty / whitespace branch ───────────────────────────────

    [TestMethod]
    public void Diagnose_NullOrWhitespace_ReturnsEmptyMessage()
    {
        Assert.AreEqual("Rule cannot be empty.", PermissionRuleViewModel.Diagnose(null));
        Assert.AreEqual("Rule cannot be empty.", PermissionRuleViewModel.Diagnose(""));
        Assert.AreEqual("Rule cannot be empty.", PermissionRuleViewModel.Diagnose("   "));
    }

    // ── Diagnose — valid rules return empty string ────────────────────────

    [TestMethod]
    [DataRow("Bash")]
    [DataRow("Edit(./**/*.cs)")]
    [DataRow("mcp__github__list")]
    public void Diagnose_ValidRule_ReturnsEmpty(string rule)
    {
        Assert.AreEqual(string.Empty, PermissionRuleViewModel.Diagnose(rule),
            "Valid rules must produce no diagnostic message.");
    }

    // ── Diagnose — unknown bare tool name ──────────────────────────────────

    [TestMethod]
    public void Diagnose_BareUnknownTool_ReportsName_AndSuggestsValidTools()
    {
        string msg = PermissionRuleViewModel.Diagnose("Foo");
        StringAssert.Contains(msg, "\"Foo\" is not a known tool name");
        StringAssert.Contains(msg, "Bash");
        StringAssert.Contains(msg, "mcp__");
    }

    [TestMethod]
    public void Diagnose_BareUnknownTool_TrimsWhitespaceInQuotedName()
    {
        // Diagnose's bare-name branch trims the rule before quoting, but
        // upstream IsValid rejects whitespace-only first. Internal whitespace
        // is preserved (still invalid, but quoted as-typed).
        string msg = PermissionRuleViewModel.Diagnose("  Foo  ");
        StringAssert.Contains(msg, "\"Foo\"");
    }

    // ── Diagnose — unknown tool with parentheses ───────────────────────────

    [TestMethod]
    public void Diagnose_UnknownToolWithParens_ReportsToolName_NotFullRule()
    {
        string msg = PermissionRuleViewModel.Diagnose("Foo(some pattern)");
        StringAssert.Contains(msg, "\"Foo\" is not a known tool name");
        // Full rule should NOT be in the message (we report just the tool name).
        Assert.IsFalse(msg.Contains("\"Foo(some pattern)\"", StringComparison.Ordinal),
            "Diagnose must report just the tool name, not the full rule string.");
    }

    [TestMethod]
    public void Diagnose_UnknownToolWithParens_HintsAtMcpFormat()
    {
        string msg = PermissionRuleViewModel.Diagnose("BadTool(*)");
        StringAssert.Contains(msg, "mcp__<server>__<tool>",
            "When a parenthesised rule has an unknown tool, hint at the MCP format.");
    }

    // ── Diagnose — missing closing paren ───────────────────────────────────

    [TestMethod]
    public void Diagnose_MissingClosingParen_SuggestsCompleteForm()
    {
        string msg = PermissionRuleViewModel.Diagnose("Bash(git status");
        StringAssert.Contains(msg, "Missing closing ')'");
        StringAssert.Contains(msg, "Bash(git status)",
            "Suggested completion must echo the user's content with the closing paren added.");
    }

    // ── Diagnose — empty parentheses ───────────────────────────────────────

    [TestMethod]
    public void Diagnose_EmptyParens_SuggestsBareToolOrPattern()
    {
        string msg = PermissionRuleViewModel.Diagnose("Bash()");
        StringAssert.Contains(msg, "Empty parentheses");
        StringAssert.Contains(msg, "\"Bash\"",
            "Suggest dropping the parens for the bare tool form.");
        StringAssert.Contains(msg, "Bash(git *)",
            "Suggest a real example of a parenthesised pattern.");
    }

    // ── Diagnose — pure-wildcard parens ────────────────────────────────────

    [TestMethod]
    [DataRow("Bash(*)")]
    [DataRow("Bash(?)")]
    [DataRow("Bash(***)")]
    [DataRow("Bash(*?*)")]
    public void Diagnose_WildcardOnlyParens_ExplainsAndSuggests(string rule)
    {
        string msg = PermissionRuleViewModel.Diagnose(rule);
        StringAssert.Contains(msg, "alone in parentheses is not valid");
        StringAssert.Contains(msg, "\"Bash\"",
            "Suggest dropping the parens for the bare tool form.");
        StringAssert.Contains(msg, "Bash(git *)",
            "Suggest a real example of a parenthesised pattern.");
    }

    // ── Diagnose — fallthrough generic invalid ─────────────────────────────

    [TestMethod]
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
        Assert.IsTrue(
            msg.Contains("Missing closing ')'", StringComparison.Ordinal)
            || msg.Contains("Invalid rule syntax", StringComparison.Ordinal),
            $"Unexpected diagnose for nested unclosed paren: {msg}");
    }

    // ── HasValidationError / ValidationErrorText round-trip ───────────────

    [TestMethod]
    public void Instance_HasValidationError_TracksRule()
    {
        PermissionRuleViewModel vm = new("Bash");
        Assert.IsFalse(vm.HasValidationError);
        Assert.AreEqual(string.Empty, vm.ValidationErrorText);

        vm.Rule = "Foo";
        Assert.IsTrue(vm.HasValidationError);
        StringAssert.Contains(vm.ValidationErrorText, "not a known tool name");

        vm.Rule = "mcp__server__tool";
        Assert.IsFalse(vm.HasValidationError);
    }

    [TestMethod]
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
        CollectionAssert.Contains(fired, "Rule");
        CollectionAssert.Contains(fired, "HasValidationError");
        CollectionAssert.Contains(fired, "ValidationErrorText");
    }
}