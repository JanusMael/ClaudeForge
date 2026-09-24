using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using LibVm = Bennewitz.Ninja.ScopedEditors.ViewModels;

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
public class ObjectPropertyEditorViewModel : PropertyEditorViewModel, LibVm.IChildEditorHost
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

    /// <summary>
    /// Keys found in this object AT THE EDITING SCOPE that no child models — captured on
    /// load, re-emitted verbatim by <see cref="ToJsonValue"/>. <c>null</c> when the scope
    /// holds nothing this editor cannot account for, which is the ordinary case.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ Without this, editing ONE modelled key deletes every unmodelled key in the same
    /// object, because <see cref="ToJsonValue"/> rebuilds the object from
    /// <see cref="Children"/> and the writer diffs that against the on-disk baseline — so a
    /// key with no child reads as a key the user removed. Measured on <c>env</c>, where the
    /// schema models ~157 names harvested from upstream descriptions and everything else
    /// (proxy settings, internal tool paths, anything an organisation adds) is unmodelled:
    /// changing one variable deleted the rest.
    /// </para>
    /// <para>
    /// An editor may decline to RENDER a key it does not understand. It must not delete
    /// what it chose not to show. This is also what the round-trip contract in the editors'
    /// <c>AGENTS.md</c> §7 already required — load-then-save must reproduce the value it
    /// was given, and an object with unmodelled keys did not.
    /// </para>
    /// <para>
    /// Only the editing scope is captured, because that is the only scope this editor
    /// writes. Comparison is ORDINAL, matching the projection in
    /// <see cref="LoadFromLayered"/> — JSON keys are case-sensitive, so a differently-cased
    /// near-match is a genuinely different key and is preserved as one.
    /// </para>
    /// </remarks>
    private JsonObject? _unmodelledAtEditingScope;

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

        // Re-emit what this editor never rendered. Appending rather than interleaving is
        // safe for file layout: JsoncEditWriter diffs this object against the load-time
        // baseline per path, so a key that comes back unchanged produces NO edit at all
        // and keeps its original position, comments and spacing.
        if (_unmodelledAtEditingScope is not null)
        {
            foreach (KeyValuePair<string, JsonNode?> kv in _unmodelledAtEditingScope)
            {
                if (!obj.ContainsKey(kv.Key))
                {
                    obj[kv.Key] = kv.Value?.DeepClone();
                }
            }
        }

        return obj.Count > 0 ? obj : null;
    }

    public override void LoadFromLayered(LayeredValue layered, ConfigScope editingScope)
    {
        SetScopeState(layered, editingScope);

        // Before projecting anything, remember what this editor is about to ignore.
        _unmodelledAtEditingScope = CaptureUnmodelled(layered, editingScope);

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
            child.LoadFromValue(new LayeredValueAdapter(childLayered), ConfigScopeAdapter.For(editingScope));
        }

        // Derive IsModified from children's actual loaded state rather than from the
        // top-level object key existing. This keeps the parent flag honest: if a child
        // has a value at this scope, the parent is modified; if no child has a value,
        // the parent is not modified (and Reset has nothing to do).
        IsModified = Children.Any(c => c.IsModified);
    }

    /// <summary>
    /// The editing scope's raw object, minus every key some child already models.
    /// </summary>
    private JsonObject? CaptureUnmodelled(LayeredValue layered, ConfigScope editingScope)
    {
        JsonObject? atScope = layered.Entries
                                     .Where(e => e.Scope == editingScope)
                                     .Select(e => e.Value as JsonObject)
                                     .FirstOrDefault(o => o is not null);
        if (atScope is null)
        {
            return null;
        }

        HashSet<string> modelled = new(Children.Select(c => c.Schema.Name), StringComparer.Ordinal);
        JsonObject? kept = null;
        foreach (KeyValuePair<string, JsonNode?> kv in atScope)
        {
            if (modelled.Contains(kv.Key))
            {
                continue;
            }

            kept ??= [];
            kept[kv.Key] = kv.Value?.DeepClone();
        }

        return kept;
    }

    /// <remarks>
    /// ⚠ Resetting DOES drop the unmodelled keys, and that is the intended reading: reset
    /// means "remove this property at this scope", an explicit destructive act on the whole
    /// object. Preserving what the reset was asked to clear would leave a property the user
    /// believes they deleted.
    /// </remarks>
    protected override void OnResetToInherited()
    {
        foreach (LibVm.PropertyEditorViewModel child in Children)
        {
            child.ResetToInheritedCommand.Execute(null);
        }

        // Drop the carried keys too, or ToJsonValue would keep returning a non-null object
        // and the property would survive a reset that emptied every field the user can see.
        _unmodelledAtEditingScope = null;
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