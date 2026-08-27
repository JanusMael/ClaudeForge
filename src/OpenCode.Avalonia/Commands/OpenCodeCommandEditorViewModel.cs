using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Sdk.Commands;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Commands;

/// <summary>
/// One entry of the <c>command</c> map.
/// </summary>
/// <remarks>
/// ⚠ Two things this row surfaces that a text box cannot: that <c>template</c> is <b>required</b>
/// and currently missing, and that a template containing <c>!`…`</c> will <b>run a shell command</b>
/// every time the command is invoked.
/// </remarks>
public sealed partial class OpenCodeCommandViewModel : ObservableObject
{
    private readonly Action<OpenCodeCommandViewModel>? _onRemove;

    private IReadOnlyList<KeyValuePair<string, object?>> _extras = [];
    private object? _raw;
    private bool _isOpaque;

    /// <summary>Creates an entry named <paramref name="name"/>.</summary>
    public OpenCodeCommandViewModel(string name, Action<OpenCodeCommandViewModel>? onRemove = null)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _onRemove = onRemove;
    }

    /// <summary>The command key, exactly as written.</summary>
    [ObservableProperty] private string _name;

    /// <summary>The command body. Required by the schema.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTemplateMissing))]
    [NotifyPropertyChangedFor(nameof(UsesShellInterpolation))]
    private string _template = string.Empty;

    /// <summary>Human-readable description.</summary>
    [ObservableProperty] private string _description = string.Empty;

    /// <summary>The agent this command runs as.</summary>
    [ObservableProperty] private string _agent = string.Empty;

    /// <summary>Model id override.</summary>
    [ObservableProperty] private string _model = string.Empty;

    /// <summary>Model variant override.</summary>
    [ObservableProperty] private string _variant = string.Empty;

    /// <summary>Whether the command runs as a subtask; null leaves the key out.</summary>
    [ObservableProperty] private bool? _subtask;

    /// <summary>True when the entry was not an object and is held verbatim.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    private bool _isOpaqueEntry;

    /// <summary>True when this entry has editable fields.</summary>
    public bool IsEditable => !IsOpaqueEntry;

    /// <summary>
    /// True when the required <c>template</c> is missing, so the entry is invalid.
    /// </summary>
    /// <remarks>
    /// The only required field in this whole phase. Reported rather than repaired — an invented
    /// template would be a claim about what the command does.
    /// </remarks>
    public bool IsTemplateMissing => IsEditable && string.IsNullOrWhiteSpace(Template);

    /// <summary>
    /// True when the template runs a shell command through <c>!`…`</c>.
    /// </summary>
    public bool UsesShellInterpolation =>
        OpenCodeCommandConfig.ContainsShellInterpolation(Template);

    /// <summary>Remove this command from the map.</summary>
    [RelayCommand]
    private void Remove() => _onRemove?.Invoke(this);

    /// <summary>Populate from a parsed entry.</summary>
    internal void Load(OpenCodeCommandConfig command)
    {
        _extras = command.Extras;
        _raw = command.Raw;
        _isOpaque = command.IsOpaque;

        IsOpaqueEntry = command.IsOpaque;
        Template = command.Template ?? string.Empty;
        Description = command.Description ?? string.Empty;
        Agent = command.Agent ?? string.Empty;
        Model = command.Model ?? string.Empty;
        Variant = command.Variant ?? string.Empty;
        Subtask = command.Subtask;
    }

    /// <summary>The model form of this entry.</summary>
    internal OpenCodeCommandConfig ToModel()
    {
        if (_isOpaque)
        {
            return new OpenCodeCommandConfig { IsOpaque = true, Raw = _raw };
        }

        return new OpenCodeCommandConfig
        {
            Template = NullIfBlank(Template),
            Description = NullIfBlank(Description),
            Agent = NullIfBlank(Agent),
            Model = NullIfBlank(Model),
            Variant = NullIfBlank(Variant),
            Subtask = Subtask,
            Extras = _extras,
        };
    }

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// Editor for OpenCode's <c>command</c> setting: a map of command name → six fields, one required.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not the generic object editor.</b> The generic dispatch renders each entry as raw JSON,
/// which hides the two things that matter: that <c>template</c> is required, and that a template
/// may execute a shell command. Neither is visible in a JSON blob, and both change what the user
/// should do next.
/// </para>
/// <para>
/// Follows the compound-editor contract in <c>src/ClaudeForge/ViewModels/Editors/AGENTS.md</c>:
/// force-fire <c>MarkModified</c>, an <c>_isLoading</c> guard, <see langword="null"/> when empty,
/// and transient input fields filtered out of the modified signal.
/// </para>
/// </remarks>
public sealed partial class OpenCodeCommandEditorViewModel : PropertyEditorViewModel
{
    private bool _isLoading;
    private IEditorValue? _lastValue;
    private IEditorScope? _lastScope;

    /// <summary>Creates the editor for <paramref name="schema"/>.</summary>
    public OpenCodeCommandEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
        Commands = [];
        Commands.CollectionChanged += OnCommandsChanged;
    }

    /// <summary>Command entries, in file order.</summary>
    public ObservableCollection<OpenCodeCommandViewModel> Commands { get; }

    /// <summary>Name for the add box. Transient — never marks the editor modified.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommandEntryCommand))]
    private string _newCommandName = string.Empty;

    /// <summary>How many entries are missing their required <c>template</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInvalidCommands))]
    private int _invalidCommandCount;

    /// <summary>True when at least one entry is missing its template.</summary>
    public bool HasInvalidCommands => InvalidCommandCount > 0;

    /// <summary>How many templates run a shell command.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShellCommands))]
    private int _shellCommandCount;

    /// <summary>True when at least one template runs a shell command.</summary>
    public bool HasShellCommands => ShellCommandCount > 0;

    /// <summary>Add a command from <see cref="NewCommandName"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanAddCommandEntry))]
    private void AddCommandEntry()
    {
        string name = NewCommandName.Trim();
        if (name.Length == 0
            || Commands.Any(c => string.Equals(c.Name, name, StringComparison.Ordinal)))
        {
            return;
        }

        OpenCodeCommandViewModel entry = NewCommand(name);
        entry.Load(new OpenCodeCommandConfig());
        Commands.Add(entry);
        NewCommandName = string.Empty;
    }

    private bool CanAddCommandEntry() => !string.IsNullOrWhiteSpace(NewCommandName);

    private OpenCodeCommandViewModel NewCommand(string name) =>
        new(name, onRemove: entry => Commands.Remove(entry));

    /// <inheritdoc />
    public override object? ToValue() => OpenCodeCommandCodec.WriteMap(BuildEntries());

    private List<KeyValuePair<string, OpenCodeCommandConfig>> BuildEntries()
    {
        List<KeyValuePair<string, OpenCodeCommandConfig>> entries = [];
        foreach (OpenCodeCommandViewModel command in Commands)
        {
            entries.Add(new KeyValuePair<string, OpenCodeCommandConfig>(
                command.Name.Trim(),
                command.ToModel()));
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

            foreach (OpenCodeCommandViewModel entry in Commands)
            {
                UnsubscribeCommand(entry);
            }

            Commands.Clear();

            foreach ((string name, OpenCodeCommandConfig command) in
                     OpenCodeCommandCodec.ReadMap(value.GetValueAt(editingScope)))
            {
                OpenCodeCommandViewModel entry = NewCommand(name);
                entry.Load(command);
                Commands.Add(entry);
            }

            IsModified = value.IsDefinedAt(editingScope);
            UpdateOtherScopesWithData(value, editingScope);
            UpdateInheritedDisplay(value, editingScope);
        }
        finally
        {
            _isLoading = false;
        }

        RefreshCounts();
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
            Commands.Clear();
        }
        finally
        {
            _isLoading = false;
        }

        RefreshCounts();
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

        RefreshCounts();

        if (IsModified)
        {
            OnPropertyChanged(nameof(IsModified));
        }
        else
        {
            IsModified = true;
        }
    }

    private void RefreshCounts()
    {
        InvalidCommandCount = Commands.Count(c => c.IsTemplateMissing);
        ShellCommandCount = Commands.Count(c => c.UsesShellInterpolation);
    }

    private void OnCommandsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (OpenCodeCommandViewModel entry in e.OldItems)
            {
                UnsubscribeCommand(entry);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (OpenCodeCommandViewModel entry in e.NewItems)
            {
                SubscribeCommand(entry);
            }
        }

        MarkModified();
    }

    private void SubscribeCommand(OpenCodeCommandViewModel entry) =>
        entry.PropertyChanged += OnCommandPropertyChanged;

    private void UnsubscribeCommand(OpenCodeCommandViewModel entry) =>
        entry.PropertyChanged -= OnCommandPropertyChanged;

    /// <remarks>
    /// ⚠ The filtered names are all <b>derived</b> from <c>Template</c> or <c>IsOpaqueEntry</c>,
    /// whose own notifications already mark the edit — and <c>IsTemplateMissing</c> /
    /// <c>UsesShellInterpolation</c> are recomputed by <see cref="RefreshCounts"/>, which
    /// <see cref="MarkModified"/> calls. Without the filter, one keystroke becomes
    /// mark → recount → row changed → mark.
    /// </remarks>
    private void OnCommandPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenCodeCommandViewModel.IsTemplateMissing)
            or nameof(OpenCodeCommandViewModel.UsesShellInterpolation)
            or nameof(OpenCodeCommandViewModel.IsEditable)
            or nameof(OpenCodeCommandViewModel.IsOpaqueEntry))
        {
            return;
        }

        MarkModified();
    }
}
