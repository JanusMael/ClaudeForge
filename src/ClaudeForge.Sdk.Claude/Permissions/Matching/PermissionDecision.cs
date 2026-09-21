using Bennewitz.Ninja.AgentForge.Abstractions.Permissions;
using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions.Matching;

/// <summary>The bucket a matching rule came from.</summary>
public enum PermissionBucket
{
    /// <summary>The <c>permissions.allow</c> array.</summary>
    Allow,

    /// <summary>The <c>permissions.ask</c> array.</summary>
    Ask,

    /// <summary>The <c>permissions.deny</c> array.</summary>
    Deny,
}

/// <summary>
/// The result of resolving a <see cref="PermissionCandidate"/> against a set of
/// permission rules, including which rule/bucket/scope drove the decision so the
/// dry-run tester can explain it.
/// </summary>
/// <param name="Outcome">The resolved outcome.</param>
/// <param name="MatchedRule">The rule that matched, or <see langword="null"/> when no rule matched (<see cref="PermissionOutcome.Default"/>).</param>
/// <param name="MatchedBucket">The bucket the matching rule came from, or <see langword="null"/> for <see cref="PermissionOutcome.Default"/>.</param>
/// <param name="MatchedScope">The scope the matching rule came from in a merged resolution, or <see langword="null"/> (single-scope resolution, or no match).</param>
/// <param name="DefaultMode">The effective <c>defaultMode</c> that applies when <see cref="Outcome"/> is <see cref="PermissionOutcome.Default"/>.</param>
/// <param name="DecidingSubcommand">For a compound Bash command, the subcommand that drove the decision; <see langword="null"/> for a simple command.</param>
public sealed record PermissionDecision(
    PermissionOutcome Outcome,
    PermissionRule? MatchedRule,
    PermissionBucket? MatchedBucket,
    ConfigScope? MatchedScope,
    PermissionDefaultMode? DefaultMode,
    string? DecidingSubcommand = null);

/// <summary>
/// One scope's allow/deny/ask rule lists, for merged (cross-scope) resolution.
/// </summary>
/// <param name="Scope">The configuration scope these rules came from.</param>
/// <param name="Allow">Allow rules at this scope.</param>
/// <param name="Deny">Deny rules at this scope.</param>
/// <param name="Ask">Ask rules at this scope.</param>
public sealed record ScopedPermissionRules(
    ConfigScope Scope,
    IReadOnlyList<PermissionRule> Allow,
    IReadOnlyList<PermissionRule> Deny,
    IReadOnlyList<PermissionRule> Ask);
