namespace Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

/// <summary>
/// An editor that contains other editors, and can therefore be descended into.
/// </summary>
/// <remarks>
/// <para>
/// Filtering a settings page needs to look inside object editors: typing a nested property
/// name should reveal that one child, not the whole parent object with every sibling. Doing
/// that requires knowing which editors have children.
/// </para>
/// <para>
/// ⚠ <b>This exists because there are TWO <c>ObjectPropertyEditorViewModel</c> types</b> — one
/// here in the library and one in the app, and the app's does <i>not</i> derive from this one.
/// A type test against either class therefore matches only half the object editors in play, and
/// the symptom is silent: the filter simply stops descending into the other half, showing a
/// collapsed parent instead of the child the user typed. An interface both implement is the only
/// test that covers both.
/// </para>
/// <para>
/// It lives in the library rather than the shell because the library's own object editor has to
/// implement it, and the library cannot reference the shell.
/// </para>
/// </remarks>
public interface IChildEditorHost
{
    /// <summary>The contained editors, in display order.</summary>
    IReadOnlyList<PropertyEditorViewModel> Children { get; }
}
