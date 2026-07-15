using System.ComponentModel;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>
/// Editor for object properties. Renders child property editors recursively.
/// Used for nested objects that have known sub-properties in the schema.
/// </summary>
/// <remarks>
/// <para>
/// Child editors are created by the factory and passed in at construction time.
/// This VM subscribes to each child's <see cref="PropertyEditorViewModel.IsModified"/>
/// change and propagates it upward so that <see cref="SettingsGroupEditorViewModel"/>
/// hears every nested edit — not just top-level ones.
/// </para>
/// <para>
/// The propagation always force-fires <c>PropertyChanged("IsModified")</c> even when
/// the parent's own <c>IsModified</c> flag does not change (e.g. a second child is
/// modified while the first was already modified). Without the force-fire,
/// CommunityToolkit.Mvvm's <c>[ObservableProperty]</c> setter would suppress the event
/// and the group editor would never write the updated value to the workspace.
/// </para>
/// </remarks>
public class ObjectPropertyEditorViewModel : PropertyEditorViewModel
{
    /// <summary>
    /// Only objects with MORE than this many children render as collapsible, name-prefix
    /// CATEGORIES (see <see cref="Categories"/>); anything at or below renders as a plain
    /// inline list exactly as it always did.
    /// <para>
    /// Deliberately high. It was the FULL set (<c>env</c>'s ~305 vars) that made the
    /// Environment page take seconds; a ~150-child list renders acceptably inline. So this
    /// must not impose an accordion on mid-sized objects (e.g. <c>sandbox</c>'s 35) that
    /// were perfectly fine before — the accordion is reserved for the pathological case.
    /// </para>
    /// </summary>
    private const int InlineChildThreshold = 150;

    /// <summary>Minimum children a name-prefix must have to earn its own category.</summary>
    private const int MinCategoryMembers = 5;

    public ObjectPropertyEditorViewModel(SchemaNode schema, ConfigScope editingScope,
                                         IReadOnlyList<LibVm.PropertyEditorViewModel> children,
                                         SettingsWorkspace? workspace = null)
        : base(schema, editingScope)
    {
        Children = children;
        IsCollapsible = children.Count > InlineChildThreshold;
        Categories = IsCollapsible ? BuildCategories(children) : [];
        // workspace parameter kept for API compatibility but not stored —
        // LoadFromLayered extracts child values from the parent LayeredValue
        // rather than querying the workspace directly (see LoadFromLayered comment).
        _ = workspace;

        // Subscribe to children so nested edits bubble up to the group editor.
        // Children have the same lifetime as this VM (created and discarded together
        // during RebuildEditors), so no explicit unsubscription is needed.
        foreach (LibVm.PropertyEditorViewModel child in Children)
        {
            child.PropertyChanged += OnChildPropertyChanged;
        }
    }

    public IReadOnlyList<LibVm.PropertyEditorViewModel> Children { get; }

    /// <summary>True when this object is large enough to render as collapsible categories.</summary>
    public bool IsCollapsible { get; }

    /// <summary>
    /// For a large (collapsible) object: its children bucketed into name-prefix categories
    /// (e.g. <c>CLAUDE</c>, <c>OTEL</c>, <c>Other</c>), each a collapsible + virtualized
    /// section that realizes nothing until expanded. Empty for small objects (inline). When
    /// prefix-grouping doesn't split the object into useful buckets, this is a single
    /// <c>All</c> category, so the large object still renders as one bounded, virtualized,
    /// collapsed section rather than an easy-to-miss toggle.
    /// </summary>
    public IReadOnlyList<PropertyCategoryViewModel> Categories { get; }

    public override JsonNode? ToJsonValue()
    {
        JsonObject obj = new();
        foreach (LibVm.PropertyEditorViewModel child in Children)
        {
            // Phase 2.1 step 3b — children are typed as the library base, so call
            // the library API (ToValue → currency → JsonNode). App-bridge subclasses
            // route through ToValue → Normalise(ToJsonValue()), so legacy overrides
            // still flow through their existing ToJsonValue path.
            JsonNode? val = JsonCurrency.ToJsonNode(child.ToValue());
            if (val != null)
            {
                obj[child.Schema.Name] = val;
            }
        }

        return obj.Count > 0 ? obj : null;
    }

    public override void LoadFromLayered(LayeredValue layered, ConfigScope editingScope)
    {
        SetScopeState(layered, editingScope);

        // Extract each child's value from the parent's per-scope entries directly.
        //
        // Do NOT use _workspace.GetLayeredValue(child.Path) here.  The workspace
        // only indexes top-level JSON keys; a nested dot-path such as
        // "preferences.coworkScheduledTasksEnabled" is not a top-level key in doc.Root,
        // so GetLayeredValue returns an empty LayeredValue for every child property.
        // With all children returning null from ToValue(), the parent's ToJsonValue()
        // also returns null, which causes ApplyToWorkspace to call
        // RemoveValue("preferences", scope) — silently destroying the user's nested data.
        foreach (LibVm.PropertyEditorViewModel child in Children)
        {
            string childName = child.Schema.Name;

            // Project each scope's parent JsonObject down to the child's named property.
            List<ScopeEntry> childEntries = layered.Entries
                                                   .Select(e => (e.Scope, Value: (e.Value as JsonObject)?[childName]?.DeepClone(),
                                                       e.SourceFilePath))
                                                   .Where(t => t.Value is not null)
                                                   .Select(t => new ScopeEntry(t.Scope, t.Value!, t.SourceFilePath))
                                                   .ToList();

            // Entries are ordered highest-priority first (lowest scope number).
            // For non-array scalars, the first entry is the effective value.
            LayeredValue childLayered = new(child.Path, childEntries)
            {
                EffectiveValue = childEntries.Count > 0 ? childEntries[0].Value : null,
                EffectiveScope = childEntries.Count > 0 ? childEntries[0].Scope : null,
            };

            // Phase 2.1 step 3b — use the library API uniformly. App-bridge
            // LoadFromValue routes through legacy LoadFromLayered overrides;
            // migrated leaves implement LoadFromValue directly.
            child.LoadFromValue(new ClaudeValueAdapter(childLayered), ClaudeScope.For(editingScope));
        }

        // Derive IsModified from children's actual loaded state rather than from the
        // top-level object key existing. This keeps the parent flag honest: if a child
        // has a value at this scope, the parent is modified; if no child has a value,
        // the parent is not modified (and Reset has nothing to do).
        IsModified = Children.Any(c => c.IsModified);
    }

    protected override void OnResetToInherited()
    {
        foreach (LibVm.PropertyEditorViewModel child in Children)
        {
            child.ResetToInheritedCommand.Execute(null);
        }
    }

    // -----------------------------------------------------------------------
    // Child change propagation
    // -----------------------------------------------------------------------

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(IsModified))
        {
            return;
        }

        bool wasModified = IsModified;
        IsModified = Children.Any(c => c.IsModified);

        // Force-fire even when IsModified stays true so that SettingsGroupEditorViewModel
        // always re-invokes ToJsonValue() and writes the complete updated object to the
        // workspace. Without this, changing a second child while the first was already
        // modified would be silently dropped — CommunityToolkit suppresses PropertyChanged
        // when the bool value does not change.
        if (wasModified == IsModified)
        {
            OnPropertyChanged(nameof(IsModified));
        }
    }

    /// <summary>
    /// Bucket <paramref name="children"/> by the leading token of their name (up to the
    /// first <c>'_'</c>, e.g. <c>CLAUDE</c>, <c>OTEL</c>). A prefix with at least
    /// <see cref="MinCategoryMembers"/> members becomes its own category (ordered
    /// alphabetically); everything else — short prefixes, singletons, and names with no
    /// underscore — pools into a trailing <c>Other</c>. When no prefix qualifies (no useful
    /// structure, e.g. sandbox's camelCase keys) the whole object collapses into a single
    /// <c>All</c> category, so it still renders as one bounded, virtualized, collapsed
    /// section instead of an easy-to-miss toggle.
    /// </summary>
    private static IReadOnlyList<PropertyCategoryViewModel> BuildCategories(
        IReadOnlyList<LibVm.PropertyEditorViewModel> children)
    {
        Dictionary<string, List<LibVm.PropertyEditorViewModel>> byPrefix = new(StringComparer.Ordinal);
        foreach (LibVm.PropertyEditorViewModel child in children)
        {
            string name = child.Schema.Name;
            int underscore = name.IndexOf('_');
            string prefix = underscore > 0 ? name[..underscore] : string.Empty;
            if (!byPrefix.TryGetValue(prefix, out List<LibVm.PropertyEditorViewModel>? bucket))
            {
                bucket = new List<LibVm.PropertyEditorViewModel>();
                byPrefix[prefix] = bucket;
            }

            bucket.Add(child);
        }

        List<PropertyCategoryViewModel> categories = new();
        List<LibVm.PropertyEditorViewModel> other = new();
        foreach (KeyValuePair<string, List<LibVm.PropertyEditorViewModel>> kv in byPrefix)
        {
            if (kv.Key.Length > 0 && kv.Value.Count >= MinCategoryMembers)
            {
                // Name is the bare prefix ("CLAUDE", not "CLAUDE_") — the trailing
                // separator is noise in a section header.
                categories.Add(new PropertyCategoryViewModel(kv.Key, kv.Value));
            }
            else
            {
                other.AddRange(kv.Value);
            }
        }

        categories.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        // No prefix reached the threshold → one bounded "All" section (still virtualized,
        // still collapsed) rather than a pile of singleton categories.
        if (categories.Count == 0)
        {
            return [new PropertyCategoryViewModel("All", children)];
        }

        if (other.Count > 0)
        {
            categories.Add(new PropertyCategoryViewModel("Other", other));
        }

        return categories;
    }
}