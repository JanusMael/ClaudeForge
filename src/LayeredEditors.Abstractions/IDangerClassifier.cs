namespace Bennewitz.Ninja.LayeredEditors.Abstractions;

/// <summary>
/// A product's answer to "how much should the user care about this setting, here, right now?".
/// </summary>
/// <remarks>
/// <para>
/// One classifier per product, supplied by the host. The editor library owns no danger policy of
/// its own: which knobs are dangerous is a statement about an agent's threat model, and the two
/// products' models differ (OpenCode has <c>share</c> and a bindable HTTP server; Claude has
/// neither).
/// </para>
/// <para>
/// ⛔⛔ <b>This is deliberately NOT an annotation on <see cref="IEditorSchema"/>, and an earlier
/// draft of the plan got that wrong.</b> Two reasons, both fatal to the annotation shape:
/// </para>
/// <list type="number">
///   <item>
///   <b><see cref="IEditorSchema"/> is per-property and scope-independent.</b> Danger is neither.
///   <c>provider.*.options.apiKey</c> is caution at a user-global scope and critical at project
///   scope because a project file is committed to git, and <c>share</c> is dangerous only when
///   its value is <c>auto</c>. A static per-schema field can express neither the scope
///   escalation nor the value predicate.
///   </item>
///   <item>
///   <b><c>IEditorSchema.Metadata</c> had zero consumers.</b> The plan claimed it was "an open
///   extensibility bag for exactly this" and implied existing plumbing to follow; grepping
///   <c>.Metadata[</c> across <c>src/</c> returned nothing. There was no path to extend.
///   </item>
/// </list>
/// <para>
/// ⚠ <b>Values arrive in the editor value-currency contract</b> documented on
/// <see cref="IEditorValue"/> — <see langword="null"/>, <see cref="bool"/>, <see cref="string"/>,
/// <see cref="long"/>, <see cref="double"/>, <c>IReadOnlyList&lt;object?&gt;</c> or
/// <c>IReadOnlyDictionary&lt;string, object?&gt;</c>. A predicate must never expect a
/// <c>JsonNode</c>: raw serialization types do not reach this far, so a rule written against one
/// silently never fires.
/// </para>
/// </remarks>
public interface IDangerClassifier
{
    /// <summary>
    /// Assess one property path, at one scope, holding one value.
    /// </summary>
    /// <param name="path">
    /// Dotted settings-tree path, matching <see cref="IEditorSchema.Path"/>.
    /// </param>
    /// <param name="scope">
    /// The scope being written or read, or <see langword="null"/> when the caller has no scope in
    /// hand (an effective-value view before a winner is known). A <see langword="null"/> scope
    /// must never *raise* severity — an unknown scope is not evidence of danger.
    /// </param>
    /// <param name="currentValue">
    /// The value held at <paramref name="scope"/>, in the editor value currency. Pass
    /// <see langword="null"/> for "not set"; a rule that cares about the difference between unset
    /// and explicitly-null must say so, because this parameter cannot express it.
    /// </param>
    /// <returns>
    /// The assessment, or <see cref="DangerAssessment.Unremarkable"/> for a path the product has
    /// nothing to say about. Never <see langword="null"/>.
    /// </returns>
    DangerAssessment Classify(string path, IEditorScope? scope, object? currentValue);

    /// <summary>
    /// Every path this classifier has an opinion about, for coverage checks.
    /// </summary>
    /// <remarks>
    /// ⭐ <b>This exists so a schema refresh cannot silently smuggle in an unclassified
    /// setting.</b> A test compares the product's schema keys against this set and fails on any
    /// key nobody has triaged — which is the only thing that stops the table rotting behind
    /// upstream. Without it the table degrades quietly: new keys simply read as unremarkable,
    /// which is exactly the wrong default for a knob nobody has looked at.
    /// <para>
    /// Wildcard patterns (see the implementation) appear here verbatim, so a coverage test must
    /// match rather than string-compare.
    /// </para>
    /// </remarks>
    IReadOnlyCollection<string> ClassifiedPaths { get; }
}
