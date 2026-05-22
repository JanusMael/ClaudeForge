namespace Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

/// <summary>
/// Extends <see cref="DefaultPropertyEditorFactory"/> with a matcher-based registration
/// mechanism. Registered matchers are tried in registration order; the first match wins.
/// Unmatched schemas fall through to the inherited generic dispatch.
/// </summary>
/// <example>
/// <code>
/// var factory = new CompositePropertyEditorFactory();
/// factory.Register(
///     s => s.Name == "myComplexProp",
///     (s, ws, scope, ctx) => new MyComplexEditorViewModel(s, scope));
/// </code>
/// </example>
public sealed class CompositePropertyEditorFactory : DefaultPropertyEditorFactory
{
    private readonly List<Registration> _registrations = [];

    /// <summary>
    /// Register a specialized factory for schemas that satisfy <paramref name="matcher"/>.
    /// </summary>
    /// <param name="matcher">Predicate run against each schema; first match wins.</param>
    /// <param name="factory">
    /// Delegate that receives the schema, workspace, editing scope, and context,
    /// and returns the editor ViewModel.
    /// </param>
    public void Register(
        Func<IEditorSchema, bool> matcher,
        Func<IEditorSchema, IEditorWorkspace?, IEditorScope, EditorContext?, PropertyEditorViewModel> factory)
    {
        _registrations.Add(new Registration(matcher, factory));
    }

    /// <inheritdoc/>
    public override PropertyEditorViewModel Create(
        IEditorSchema schema,
        IEditorWorkspace? workspace,
        IEditorScope editingScope,
        EditorContext? context = null)
    {
        foreach (Registration reg in _registrations)
        {
            if (reg.Matcher(schema))
            {
                return reg.Factory(schema, workspace, editingScope, context);
            }
        }

        return base.Create(schema, workspace, editingScope, context);
    }

    private readonly record struct Registration(
        Func<IEditorSchema, bool> Matcher,
        Func<IEditorSchema, IEditorWorkspace?, IEditorScope, EditorContext?, PropertyEditorViewModel> Factory);
}