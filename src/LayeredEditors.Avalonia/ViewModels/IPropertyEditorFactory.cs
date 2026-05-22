namespace Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

/// <summary>
/// Creates a <see cref="PropertyEditorViewModel"/> for a given schema / workspace pair.
/// Implement to provide custom editors for specific schemas (e.g. complex domain objects).
/// </summary>
public interface IPropertyEditorFactory
{
    /// <summary>
    /// Create an editor ViewModel for <paramref name="schema"/> at <paramref name="editingScope"/>.
    /// </summary>
    /// <param name="schema">Schema describing the property to edit.</param>
    /// <param name="workspace">
    /// Workspace providing per-scope values and accepting mutations.
    /// Pass <c>null</c> for object-type editors if child values are not needed.
    /// </param>
    /// <param name="editingScope">The scope the user is currently editing.</param>
    /// <param name="context">
    /// Optional context supplying UI callbacks (file/directory browse dialogs).
    /// </param>
    /// <returns>A fully constructed editor ViewModel, never <c>null</c>.</returns>
    PropertyEditorViewModel Create(
        IEditorSchema schema,
        IEditorWorkspace? workspace,
        IEditorScope editingScope,
        EditorContext? context = null);
}