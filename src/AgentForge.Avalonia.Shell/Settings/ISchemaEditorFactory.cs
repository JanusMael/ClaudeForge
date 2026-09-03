using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;

/// <summary>
/// Turns one schema node into the editor view-model that edits it.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam that lets a neutral settings page host a product's editors. The generic
/// leaf editors — boolean, string, number, path — are the same for every product and come from
/// the editor library. What differs is the <i>specialised</i> editors a product registers for
/// its own complex shapes, and the fallback it chooses for a shape it cannot classify.
/// </para>
/// <para>
/// Every type in the signature is already product-neutral, which is why this interface is so
/// small: <see cref="SchemaNode"/>, <see cref="ConfigScope"/> and <see cref="SettingsWorkspace"/>
/// all come from <c>AgentForge.Core</c>, and the returned view-model from the editor library.
/// The product-specific part was never the signature — only which editors get registered behind
/// it.
/// </para>
/// <para>
/// ⚠ <b>No implementation of this belongs in the shell, and the shell must never default to
/// one.</b> A settings page that silently falls back to a generic factory renders a product's
/// complex settings as raw JSON — technically correct, visibly wrong, and reported as "the
/// permissions page is broken" rather than as a missing registration.
/// </para>
/// </remarks>
public interface ISchemaEditorFactory
{
    /// <summary>
    /// Create the editor for <paramref name="schema"/>.
    /// </summary>
    /// <param name="schema">The schema node describing the property.</param>
    /// <param name="editingScope">The scope the user is currently editing.</param>
    /// <param name="browseDialog">Optional file/directory browse callback for path editors.</param>
    /// <param name="workspace">Workspace for object-type editors that need child value access.</param>
    /// <returns>A fully constructed editor view-model, never <see langword="null"/>.</returns>
    PropertyEditorViewModel Create(
        SchemaNode schema,
        ConfigScope editingScope,
        Func<Task<string?>>? browseDialog = null,
        SettingsWorkspace? workspace = null);

    /// <summary>
    /// The danger policy this factory stamps onto every editor it produces, or
    /// <see langword="null"/> for a product section that declares none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>Exposed so that a surface which is NOT an editor can classify from the same table
    /// its editors do.</b> The effective-value view is the case: it renders one row per setting
    /// without owning an editor for it, so it has to call
    /// <see cref="IDangerClassifier.Classify(string, IEditorScope?, object?)"/> itself. Reading
    /// the classifier off the factory that built the page's editors makes the two halves of that
    /// page provably the same policy; an independently-supplied classifier could differ, and the
    /// user would see one page contradict itself.
    /// </para>
    /// <para>
    /// ⛔ <b>This is not licence for search to classify.</b> Search holds neither the value nor
    /// the editing scope, so it asks the editor instead — see
    /// <see cref="LayeredEditors.Avalonia.ViewModels.IDangerAnnotatedEditor"/>. The effective view
    /// is different in kind: a row there IS a (path, winning scope, winning value) triple, and
    /// those are the three inputs classification takes.
    /// </para>
    /// </remarks>
    IDangerClassifier? Danger { get; }
}
