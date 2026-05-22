using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.JsonHelpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>All hooks registered for one event type (e.g. "PreToolUse").</summary>
public partial class HookEventGroup : ObservableObject
{
    public HookEventGroup(string eventName)
    {
        EventName = eventName;
        Hooks = [];
        Hooks.CollectionChanged += OnHooksCollectionChanged;
    }

    public string EventName { get; }
    public ObservableCollection<HookEntry> Hooks { get; }

    [ObservableProperty] private HookEntry? _selectedHook;
    [ObservableProperty] private bool _isExpanded;

    /// <summary>
    /// True when at least one hook in this group has an empty Matcher or empty CommandValue.
    /// Those hooks are silently skipped during serialisation and will not fire.
    /// Shown as a warning banner above the DataGrid so the user knows something needs filling in.
    /// </summary>
    public bool HasAnyHookWithWarning => Hooks.Any(h => h.HasValidationWarning);

    private void OnHooksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Subscribe to PropertyChanged on newly-added entries so HasAnyHookWithWarning
        // updates when the user types in an existing row (not just when rows are added/removed).
        if (e.NewItems != null)
        {
            foreach (HookEntry entry in e.NewItems)
            {
                entry.PropertyChanged += OnHookEntryPropertyChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (HookEntry entry in e.OldItems)
            {
                entry.PropertyChanged -= OnHookEntryPropertyChanged;
            }
        }

        OnPropertyChanged(nameof(HasAnyHookWithWarning));
    }

    private void OnHookEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HookEntry.HasValidationWarning) or
            nameof(HookEntry.Matcher) or
            nameof(HookEntry.CommandValue))
        {
            OnPropertyChanged(nameof(HasAnyHookWithWarning));
        }
    }

    [RelayCommand]
    private void AddHook()
    {
        HookEntry entry = new();
        Hooks.Add(entry);
        SelectedHook = entry;
        IsExpanded = true;
    }

    [RelayCommand]
    private void RemoveHook(HookEntry? entry)
    {
        if (entry == null)
        {
            return;
        }

        Hooks.Remove(entry);
        SelectedHook = Hooks.FirstOrDefault();
    }

    /// <summary>
    /// Parse the Claude Code hooks shape:
    /// <code>[{ "matcher": "Bash", "hooks": [{ "type": "command", "command": "..." }] }]</code>
    /// Each inner hook becomes a separate <see cref="HookEntry"/> row with the outer matcher
    /// threaded through. Also tolerates a legacy flat shape where command/prompt/url sit on the
    /// outer object directly (older schemas / hand-edited files).
    /// </summary>
    public static HookEventGroup FromJson(string eventName, JsonNode? node)
    {
        HookEventGroup group = new(eventName);
        if (node is JsonArray arr)
        {
            foreach (JsonNode? item in arr)
            {
                if (item is JsonObject obj)
                {
                    AddFromOuterObject(group, obj);
                }
            }
        }
        else if (node is JsonObject single)
        {
            AddFromOuterObject(group, single);
        }

        if (group.Hooks.Count > 0)
        {
            group.SelectedHook = group.Hooks[0];
        }

        return group;
    }

    private static void AddFromOuterObject(HookEventGroup group, JsonObject outer)
    {
        string matcher = outer["matcher"].AsStringOrNull() ?? string.Empty;

        // Preferred shape: outer has a nested "hooks" array; each inner item carries the
        // type/command/prompt/url pair.
        if (outer["hooks"] is JsonArray inner && inner.Count > 0)
        {
            foreach (JsonNode? child in inner)
            {
                if (child is JsonObject childObj)
                {
                    HookEntry entry = HookEntry.FromJson(childObj);
                    entry.Matcher = matcher;
                    group.Hooks.Add(entry);
                }
            }

            return;
        }

        // Legacy/flat shape: the outer object itself carries command/prompt/url.
        group.Hooks.Add(HookEntry.FromJson(outer));
    }

    /// <summary>
    /// Emit the nested Claude Code shape, grouping entries by matcher so round-tripping
    /// preserves the on-disk layout.
    /// Entries with an empty <see cref="HookEntry.CommandValue"/> are skipped —
    /// they would produce an invalid <c>{"type":"command"}</c> object with no value key.
    /// </summary>
    public JsonArray ToJson()
    {
        JsonArray arr = new();
        // Emit ALL non-opaque hooks, including those with an
        // empty CommandValue.  The previous behaviour (drop empty entries)
        // made 'add hook' produce no JSON change relative to baseline, so
        // the save-button structural-diff gate stayed disabled — the user
        // saw no signal that they had pending work.  An empty CommandValue
        // hook serializes as { type: "command", command: "" } which IS
        // schema-invalid (minLength:1 on the command field) — but the
        // save-time validation banner surfaces that clearly.  IsOpaque
        // hooks (unrecognised types like agent/http) round-trip verbatim;
        // they don't have a CommandValue concept and need explicit
        // inclusion.
        List<HookEntry> validHooks = Hooks.ToList();
        foreach (IGrouping<string, HookEntry> byMatcher in validHooks.GroupBy(h => h.Matcher ?? string.Empty))
        {
            JsonObject outer = new();
            if (!string.IsNullOrEmpty(byMatcher.Key))
            {
                outer["matcher"] = byMatcher.Key;
            }

            JsonArray innerArr = new();
            foreach (HookEntry entry in byMatcher)
            {
                innerArr.Add((JsonNode?)entry.ToJson());
            }

            outer["hooks"] = innerArr;

            arr.Add((JsonNode?)outer);
        }

        return arr;
    }
}