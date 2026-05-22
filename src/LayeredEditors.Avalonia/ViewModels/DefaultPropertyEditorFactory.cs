namespace Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

/// <summary>
/// Generic dispatch factory — creates the standard library <see cref="PropertyEditorViewModel"/>
/// for each <see cref="EditorValueType"/> via a simple switch.
/// </summary>
/// <remarks>
/// Instantiate once and keep for the lifetime of a settings surface. Recursive calls
/// for Object child editors go through <see cref="Create"/> so that subclasses
/// (e.g. <see cref="CompositePropertyEditorFactory"/>) can intercept them.
/// </remarks>
public class DefaultPropertyEditorFactory : IPropertyEditorFactory
{
    /// <inheritdoc/>
    public virtual PropertyEditorViewModel Create(
        IEditorSchema schema,
        IEditorWorkspace? workspace,
        IEditorScope editingScope,
        EditorContext? context = null)
    {
        return schema.ValueType switch
        {
            EditorValueType.Boolean => new BooleanPropertyEditorViewModel(schema, editingScope),
            EditorValueType.String => new StringPropertyEditorViewModel(schema, editingScope),
            EditorValueType.Path => new PathPropertyEditorViewModel(schema, editingScope,
                context?.BrowsePath ?? context?.BrowseFile),
            EditorValueType.Enum => new EnumPropertyEditorViewModel(schema, editingScope),
            EditorValueType.Integer => new NumberPropertyEditorViewModel(schema, editingScope),
            EditorValueType.Number => new NumberPropertyEditorViewModel(schema, editingScope),
            EditorValueType.StringArray => new StringArrayPropertyEditorViewModel(schema, editingScope),
            EditorValueType.Object => CreateObjectEditor(schema, workspace, editingScope, context),
            var _ => new StringPropertyEditorViewModel(schema, editingScope),
        };
    }

    /// <summary>
    /// Create editors for all schemas in a group, using <see cref="Create"/> for each.
    /// Subclass overrides of <see cref="Create"/> propagate automatically.
    /// </summary>
    public IReadOnlyList<PropertyEditorViewModel> CreateForGroup(
        IReadOnlyList<IEditorSchema> schemas,
        IEditorWorkspace? workspace,
        IEditorScope editingScope,
        EditorContext? context = null)
    {
        List<PropertyEditorViewModel> editors = new(schemas.Count);
        foreach (IEditorSchema s in schemas)
        {
            editors.Add(Create(s, workspace, editingScope, context));
        }

        return editors;
    }

    /// <summary>
    /// Virtual so subclasses can intercept child-property creation.
    /// Calls <see cref="Create"/> for each child so subclass overrides propagate.
    /// </summary>
    protected virtual PropertyEditorViewModel CreateObjectEditor(
        IEditorSchema schema,
        IEditorWorkspace? workspace,
        IEditorScope editingScope,
        EditorContext? context)
    {
        List<PropertyEditorViewModel> children = new(schema.Properties.Count);
        foreach (IEditorSchema child in schema.Properties)
        {
            children.Add(Create(child, workspace, editingScope, context));
        }

        return new ObjectPropertyEditorViewModel(schema, editingScope, children, workspace);
    }
}