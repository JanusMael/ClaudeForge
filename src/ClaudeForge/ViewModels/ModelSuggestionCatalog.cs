using Bennewitz.Ninja.ClaudeForge.Core.Catalog;
using Bennewitz.Ninja.ClaudeForge.Sdk.Models;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// Builds the model picker's rich suggestion list from the model catalog. Shared so the
/// Essentials card and the Model &amp; Effort property editor show the SAME friendly
/// entries — previously Essentials had friendly labels while the property editor showed
/// only the raw hyphenated ids.
/// </summary>
public static class ModelSuggestionCatalog
{
    /// <summary>
    /// For each non-legacy catalogued model, an entry for its alias, its full id, and (when
    /// supported) the <c>[1m]</c> variant — each carrying the brand <see cref="ModelInfo.Label"/>
    /// so the picker displays a friendly name ("Opus 4.8") while committing a valid id, and the
    /// fuzzy filter can match either. Order mirrors <c>IModelCatalogAccessor.ModelSuggestions()</c>.
    /// </summary>
    public static IReadOnlyList<ModelSuggestionItem> Build(IModelCatalogAccessor catalog)
    {
        List<ModelSuggestionItem> items = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        void Add(string value, string label, string? detail)
        {
            if (seen.Add(value))
            {
                items.Add(new ModelSuggestionItem(value, label, detail));
            }
        }

        foreach (ModelInfo m in catalog.AllModels)
        {
            if (m.Legacy)
            {
                continue;
            }

            string primary = m.Alias ?? m.Id;
            if (m.Alias is not null)
            {
                Add(m.Alias, m.Label, m.Alias);
            }

            Add(m.Id, m.Label, m.Id);

            if (m.Supports1m)
            {
                Add(primary + "[1m]", m.Label + " · 1M context", primary + "[1m]");
            }
        }

        return items;
    }

    /// <summary>Convenience overload using the app-wide default catalog.</summary>
    public static IReadOnlyList<ModelSuggestionItem> Build()
    {
        return Build(ModelCatalogProvider.Default);
    }
}
