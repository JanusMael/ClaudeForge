namespace Bennewitz.Ninja.LayeredEditors.Abstractions;

/// <summary>
/// How much attention a setting's current state deserves.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>This replaces a hex STRING, and that is the point of the type.</b> Severity used to
/// travel as <c>string severityColor</c> — <c>"#D32F2F"</c> / <c>"#F4B400"</c> / <c>"#1976D2"</c> —
/// parsed at construction with a silent fall back to grey when the parse failed. Three separate
/// problems followed from that shape and all three disappear here rather than being fixed:
/// </para>
/// <list type="number">
///   <item>
///   <b>A colour is not a meaning.</b> Nothing could ask "is this setting security-critical?"
///   without comparing hex strings, so the settings tree, the effective view, the search hits and
///   the save preview all showed no severity at all — the Essentials page was the only surface
///   that could render it.
///   </item>
///   <item>
///   <b>One value cannot serve two themes.</b> A single literal was emitted regardless of theme
///   variant, so the light-theme reds and ambers were the ones that shipped into dark mode.
///   The enum is resolved through <c>AppSeverity*Brush</c>, which is declared per variant.
///   </item>
///   <item>
///   <b>An unparseable colour was indistinguishable from a deliberate grey.</b> The fallback
///   branch existed precisely because a string can be malformed; an enum member cannot.
///   </item>
/// </list>
/// <para>
/// ⚠ <b>Ordered least to most severe, deliberately.</b> Callers that need "the worst severity in
/// this group" — the settings tree rolling child rows up into a parent node — can use
/// <see cref="System.Math.Max(int, int)"/> over the underlying values instead of a comparison
/// table that would need editing every time a member is added. Insert new members in rank order,
/// never at the end for convenience.
/// </para>
/// <para>
/// Lives in the abstractions assembly, not in a UI one, because the danger tables that assign
/// these are product data and must be readable without a rendering stack.
/// <para>
/// ⛔ <b>Correction:</b> an earlier version of this remark said the assigning table "is schema
/// metadata (<c>IEditorSchema.Metadata</c>)". It is not, and it cannot be — severity escalates
/// with the writing scope and often depends on the current value, neither of which a static
/// per-property annotation can express, and <c>Metadata</c> had no consumers to extend. The
/// carrier is <see cref="IDangerClassifier"/>, supplied per product.
/// </para>
/// </para>
/// </remarks>
public enum AppSeverity
{
    /// <summary>
    /// Nothing notable — the ordinary case, and the default so an unannotated setting reads as
    /// unremarkable rather than as an unset value needing interpretation.
    /// </summary>
    Neutral = 0,

    /// <summary>
    /// Changes behaviour in a way worth knowing about, but cannot cost money or weaken a
    /// boundary. Blue.
    /// </summary>
    Info = 1,

    /// <summary>
    /// Affects cost, quality, or output volume. Recoverable, but a surprise on a bill or in a
    /// diff. Amber.
    /// </summary>
    Caution = 2,

    /// <summary>
    /// Weakens a security or safety boundary — sandboxing, permission bypass, unreviewed
    /// execution. Red.
    /// </summary>
    Critical = 3,
}
