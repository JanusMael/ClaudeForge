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
    /// <summary>
    /// Per-field notice tagged onto the text-box fallback when the factory has no
    /// structured editor for a value's shape (Unknown / Dictionary / Complex).
    /// Drives the host's warning badge so the fallback is never SILENT — the prior
    /// behaviour let a user type a scalar into a structured slot unnoticed. A
    /// consuming app that can do better (e.g. a validated raw-JSON editor)
    /// registers a <see cref="CompositePropertyEditorFactory"/> matcher to
    /// intercept these shapes.
    /// </summary>
    public const string NoStructuredEditorNotice =
        "No structured editor matches this value's shape — it is shown as a plain "
        + "text box. Register a specialized editor factory to edit it properly.";

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
            // Unknown / Dictionary / Complex — no structured editor in this generic
            // factory. Fall back to a text box but TAG it (UnsupportedShapeNotice) so
            // the host renders a warning badge; the prior bare String editor was a
            // SILENT mis-edit hazard (a scalar typed into a structured slot). A
            // consumer that can do better registers a CompositePropertyEditorFactory
            // matcher to intercept these shapes.
            var _ => new StringPropertyEditorViewModel(schema, editingScope)
            {
                UnsupportedShapeNotice = NoStructuredEditorNotice,
            },
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