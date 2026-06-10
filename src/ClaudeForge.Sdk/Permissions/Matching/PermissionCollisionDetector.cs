namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

/// <summary>The relationship a candidate rule has with an existing one.</summary>
public enum PermissionCollisionKind
{
    /// <summary>
    /// The candidate overlaps with a rule in a <b>different</b> bucket, so the two
    /// buckets disagree about the same action. Because evaluation is
    /// deny → ask → allow, the higher-priority bucket wins regardless of which the
    /// user is adding to — worth flagging.
    /// </summary>
    Conflict,

    /// <summary>
    /// The candidate overlaps with a rule in the <b>same</b> bucket (one covers the
    /// other), so one of the two is redundant.
    /// </summary>
    Redundant,
}

/// <summary>
/// A detected relationship between a candidate rule and one existing rule.
/// </summary>
public sealed record PermissionCollision(
    PermissionCollisionKind Kind,
    PermissionRule ExistingRule,
    PermissionBucket ExistingBucket);

/// <summary>
/// Best-effort, <b>conservative</b> add-time detection of rules that conflict
/// (across buckets) or are redundant (within a bucket) with what the user is
/// adding. Favors precision over recall: it reports only high-confidence overlaps
/// (exact cross-bucket duplicates, bare-tool / <c>Tool(*)</c> coverage, Bash/
/// PowerShell prefix subsumption, and MCP whole-server coverage) and stays silent
/// on cases it can't decide cleanly (e.g. arbitrary path globs), so it never
/// nags with false positives.
/// </summary>
/// <remarks>
/// Subsumption reuses <see cref="BashRuleMatcher"/> and the
/// <see cref="ParsedPermissionRule"/> structure rather than attempting general
/// glob-subset reasoning (undecidable in the general case). Strings are compared
/// after <see cref="PermissionRuleNormalizer"/> so the colon/space and path-
/// separator variants converge before comparison.
/// </remarks>
public static class PermissionCollisionDetector
{
    /// <summary>
    /// Returns the most relevant collision for adding <paramref name="candidate"/>
    /// to <paramref name="targetBucket"/>, or <see langword="null"/> when none is
    /// detected. A cross-bucket <see cref="PermissionCollisionKind.Conflict"/> is
    /// preferred over a same-bucket <see cref="PermissionCollisionKind.Redundant"/>.
    /// </summary>
    public static PermissionCollision? Detect(
        PermissionRule candidate,
        PermissionBucket targetBucket,
        IReadOnlyList<PermissionRule> allow,
        IReadOnlyList<PermissionRule> deny,
        IReadOnlyList<PermissionRule> ask)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        string candNorm = PermissionRuleNormalizer.Normalize(candidate.Value);
        if (!ParsedPermissionRule.TryParse(candNorm, out ParsedPermissionRule? cand))
        {
            return null;
        }

        (IReadOnlyList<PermissionRule> rules, PermissionBucket bucket)[] buckets =
        [
            (allow ?? [], PermissionBucket.Allow),
            (ask ?? [], PermissionBucket.Ask),
            (deny ?? [], PermissionBucket.Deny),
        ];

        PermissionCollision? redundant = null;

        foreach ((IReadOnlyList<PermissionRule> rules, PermissionBucket bucket) in buckets)
        {
            foreach (PermissionRule existing in rules)
            {
                string exNorm = PermissionRuleNormalizer.Normalize(existing.Value);
                if (!ParsedPermissionRule.TryParse(exNorm, out ParsedPermissionRule? ex))
                {
                    continue;
                }

                bool exact = string.Equals(exNorm, candNorm, StringComparison.Ordinal);
                bool overlap = exact || Covers(cand, ex) || Covers(ex, cand);
                if (!overlap)
                {
                    continue;
                }

                if (bucket != targetBucket)
                {
                    // Cross-bucket overlap is the strongest signal — return now.
                    return new PermissionCollision(PermissionCollisionKind.Conflict, existing, bucket);
                }

                // Same bucket: an exact duplicate is handled by the caller's
                // dedupe; only a non-exact overlap is a useful "redundant" note.
                if (!exact)
                {
                    redundant ??= new PermissionCollision(
                        PermissionCollisionKind.Redundant, existing, bucket);
                }
            }
        }

        return redundant;
    }

    /// <summary>
    /// True when rule <paramref name="a"/> is at least as broad as
    /// <paramref name="b"/> — i.e. <paramref name="a"/> matches a representative
    /// candidate derived from <paramref name="b"/>. Conservative: returns
    /// <see langword="false"/> for relationships it can't decide cleanly.
    /// </summary>
    private static bool Covers(ParsedPermissionRule a, ParsedPermissionRule b)
    {
        // MCP: a whole-server rule covers any tool on the same server.
        if (a.IsMcp || b.IsMcp)
        {
            return a.IsMcp && b.IsMcp
                   && string.Equals(a.McpServer, b.McpServer, StringComparison.Ordinal)
                   && a.McpAllTools;
        }

        // Different tools never collide.
        if (!string.Equals(a.ToolName, b.ToolName, StringComparison.Ordinal))
        {
            return false;
        }

        // A bare tool / Tool(*) covers every use of that tool.
        if (a.MatchesAllUses)
        {
            return true;
        }

        // Bash / PowerShell: derive a literal command from b and test a's glob.
        if (a.ToolName is "Bash" or "PowerShell")
        {
            string? rep = ShellRepresentative(b);
            if (rep is null)
            {
                return false;
            }

            return BashRuleMatcher.Match(a, rep, caseInsensitive: a.ToolName == "PowerShell");
        }

        // Read/Edit/Write/WebFetch/Agent: only the bare-tool case (handled above)
        // is treated as coverage; everything else relies on exact-string matching
        // to stay conservative (no path-glob subset reasoning).
        return false;
    }

    /// <summary>
    /// A representative literal command for a Bash/PowerShell rule: the specifier
    /// with a trailing <c>:*</c>/<c> *</c> stripped. Returns <see langword="null"/>
    /// when the remainder still contains a <c>*</c> (a leading/interior wildcard
    /// can't be sampled into one clean literal), so callers skip subsumption.
    /// </summary>
    private static string? ShellRepresentative(ParsedPermissionRule rule)
    {
        if (rule.Specifier is not { Length: > 0 } spec)
        {
            return null;
        }

        string core = spec;
        if (core.EndsWith(":*", StringComparison.Ordinal) || core.EndsWith(" *", StringComparison.Ordinal))
        {
            core = core[..^2];
        }

        return core.Contains('*', StringComparison.Ordinal) ? null : core;
    }
}
