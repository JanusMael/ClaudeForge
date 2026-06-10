using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

/// <summary>
/// Resolves what Claude Code would decide for a <see cref="PermissionCandidate"/>
/// given a set of allow/deny/ask rules and a default mode.
/// </summary>
/// <remarks>
/// <para>
/// <b>Spec.</b>
/// <see href="https://code.claude.com/docs/en/permissions">code.claude.com/docs/en/permissions</see>
/// §"Manage permissions" and §"Settings precedence". Rules are evaluated
/// <b>deny → ask → allow</b>; the first matching rule wins, so a deny anywhere
/// beats an allow anywhere. In a merged (cross-scope) resolution this bucket
/// order dominates scope order: every scope's deny rules are checked before any
/// scope's ask rules, and all ask before any allow. Within a bucket, scopes are
/// visited in precedence order (Managed → Local → Project → User) so the winning
/// match is attributed to the highest-precedence scope.
/// </para>
/// <para>
/// <b>Compound Bash commands.</b> Claude Code splits a compound command on shell
/// operators and requires each subcommand to match independently
/// (see <see cref="BashCommandSplitter"/>). This resolver mirrors that: it
/// resolves each subcommand and returns the most restrictive outcome
/// (Deny &gt; Ask &gt; Default &gt; Allow), since the whole command is only as
/// permitted as its least-permitted part. Recognized process wrappers are
/// stripped from each subcommand first.
/// </para>
/// </remarks>
public static class PermissionResolver
{
    /// <summary>Resolve against a single scope's rule lists.</summary>
    public static PermissionDecision Resolve(
        PermissionCandidate candidate,
        IReadOnlyList<PermissionRule> allow,
        IReadOnlyList<PermissionRule> deny,
        IReadOnlyList<PermissionRule> ask,
        PermissionDefaultMode? defaultMode,
        PermissionMatchContext context)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(context);

        List<RuleGroup> groups =
        [
            new(PermissionBucket.Deny, null, deny ?? []),
            new(PermissionBucket.Ask, null, ask ?? []),
            new(PermissionBucket.Allow, null, allow ?? []),
        ];

        return ResolveWithCompound(candidate, groups, defaultMode, context);
    }

    /// <summary>
    /// Resolve against multiple scopes. <paramref name="scopes"/> should be
    /// ordered by precedence (highest first, e.g. Managed → … → User); the
    /// resolver enforces deny-&gt;ask-&gt;allow bucket order across all of them.
    /// </summary>
    public static PermissionDecision ResolveMerged(
        PermissionCandidate candidate,
        IReadOnlyList<ScopedPermissionRules> scopes,
        PermissionDefaultMode? defaultMode,
        PermissionMatchContext context)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(context);

        // Bucket order dominates scope order: all denies, then all asks, then all
        // allows — each in scope-precedence order for correct attribution.
        IReadOnlyList<ScopedPermissionRules> ordered =
            scopes.OrderBy(s => (int)s.Scope).ToList();

        List<RuleGroup> groups = [];
        groups.AddRange(ordered.Select(s => new RuleGroup(PermissionBucket.Deny, s.Scope, s.Deny)));
        groups.AddRange(ordered.Select(s => new RuleGroup(PermissionBucket.Ask, s.Scope, s.Ask)));
        groups.AddRange(ordered.Select(s => new RuleGroup(PermissionBucket.Allow, s.Scope, s.Allow)));

        return ResolveWithCompound(candidate, groups, defaultMode, context);
    }

    private static PermissionDecision ResolveWithCompound(
        PermissionCandidate candidate,
        IReadOnlyList<RuleGroup> groups,
        PermissionDefaultMode? defaultMode,
        PermissionMatchContext context)
    {
        // Only Bash/PowerShell candidates carry compound semantics.
        bool isShell = candidate is { ToolName: "Bash" or "PowerShell", CommandText: not null };
        if (!isShell)
        {
            return ResolveCore(candidate, groups, defaultMode, context, decidingSubcommand: null);
        }

        IReadOnlyList<string> subs = BashCommandSplitter.SplitCompound(candidate.CommandText!);
        if (subs.Count <= 1)
        {
            string cmd = subs.Count == 1 ? subs[0] : candidate.CommandText!;
            string stripped = BashCommandSplitter.StripWrappers(cmd);
            PermissionCandidate single = WithCommand(candidate, stripped);
            // A simple command has no "deciding subcommand" distinct from itself.
            return ResolveCore(single, groups, defaultMode, context, decidingSubcommand: null);
        }

        // Compound: resolve each subcommand; the whole is as restricted as its
        // least-permitted part.
        PermissionDecision? worst = null;
        foreach (string sub in subs)
        {
            string stripped = BashCommandSplitter.StripWrappers(sub);
            PermissionCandidate part = WithCommand(candidate, stripped);
            PermissionDecision d = ResolveCore(part, groups, defaultMode, context, decidingSubcommand: sub);
            if (worst is null || Restrictiveness(d.Outcome) > Restrictiveness(worst.Outcome))
            {
                worst = d;
            }
        }

        return worst!;
    }

    private static PermissionDecision ResolveCore(
        PermissionCandidate candidate,
        IReadOnlyList<RuleGroup> groups,
        PermissionDefaultMode? defaultMode,
        PermissionMatchContext context,
        string? decidingSubcommand)
    {
        foreach (RuleGroup group in groups)
        {
            foreach (PermissionRule rule in group.Rules)
            {
                if (!ParsedPermissionRule.TryParse(rule.Value, out ParsedPermissionRule? parsed))
                {
                    continue;
                }

                if (PermissionRuleMatcher.Matches(parsed, candidate, context))
                {
                    return new PermissionDecision(
                        OutcomeFor(group.Bucket),
                        rule,
                        group.Bucket,
                        group.Scope,
                        defaultMode,
                        decidingSubcommand);
                }
            }
        }

        return new PermissionDecision(
            PermissionOutcome.Default, null, null, null, defaultMode, decidingSubcommand);
    }

    private static PermissionOutcome OutcomeFor(PermissionBucket bucket) => bucket switch
    {
        PermissionBucket.Deny => PermissionOutcome.Deny,
        PermissionBucket.Ask => PermissionOutcome.Ask,
        PermissionBucket.Allow => PermissionOutcome.Allow,
        var _ => PermissionOutcome.Default,
    };

    // Most-restrictive ranking for combining compound subcommand outcomes.
    private static int Restrictiveness(PermissionOutcome outcome) => outcome switch
    {
        PermissionOutcome.Deny => 3,
        PermissionOutcome.Ask => 2,
        PermissionOutcome.Default => 1, // unmatched ⇒ not pre-allowed ⇒ would prompt/deny per mode
        PermissionOutcome.Allow => 0,
        var _ => 0,
    };

    private static PermissionCandidate WithCommand(PermissionCandidate original, string command) =>
        original.ToolName == "PowerShell"
            ? PermissionCandidate.PowerShell(command)
            : PermissionCandidate.Bash(command);

    private sealed record RuleGroup(
        PermissionBucket Bucket,
        ConfigScope? Scope,
        IReadOnlyList<PermissionRule> Rules);
}
