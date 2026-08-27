namespace Bennewitz.Ninja.OpenCode.Sdk.Agents;

/// <summary>
/// How an agent may be invoked.
/// </summary>
public enum OpenCodeAgentMode
{
    /// <summary>The key is absent — OpenCode's own default applies.</summary>
    Unset,

    /// <summary>Only reachable as a subagent.</summary>
    Subagent,

    /// <summary>Selectable as the primary agent.</summary>
    Primary,

    /// <summary>Both.</summary>
    All,
}

/// <summary>
/// One entry of the <c>agent</c> map: an agent definition, or an override of a built-in one.
/// </summary>
/// <remarks>
/// <para>
/// Every field is optional — the schema declares no <c>required</c> — because an entry is usually a
/// <i>partial override</i> of a built-in rather than a whole definition. That is why nothing here
/// has a non-null default: a field left unset must stay absent from the written value, since
/// writing it would override something the user never touched.
/// </para>
/// <para>
/// ⚠ <b><see cref="Permission"/> is held as an opaque value on purpose.</b> Its schema is a bare
/// <c>$ref</c> to the very same <c>PermissionConfig</c> the top-level <c>permission</c> key uses,
/// so parsing it here would duplicate the permission model — and the two copies would drift. The
/// editor hands this value straight to the permission grid instead, and
/// <c>ActionOnlyToolsSchemaDriftTests</c> asserts the two schema locations stay identical.
/// </para>
/// <para>
/// ⚠ <b>The schema does NOT set <c>additionalProperties: false</c> here</b>, unlike the MCP
/// variants. Unknown fields are therefore legal rather than merely tolerated, which makes
/// <see cref="Extras"/> a correctness requirement rather than a courtesy.
/// </para>
/// </remarks>
public sealed record OpenCodeAgentConfig
{
    /// <summary>Model id, free-form.</summary>
    public string? Model { get; init; }

    /// <summary>Model variant, free-form.</summary>
    public string? Variant { get; init; }

    /// <summary>Sampling temperature.</summary>
    public double? Temperature { get; init; }

    /// <summary>Nucleus-sampling probability (<c>top_p</c>).</summary>
    public double? TopP { get; init; }

    /// <summary>System prompt.</summary>
    public string? Prompt { get; init; }

    /// <summary>
    /// Per-tool enable flags.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Deprecated by the schema</b> — its description reads "@deprecated Use 'permission'
    /// field instead". Kept editable because existing configs contain it and silently dropping a
    /// user's tool flags would change agent behaviour; surfaced as deprecated so nobody adds more.
    /// </remarks>
    public IReadOnlyList<KeyValuePair<string, bool>> Tools { get; init; } = [];

    /// <summary>Whether the agent is disabled.</summary>
    public bool? Disable { get; init; }

    /// <summary>Human-readable description.</summary>
    public string? Description { get; init; }

    /// <summary>How the agent may be invoked.</summary>
    public OpenCodeAgentMode Mode { get; init; } = OpenCodeAgentMode.Unset;

    /// <summary>Whether the agent is hidden from pickers.</summary>
    public bool? Hidden { get; init; }

    /// <summary>
    /// Provider-specific options, preserved verbatim.
    /// </summary>
    /// <remarks>
    /// Typed only as <c>object</c> by the schema, with no declared properties — so there is nothing
    /// to model and nothing this editor could usefully render. Held and written back unchanged.
    /// </remarks>
    public object? Options { get; init; }

    /// <summary>
    /// Display colour: either a free-form string or one of the theme role names.
    /// </summary>
    /// <remarks>
    /// The schema's union is <c>string | enum(primary…info)</c>, and the first arm carries no
    /// pattern — so <b>every</b> string validates and the enum arm is a suggestion list rather than
    /// a constraint. Treating it as a closed enum would reject the hex values the union exists to
    /// permit; the same free-form-with-suggestions shape as <c>model</c>.
    /// </remarks>
    public string? Color { get; init; }

    /// <summary>Step budget.</summary>
    public long? Steps { get; init; }

    /// <summary>Maximum steps. Distinct from <see cref="Steps"/>; the schema declares both.</summary>
    public long? MaxSteps { get; init; }

    /// <summary>
    /// The agent's permission override, as value currency — see the type remarks.
    /// </summary>
    public object? Permission { get; init; }

    /// <summary>Fields this model does not surface, preserved in the order they arrived.</summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Extras { get; init; } = [];

    /// <summary>
    /// The entry exactly as read, set only when it was not an object at all.
    /// </summary>
    public object? Raw { get; init; }

    /// <summary>
    /// True when the entry was not an object, so <see cref="Raw"/> is what gets written back.
    /// </summary>
    /// <remarks>
    /// ⚠ An explicit flag rather than <c>Raw is not null</c>, which cannot tell a JSON
    /// <c>null</c> entry from an object one. That derivation made <c>"agent": {"x": null}</c>
    /// round-trip as <c>{"x": {}}</c> — the editor inventing an empty agent where the user had
    /// written nothing. Found by the round-trip test, not by reading the code.
    /// </remarks>
    public bool IsOpaque { get; init; }
}

/// <summary>
/// The agent names OpenCode ships and lets a config override.
/// </summary>
/// <remarks>
/// Read from the bundled schema's named properties on <c>agent</c>, which sit alongside an
/// <c>additionalProperties</c> of the same type — so these seven are overridable built-ins and any
/// other key defines a new agent. The distinction is worth surfacing: overriding <c>build</c>
/// changes how the tool behaves, whereas adding <c>my-agent</c> only adds a choice.
/// </remarks>
public static class OpenCodeBuiltInAgents
{
    /// <summary>The seven overridable built-in agent names.</summary>
    public static IReadOnlySet<string> Names { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "plan",
            "build",
            "general",
            "explore",
            "title",
            "summary",
            "compaction",
        };
}
