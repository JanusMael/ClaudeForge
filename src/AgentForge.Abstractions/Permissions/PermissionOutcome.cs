namespace Bennewitz.Ninja.AgentForge.Abstractions.Permissions;

/// <summary>
/// What a permission system decided about one candidate action: run it, prompt first,
/// block it, or — when nothing matched — fall through to whatever the product's default
/// behaviour is.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is shared, when almost nothing else about permissions is.</b> Both products
/// spell the three deliberate answers the same way. Claude Code sorts rules into
/// <c>permissions.allow</c> / <c>ask</c> / <c>deny</c> arrays; OpenCode maps a tool or glob
/// straight to the string <c>"allow"</c>, <c>"ask"</c> or <c>"deny"</c>. Different syntax,
/// different matching, same vocabulary — so this enum is the answer to "what happens", and
/// carries no opinion about how that answer was reached.
/// </para>
/// <para>
/// <b>What deliberately did not come with it.</b> The bucket a rule was drawn from, the
/// default mode that applies on fall-through, and the rule syntax itself are all
/// product-specific, and each product keeps its own. An abstraction general enough to
/// express Claude's tool specifiers, gitignore path semantics and Bash chain-splitting
/// <i>and</i> OpenCode's flat tool-to-glob map would be an abstraction over two things that
/// merely rhyme. Two parallel implementations sharing one vocabulary is the smaller lie.
/// </para>
/// <para>
/// <b>The zero value is deliberate.</b> <see cref="Default"/> is declared first so an
/// uninitialised field reads as "nothing decided this yet" rather than as an affirmative
/// grant. The members were previously ordered <c>Allow, Ask, Deny, Default</c>, which made
/// <c>default(PermissionOutcome)</c> equal to <see cref="Allow"/> — the one value that must
/// never arrive by accident. Nothing observed it, because the only field of this type is
/// gated behind a "has a result yet" flag; but the type is shared now, and the next product
/// to hold one is not bound by that gate. Ordinals are not persisted or compared anywhere
/// (measured: shifting every ordinal changed no test), so the order is free to be safe.
/// This mirrors the deliberate choice that <c>default(ConfigScope)</c> is the read-only
/// <c>Managed</c> rung rather than an editable one.
/// </para>
/// </remarks>
public enum PermissionOutcome
{
    /// <summary>
    /// No rule matched. What happens next is the product's own fall-through behaviour, so
    /// this value says only that nothing decided the call — never that it was permitted.
    /// Deliberately the zero value; see the remarks.
    /// </summary>
    Default,

    /// <summary>An allow rule matched — the tool runs without prompting.</summary>
    Allow,

    /// <summary>An ask rule matched — the agent prompts the user before running the tool.</summary>
    Ask,

    /// <summary>A deny rule matched — the tool call is blocked.</summary>
    Deny,
}
