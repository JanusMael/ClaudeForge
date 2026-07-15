using Bennewitz.Ninja.LayeredEditors.Abstractions;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>
/// Editor for the <c>model</c> property. A free-form enum (so a custom id can still be
/// typed) that ALSO carries the rich, friendly <see cref="ModelSuggestionItem"/> list — so
/// the Model &amp; Effort page shows the SAME picker as Essentials (brand names, fuzzy
/// matching, chevron) instead of a bare list of raw hyphenated ids.
/// </summary>
/// <remarks>
/// Derives from the generic enum editor so everything else (value load/save, scope badges,
/// reset) is inherited unchanged; only the suggestion source is enriched. Because it is a
/// SUBCLASS, its DataTemplate must be declared BEFORE the base
/// <c>EnumPropertyEditorViewModel</c> template in <c>PropertyEditorWrapper</c> — templates
/// match in declaration order and the base template would otherwise win.
/// </remarks>
public sealed class ModelPropertyEditorViewModel : LibVm.EnumPropertyEditorViewModel
{
    public ModelPropertyEditorViewModel(
        IEditorSchema schema,
        IEditorScope editingScope,
        IReadOnlyList<ModelSuggestionItem> suggestions)
        : base(schema, editingScope)
    {
        ModelSuggestions = suggestions;
    }

    /// <summary>Friendly suggestions (brand label + committed id) shown in the picker.</summary>
    public IReadOnlyList<ModelSuggestionItem> ModelSuggestions { get; }
}
