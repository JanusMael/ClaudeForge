using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
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
    /// <remarks>
    /// <para>
    /// ⭐ <b>The ONE place the danger policy is attached, for every editor this app produces.</b>
    /// The alternative — attaching inside each arm — means a registration added later silently
    /// ships with no severity, which is indistinguishable from "this product says nothing is
    /// dangerous here". Nobody files a bug about a page that looks calm.
    /// </para>
    /// <para>
    /// ⚠ <b>It has to be here and not in <see cref="DefaultEditorFactory.Create"/>.</b> A
    /// registration match returns without ever calling <c>base.Create</c>, so an attach in the
    /// base would cover the generic editors and miss every specialised one — the hooks page, the
    /// permissions page, the MCP page: exactly the surfaces where the dangerous keys live.
    /// </para>
    /// <para>
    /// Child editors reach this too: <c>CreateChildEditors</c> calls the virtual <c>Create</c>, so
    /// it dispatches back here for each child and each gets its own attach.
    /// </para>
    /// </remarks>
    public override LibVm.PropertyEditorViewModel Create(
        SchemaNode schema,
        ConfigScope editingScope,
        Func<Task<string?>>? browseDialog = null,
        SettingsWorkspace? workspace = null) =>
        CreateUnattached(schema, editingScope, browseDialog, workspace)
            .AttachDangerClassifier(Danger);

    /// <summary>Pick the editor for this schema — a registration if one matches, else the base.</summary>
    private LibVm.PropertyEditorViewModel CreateUnattached(
        SchemaNode schema,
        ConfigScope editingScope,
        Func<Task<string?>>? browseDialog,
        SettingsWorkspace? workspace)
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