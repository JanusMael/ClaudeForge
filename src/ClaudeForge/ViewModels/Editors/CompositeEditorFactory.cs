using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>
/// Extends <see cref="DefaultEditorFactory"/> with a matcher-based registration
/// mechanism. Registered matchers are tried in registration order; the first match
/// wins. Unmatched nodes fall through to the inherited generic dispatch.
/// </summary>
/// <example>
/// <code>
/// var factory = new CompositeEditorFactory();
/// factory.Register(
///     s => s.Name == "hooks",
///     (s, scope) => new HooksEditorViewModel(s, scope));
/// </code>
/// </example>
public sealed class CompositeEditorFactory : DefaultEditorFactory
{
    private readonly List<Registration> _registrations = [];

    /// <summary>
    /// Register a specialized factory for schemas that satisfy <paramref name="matcher"/>.
    /// </summary>
    /// <param name="matcher">Predicate run against each schema; first match wins.</param>
    /// <param name="factory">
    /// Delegate that receives the schema and editing scope and returns the editor VM.
    /// </param>
    public void Register(
        Func<SchemaNode, bool> matcher,
        Func<SchemaNode, ConfigScope, LibVm.PropertyEditorViewModel> factory)
    {
        _registrations.Add(new Registration(matcher, factory));
    }

    /// <inheritdoc/>
    public override LibVm.PropertyEditorViewModel Create(
        SchemaNode schema,
        ConfigScope editingScope,
        Func<Task<string?>>? browseDialog = null,
        SettingsWorkspace? workspace = null)
    {
        foreach (Registration reg in _registrations)
        {
            if (reg.Matcher(schema))
            {
                return reg.Factory(schema, editingScope);
            }
        }

        return base.Create(schema, editingScope, browseDialog, workspace);
    }

    private readonly record struct Registration(
        Func<SchemaNode, bool> Matcher,
        Func<SchemaNode, ConfigScope, LibVm.PropertyEditorViewModel> Factory);
}