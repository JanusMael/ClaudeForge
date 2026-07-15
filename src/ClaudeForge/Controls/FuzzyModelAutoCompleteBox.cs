using Avalonia.Controls;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Controls;

/// <summary>
/// An <see cref="AutoCompleteBox"/> pre-wired with <see cref="ModelSuggestionFilter"/>
/// for the Essentials model picker. Typing a friendly fragment ("Opus", "opus 4",
/// "sonnet") surfaces every matching suggestion — the id (<c>claude-opus-4-8</c>), the
/// alias (<c>opus</c>), the <c>[1m]</c> variant, and the brand label (<c>Opus 4.8</c>) —
/// because the filter matches against BOTH the item's value and its label.
/// <para>
/// The filter is a delegate (not settable in XAML), so baking it into a control lets
/// the templated picker use it declaratively. Setting <see cref="AutoCompleteBox.ItemFilter"/>
/// supersedes <see cref="AutoCompleteBox.FilterMode"/>.
/// </para>
/// </summary>
public sealed class FuzzyModelAutoCompleteBox : AutoCompleteBox
{
    // A subclassed templated control gets its OWN type as its style key by default, so
    // the styling system finds no ControlTheme for FuzzyModelAutoCompleteBox and the
    // control renders blank (no template — the symptom: the Essentials model card showed
    // its title/description but no input box). Point the style key at the base type so it
    // reuses AutoCompleteBox's ControlTheme (input box + suggestion popup); the baked-in
    // fuzzy ItemFilter below is unaffected.
    protected override Type StyleKeyOverride => typeof(AutoCompleteBox);

    public FuzzyModelAutoCompleteBox()
    {
        ItemFilter = (search, item) =>
        {
            string haystack = item switch
            {
                ModelSuggestionItem m => $"{m.Value} {m.Label} {m.Detail}",
                string s => s,
                _ => item?.ToString() ?? string.Empty,
            };

            return ModelSuggestionFilter.Matches(search, haystack);
        };
    }
}
