using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.OpenCode.Sdk;

/// <summary>
/// OpenCode's configuration scope ladder — five rungs, two of them read-only.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order is load-bearing and silent when wrong.</b> <see cref="ScopeLadder"/> takes its
/// rungs <b>highest-priority first</b>, and the plan writes this ladder the other way round
/// (<c>global → custom → project → inline → managed</c>, ascending). Those are the same
/// ladder written in opposite directions; reversing one into the other by accident inverts
/// precedence everywhere, and the only symptom is that the wrong file wins. The anchor for
/// getting it right is S1's measurement, quoted per-rung below.
/// </para>
/// <para>
/// <b>Confidence is not uniform across these rungs, and pretending otherwise would be the
/// dangerous part.</b> Spike S1 measured exactly three of them against a real OpenCode
/// install and confirmed <c>OPENCODE_CONFIG &lt; project &lt; OPENCODE_CONFIG_CONTENT</c>.
/// The outermost two — <see cref="Global"/> and <see cref="Managed"/> — are asserted by the
/// plan but were never exercised. They are placed where every description of OpenCode's
/// layering puts them, and they are the two least likely to be wrong (a global config that
/// outranked a project one would be remarkable), but a reader should know which claims have
/// evidence behind them.
/// </para>
/// <para>
/// ⚠ <b>A sixth rung is unresolved.</b> Two places in the plan describe this ladder as
/// <c>… → managed → macOS MDM</c> — six rungs, with MDM above managed — while Phase 7's own
/// task list specifies the five implemented here. Neither is measured. The open question is
/// whether MDM is a distinct layer or merely how <c>managed</c> is delivered on macOS, which
/// is what it is for Claude. It is left out deliberately rather than invented: an
/// unpopulated rung is harmless, but a rung that does not exist upstream would appear in the
/// scope picker as a layer users can never populate. Resolving it needs an actual
/// MDM-managed macOS install, not a reading of the docs.
/// </para>
/// <para>
/// A ladder is the vocabulary of possible rungs, not a claim that every rung is populated —
/// discovery decides that, and <c>AgentConfigClientCore.EditableScopes</c> derives what the
/// user can edit from the documents actually found.
/// </para>
/// </remarks>
public static class OpenCodeScopes
{
    /// <summary>
    /// Policy-deployed configuration. Read-only. Highest priority, as for Claude.
    /// <para>⚠ Asserted by the plan, not measured by any spike.</para>
    /// </summary>
    public const string Managed = "Managed";

    /// <summary>
    /// <c>$OPENCODE_CONFIG_CONTENT</c> — a whole config passed inline through the
    /// environment. Read-only: there is no file to write back to.
    /// <para>✅ Measured (S1): outranks <see cref="Project"/>.</para>
    /// </summary>
    public const string Inline = "Inline";

    /// <summary>
    /// The project's own config, found by walking up from the working directory.
    /// <para>✅ Measured (S1): outranks <see cref="Custom"/>, outranked by <see cref="Inline"/>.</para>
    /// </summary>
    public const string Project = "Project";

    /// <summary>
    /// <c>$OPENCODE_CONFIG</c> — an explicit config file path.
    /// <para>✅ Measured (S1): outranked by <see cref="Project"/>.</para>
    /// </summary>
    public const string Custom = "Custom";

    /// <summary>
    /// <c>~/.config/opencode/opencode.json</c>, honouring <c>$OPENCODE_CONFIG_DIR</c>.
    /// Lowest priority.
    /// <para>⚠ Asserted by the plan, not measured by any spike.</para>
    /// </summary>
    public const string Global = "Global";

    /// <summary>
    /// The ladder itself, <b>highest-priority first</b> — the order
    /// <see cref="ScopeLadder"/> requires.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="ScopeLadder.Default"/> or any derivative of it. That
    /// instance <i>is</i> Claude's ladder and is the value <c>ConfigScope</c> encodes as
    /// <see langword="null"/>, so a second product inheriting it would silently adopt
    /// Claude's four rungs and Claude's read-only rule.
    /// </remarks>
    public static ScopeLadder Ladder { get; } = new(
        "opencode",
        new ScopeRung(Managed, IsReadOnly: true),
        new ScopeRung(Inline, IsReadOnly: true),
        new ScopeRung(Project, IsReadOnly: false),
        new ScopeRung(Custom, IsReadOnly: false),
        new ScopeRung(Global, IsReadOnly: false));
}
