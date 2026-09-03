namespace Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

/// <summary>
/// An editor that can answer "how much does the setting at this path matter, and is it wrong
/// right now?" for any path it renders — including paths nested inside it.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>This exists so a second surface cannot disagree with the settings row.</b> A search hit
/// and a settings row are two views of the same knob, and the obvious implementation — hand the
/// search view-model a classifier and let it call
/// <see cref="IDangerClassifier.Classify(string, IEditorScope?, object?)"/> itself — quietly
/// reclassifies. Search holds no value and no editing scope, so it would have to pass
/// <see langword="null"/> for both, and the assessment is a function of all three: the same
/// <c>apiKey</c> is Caution at a user-global scope and Critical in a project file, and
/// <c>IsDangerNow</c> is meaningless without the value. The result would be a dot on the hit that
/// contradicts the dot on the row it navigates to.
/// </para>
/// <para>
/// Asking the editor instead returns the assessment the row is <i>already</i> rendering — the same
/// <see cref="DangerAssessment"/> instance, from the same classifier, at the page's real editing
/// scope, over the editor's current (possibly unsaved) value. Agreement is structural rather than
/// tested-for.
/// </para>
/// <para>
/// It lives in the library rather than the shell for the same reason
/// <see cref="IChildEditorHost"/> does: <see cref="PropertyEditorViewModel"/> implements it, and
/// the library cannot reference the shell. The shell's own group editor — which holds a list of
/// editors rather than being one — implements it too.
/// </para>
/// </remarks>
public interface IDangerAnnotatedEditor
{
    /// <summary>
    /// The assessment for <paramref name="jsonPath"/>, or <see langword="null"/> when this editor
    /// does not render that path at all.
    /// </summary>
    /// <remarks>
    /// ⚠ <b><see langword="null"/> and <see cref="DangerAssessment.Unremarkable"/> are different
    /// answers and the distinction is load-bearing</b>, even though both render as no dot.
    /// <see langword="null"/> means "not mine, keep looking", which is what lets a caller walk a
    /// list of editors and stop at the first owner; <c>Unremarkable</c> means "mine, and the
    /// product triaged it as nothing notable". Collapsing them makes the walk stop at the first
    /// editor it asks and report every other path as safe.
    /// </remarks>
    DangerAssessment? AssessDanger(string jsonPath);
}
