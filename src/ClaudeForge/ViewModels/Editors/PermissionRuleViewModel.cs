using System.Text.RegularExpressions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>
/// A single permission rule (e.g. <c>Bash(rm *)</c>) inside an Allow/Deny/Ask list.
/// Wraps a mutable string so rules can be edited inline via a two-way-bound TextBox
/// (Avalonia cannot two-way-bind to a raw <see cref="string"/> element of an
/// <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>).
/// </summary>
public partial class PermissionRuleViewModel : ObservableObject
{
    // ---------------------------------------------------------------------------
    // Schema-derived validation (matches the permissionRule pattern in the schema)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The permissionRule regex — the SDK validator's own instance, so the two cannot
    /// drift. A valid rule is a known tool name (optionally followed by a
    /// parenthesised, not-purely-wildcard pattern) or any string starting with
    /// <c>mcp__</c>.
    /// <para>
    /// It runs on every keystroke against user-typed input, and needs no match
    /// timeout because the regex is <see cref="RegexOptions.NonBacktracking"/>, which is
    /// linear in the input length. This used to be a private copy with a 100 ms timeout.
    /// Under CPU load that timeout fired on 9-character input and reported valid rules
    /// as invalid.
    /// </para>
    /// </summary>
    private static Regex RuleRegex => PermissionTools.RuleRegex;

    /// <summary>
    /// Tool names accepted by the schema regex. The single source of truth lives in
    /// the SDK (<see cref="PermissionTools.NameSet"/>); this alias keeps existing
    /// call sites unchanged while eliminating the former hand-maintained copy.
    /// </summary>
    internal static readonly IReadOnlySet<string> KnownToolNames = PermissionTools.NameSet;

    // ---------------------------------------------------------------------------
    // Constructor
    // ---------------------------------------------------------------------------

    public PermissionRuleViewModel(string rule)
    {
        _rule = rule;
    }

    // ---------------------------------------------------------------------------
    // Observable property
    // ---------------------------------------------------------------------------

    [ObservableProperty] private string _rule;

    // Called by the source-generated OnRuleChanged partial method so AXAML
    // validation indicators respond immediately when the user edits a rule.
    partial void OnRuleChanged(string value)
    {
        OnPropertyChanged(nameof(HasValidationError));
        OnPropertyChanged(nameof(ValidationErrorText));
    }

    // ---------------------------------------------------------------------------
    // Validation — driven by the full schema regex, not just an empty-string check
    // ---------------------------------------------------------------------------

    /// <summary>True when the rule is empty, whitespace, or fails the schema permissionRule pattern.</summary>
    public bool HasValidationError => !IsValid(Rule);

    /// <summary>Human-readable validation message shown below the rule TextBox when invalid.</summary>
    public string ValidationErrorText => Diagnose(Rule);

    // ---------------------------------------------------------------------------
    // Public static helpers — used by PermissionsEditorViewModel for add-button validation
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns <c>true</c> when <paramref name="rule"/> satisfies the schema
    /// <c>permissionRule</c> pattern.
    /// </summary>
    public static bool IsValid(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return false;
        }

        return RuleRegex.IsMatch(rule);
    }

    /// <summary>
    /// Returns an empty string for a valid rule, or a human-friendly explanation
    /// of exactly why the rule is invalid and how to fix it.
    /// </summary>
    public static string Diagnose(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return "Rule cannot be empty.";
        }

        // mcp__ prefix is always valid — shouldn't reach here, but guard anyway.
        if (rule.StartsWith("mcp__", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (RuleRegex.IsMatch(rule))
        {
            return string.Empty;
        }

        // --- diagnose the specific problem ---

        int parenIdx = rule.IndexOf('(');

        if (parenIdx < 0)
        {
            // Bare name — must be a recognised tool.
            return $"\"{rule.Trim()}\" is not a known tool name. "
                   + "Valid tools: Bash, Edit, Read, Write, Glob, Grep, WebFetch, WebSearch, and others. "
                   + "For MCP tools, start the rule with mcp__.";
        }

        string toolName = rule[..parenIdx];

        if (!KnownToolNames.Contains(toolName))
        {
            return $"\"{toolName}\" is not a known tool name. "
                   + "Valid tools: Bash, Edit, Read, Write, Glob, Grep, WebFetch, WebSearch, and others. "
                   + "For MCP tools, use mcp__<server>__<tool>.";
        }

        if (!rule.EndsWith(')'))
        {
            return $"Missing closing ')'. Did you mean: {toolName}({rule[(parenIdx + 1)..]}) ?";
        }

        string inner = rule[(parenIdx + 1)..^1];

        if (string.IsNullOrEmpty(inner))
        {
            return $"Empty parentheses — to allow all uses of {toolName}, "
                   + $"drop the parentheses: \"{toolName}\". "
                   + $"Or add a specific pattern: \"{toolName}(git *)\".";
        }

        // The pattern requires one parens character other than * and ?, which
        // rejects content that is only wildcards.
        if (inner.All(c => c is '*' or ')' or '?'))
        {
            return $"\"{inner}\" alone in parentheses is not valid. "
                   + $"To allow all uses of {toolName}, use just \"{toolName}\" (no parentheses). "
                   + $"To restrict to a specific pattern, use a real argument like \"{toolName}(git *)\".";
        }

        return $"Invalid rule syntax. Examples: {toolName}(git *), {toolName}(npm run *), "
               + $"or just {toolName} to allow all invocations.";
    }
}