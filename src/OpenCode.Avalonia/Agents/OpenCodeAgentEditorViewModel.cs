using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Sdk.Agents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Agents;

/// <summary>
/// Editor for OpenCode's <c>agent</c> setting: a map of agent name → fifteen optional fields,
/// including a nested permission override.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not the generic object editor.</b> The value is an object whose every entry is an
/// <c>AgentConfig</c>, and the generic dispatch would render each as raw JSON — fifteen fields, a
/// deprecated sub-map, and a nested permission union, all as text. It also cannot express the one
/// thing that decides whether an edit is safe: that an entry is a <b>partial override</b>, so a
/// control left alone must write nothing at all.
/// </para>
/// <para>
/// ⭐ <b>Implements <see cref="IChildEditorHost"/>, and that is a real payoff rather than
/// bookkeeping.</b> Filtering a settings page descends into child editors, so typing a permission
/// pattern now finds the agent whose override contains it — without this, the filter stops at the
/// collapsed <c>agent</c> row and the nested rules are unreachable by search.
/// </para>
/// <para>
/// Follows the compound-editor contract in <c>src/ClaudeForge/ViewModels/Editors/AGENTS.md</c>:
/// force-fire <c>MarkModified</c>, an <c>_isLoading</c> guard, <see langword="null"/> when empty,
/// transient input fields filtered, and nested collections subscribed <i>including the rows already
/// in them at hook time</i>.
/// </para>
/// </remarks>
public sealed partial class OpenCodeAgentEditorViewModel : PropertyEditorViewModel, IChildEditorHost
{
    private bool _isLoading;
    private IEditorValue? _lastValue;
    private IEditorScope? _lastScope;
    private string _globalPermissionSummary = string.Empty;

    /// <summary>Creates the editor for <paramref name="schema"/>.</summary>
    public OpenCodeAgentEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
        Agents = [];
        Agents.CollectionChanged += OnAgentsChanged;
    }

    /// <summary>Agent entries, in file order.</summary>
    public ObservableCollection<OpenCodeAgentViewModel> Agents { get; }

    /// <summary>The seven overridable built-in names, offered as add-box suggestions.</summary>
    public static IReadOnlyList<string> BuiltInNames { get; } =
        [.. OpenCodeBuiltInAgents.Names.Order(StringComparer.Ordinal)];

    /// <inheritdoc />
    /// <remarks>
    /// The nested permission grids. Rebuilt on demand rather than cached, because an override can
    /// be added or removed at any time and a stale list would hide a child from the filter — or
    /// keep offering one that no longer exists.
    /// </remarks>
    public IReadOnlyList<PropertyEditorViewModel> Children =>
        [.. Agents.Select(a => a.Permission).OfType<PropertyEditorViewModel>()];

    /// <summary>Name for the add box. Transient — never marks the editor modified.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddAgentCommand))]
    private string _newAgentName = string.Empty;

    /// <summary>Add an agent from <see cref="NewAgentName"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanAddAgent))]
    private void AddAgent()
    {
        string name = NewAgentName.Trim();
        if (name.Length == 0
            || Agents.Any(a => string.Equals(a.Name, name, StringComparison.Ordinal)))
        {
            return;
        }

        OpenCodeAgentViewModel entry = NewAgent(name);
        entry.Load(new OpenCodeAgentConfig(), EditingScope, _globalPermissionSummary);
        Agents.Add(entry);
        NewAgentName = string.Empty;
    }

    private bool CanAddAgent() => !string.IsNullOrWhiteSpace(NewAgentName);

    private OpenCodeAgentViewModel NewAgent(string name) =>
        new(name, onRemove: entry => Agents.Remove(entry));

    /// <inheritdoc />
    public override object? ToValue() => OpenCodeAgentCodec.WriteMap(BuildEntries());

    private List<KeyValuePair<string, OpenCodeAgentConfig>> BuildEntries()
    {
        List<KeyValuePair<string, OpenCodeAgentConfig>> entries = [];
        foreach (OpenCodeAgentViewModel agent in Agents)
        {
            entries.Add(new KeyValuePair<string, OpenCodeAgentConfig>(
                agent.Name.Trim(),
                agent.ToModel()));
        }

        return entries;
    }

    /// <inheritdoc />
    public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
    {
        ArgumentNullException.ThrowIfNull(value);

        _isLoading = true;
        try
        {
            _lastValue = value;
            _lastScope = editingScope;

            EditingScope = editingScope;
            EffectiveScope = value.EffectiveScope;
            IsOverridden = value.IsOverridden;

            foreach (OpenCodeAgentViewModel entry in Agents)
            {
                UnsubscribeAgent(entry);
            }

            Agents.Clear();

            foreach ((string name, OpenCodeAgentConfig agent) in
                     OpenCodeAgentCodec.ReadMap(value.GetValueAt(editingScope)))
            {
                OpenCodeAgentViewModel entry = NewAgent(name);

                // Loaded BEFORE joining the collection, which is why SubscribeAgent must hook the
                // rows and the child grid already present rather than only future additions.
                entry.Load(agent, editingScope, _globalPermissionSummary);
                Agents.Add(entry);
            }

            IsModified = value.IsDefinedAt(editingScope);
            UpdateOtherScopesWithData(value, editingScope);
            UpdateInheritedDisplay(value, editingScope);
        }
        finally
        {
            _isLoading = false;
        }

        OnPropertyChanged(nameof(Children));
    }

    /// <summary>
    /// Supply a read-only summary of the global <c>permission</c> value, shown beside each agent's
    /// override.
    /// </summary>
    /// <remarks>
    /// ⚠ Called by the host, not read from the workspace here: this editor is handed one value and
    /// one scope, and reaching sideways for a sibling key would couple it to a workspace it is not
    /// given. The plan asks the effective view to show <i>global → agent override</i>; this is the
    /// half that belongs to the editor. Empty is a legitimate answer and renders nothing.
    /// </remarks>
    public void SetGlobalPermissionSummary(string summary)
    {
        _globalPermissionSummary = summary ?? string.Empty;
        foreach (OpenCodeAgentViewModel agent in Agents)
        {
            agent.GlobalPermissionSummary = _globalPermissionSummary;
        }
    }

    /// <inheritdoc />
    protected override void OnResetToInherited()
    {
        if (_lastValue is { } value && _lastScope is { } scope)
        {
            LoadFromValue(value, scope);
            IsModified = false;
            return;
        }

        _isLoading = true;
        try
        {
            Agents.Clear();
        }
        finally
        {
            _isLoading = false;
        }

        OnPropertyChanged(nameof(Children));
    }

    // ── Modification plumbing ────────────────────────────────────────────────

    /// <summary>
    /// Force-fire <c>PropertyChanged(IsModified)</c> on every user mutation, even when the flag was
    /// already true from the prior load.
    /// </summary>
    private void MarkModified()
    {
        if (_isLoading)
        {
            return;
        }

        if (IsModified)
        {
            OnPropertyChanged(nameof(IsModified));
        }
        else
        {
            IsModified = true;
        }
    }

    private void OnAgentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (OpenCodeAgentViewModel entry in e.OldItems)
            {
                UnsubscribeAgent(entry);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (OpenCodeAgentViewModel entry in e.NewItems)
            {
                SubscribeAgent(entry);
            }
        }

        OnPropertyChanged(nameof(Children));
        MarkModified();
    }

    /// <remarks>
    /// ⚠ Four levels: the agent's own properties, its deprecated tool list, the rows inside that
    /// list, and the <b>child permission grid</b> — whose <c>IsModified</c> is the only signal that
    /// a nested rule changed. Miss the last one and editing an agent's permission rules leaves Save
    /// disabled, which looks exactly like the editor ignoring the user.
    /// </remarks>
    private void SubscribeAgent(OpenCodeAgentViewModel entry)
    {
        entry.PropertyChanged += OnAgentPropertyChanged;
        entry.Tools.PropertyChanged += OnToolListPropertyChanged;
        entry.Tools.Rows.CollectionChanged += OnToolRowsChanged;

        foreach (OpenCodeAgentToolViewModel row in entry.Tools.Rows)
        {
            row.PropertyChanged += OnNestedRowPropertyChanged;
        }

        if (entry.Permission is { } permission)
        {
            permission.PropertyChanged += OnChildEditorPropertyChanged;
        }
    }

    private void UnsubscribeAgent(OpenCodeAgentViewModel entry)
    {
        entry.PropertyChanged -= OnAgentPropertyChanged;
        entry.Tools.PropertyChanged -= OnToolListPropertyChanged;
        entry.Tools.Rows.CollectionChanged -= OnToolRowsChanged;

        foreach (OpenCodeAgentToolViewModel row in entry.Tools.Rows)
        {
            row.PropertyChanged -= OnNestedRowPropertyChanged;
        }

        if (entry.Permission is { } permission)
        {
            permission.PropertyChanged -= OnChildEditorPropertyChanged;
        }
    }

    private void OnToolRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (OpenCodeAgentToolViewModel row in e.OldItems)
            {
                row.PropertyChanged -= OnNestedRowPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (OpenCodeAgentToolViewModel row in e.NewItems)
            {
                row.PropertyChanged += OnNestedRowPropertyChanged;
            }
        }

        MarkModified();
    }

    /// <remarks>
    /// The <c>Permission</c> change is where the child grid is swapped in or out, so the
    /// subscription has to follow it — and <c>Children</c> has to be re-announced or the filter
    /// keeps the old list.
    /// </remarks>
    private void OnAgentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenCodeAgentViewModel.IsBuiltIn)
            or nameof(OpenCodeAgentViewModel.IsUserDefined)
            or nameof(OpenCodeAgentViewModel.IsEditable)
            or nameof(OpenCodeAgentViewModel.IsOpaqueEntry)
            or nameof(OpenCodeAgentViewModel.HasNoPermissionOverride)
            or nameof(OpenCodeAgentViewModel.HasDeprecatedTools)
            or nameof(OpenCodeAgentViewModel.GlobalPermissionSummary)
            or nameof(OpenCodeAgentViewModel.HasGlobalPermissionContext))
        {
            return;
        }

        if (e.PropertyName == nameof(OpenCodeAgentViewModel.Permission)
            && sender is OpenCodeAgentViewModel agent)
        {
            // Re-hook: the old child is gone and the new one is the thing that now reports edits.
            if (agent.Permission is { } permission)
            {
                permission.PropertyChanged -= OnChildEditorPropertyChanged;
                permission.PropertyChanged += OnChildEditorPropertyChanged;
            }

            OnPropertyChanged(nameof(Children));
        }

        MarkModified();
    }

    /// <remarks>
    /// The add box only. Marking the editor modified per keystroke makes Save flicker and, on a
    /// live-write host, writes half-typed tool names to disk.
    /// </remarks>
    private void OnToolListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenCodeAgentToolListViewModel.NewTool))
        {
            return;
        }

        MarkModified();
    }

    private void OnNestedRowPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        MarkModified();

    /// <remarks>
    /// Only the child's <c>IsModified</c> matters. Its other properties are its own display state —
    /// shadow counts, tester output — and reacting to those would mark the file dirty for merely
    /// running a test.
    /// </remarks>
    private void OnChildEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PropertyEditorViewModel.IsModified))
        {
            MarkModified();
        }
    }

    /// <summary>
    /// A one-line summary of a permission value, for the read-only global context row.
    /// </summary>
    /// <remarks>
    /// Deliberately crude — a count and the first few tools, not a rendering of the rules. The row
    /// exists to tell the user "there is a global policy under this override", and a fuller view
    /// belongs on the global permission editor rather than duplicated per agent.
    /// </remarks>
    public static string SummarisePermission(object? globalPermission)
    {
        switch (globalPermission)
        {
            case string action:
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "Global permission: {0} for every tool.",
                    action);
            case IReadOnlyDictionary<string, object?> map when map.Count > 0:
                string names = string.Join(", ", map.Keys.Take(4));
                string more = map.Count > 4
                    ? string.Format(CultureInfo.CurrentCulture, " (+{0} more)", map.Count - 4)
                    : string.Empty;
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "Global permission covers: {0}{1}",
                    names,
                    more);
            default:
                return string.Empty;
        }
    }
}
