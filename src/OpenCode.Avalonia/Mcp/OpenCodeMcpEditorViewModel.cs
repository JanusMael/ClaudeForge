using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Avalonia.Editing;
using Bennewitz.Ninja.OpenCode.Sdk.Mcp;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Mcp;

/// <summary>
/// Editor for OpenCode's <c>mcp</c> setting: a map of server name → one of three union arms.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not the generic object editor.</b> To the schema each value is an <c>anyOf</c> over two
/// <c>$ref</c>s and an inline object, which the generic dispatch cannot classify — so every MCP
/// server renders as raw JSON. That is the single most-edited compound key in an OpenCode config
/// after <c>permission</c>.
/// </para>
/// <para>
/// ⛔ <b>The plan says to copy <c>MarketplaceListEditorViewModel</c> because it "echoes an unknown
/// variant back unchanged rather than dropping it". It does not.</b> That editor returns
/// <see langword="null"/> from <c>TryHydrateEntry</c> for an unknown <c>source</c> and the caller
/// skips the row; its save path carries the comment <c>// unknown source — drop on save</c>. The
/// plan's reasoning was right and its cited evidence was wrong. The preservation pattern here
/// follows the permission grid instead — hold what you cannot parse and write it back — but at
/// <b>per-entry</b> granularity, so one server from a newer OpenCode does not make the rest
/// read-only.
/// </para>
/// <para>
/// Follows the compound-editor contract in
/// <c>src/ClaudeForge/ViewModels/Editors/AGENTS.md</c>: force-fire <c>MarkModified</c>, an
/// <c>_isLoading</c> guard, <see langword="null"/> when empty so the key is removed rather than
/// written empty, transient input fields filtered, and — the trap that bit the Claude MCP editor —
/// nested collections subscribed <i>including the rows already in them at hook time</i>.
/// </para>
/// </remarks>
public sealed partial class OpenCodeMcpEditorViewModel : PropertyEditorViewModel
{
    private bool _isLoading;
    private IEditorValue? _lastValue;
    private IEditorScope? _lastScope;

    /// <summary>Creates the editor for <paramref name="schema"/>.</summary>
    public OpenCodeMcpEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
        Servers = [];
        Servers.CollectionChanged += OnServersChanged;
    }

    /// <summary>The kinds a user may choose for a server.</summary>
    /// <remarks>
    /// <see cref="OpenCodeMcpKind.Unrecognised"/> is deliberately absent: it is a state an entry
    /// arrives in, never one a user selects. Offering it would invite turning an editable server
    /// into an opaque blob.
    /// </remarks>
    public static IReadOnlyList<OpenCodeMcpKind> SelectableKinds { get; } =
        [OpenCodeMcpKind.Local, OpenCodeMcpKind.Remote, OpenCodeMcpKind.EnabledOverride];

    /// <summary>Server entries, in file order.</summary>
    public ObservableCollection<OpenCodeMcpServerViewModel> Servers { get; }

    /// <summary>Name for the add box. Transient — never marks the editor modified.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddServerCommand))]
    private string _newServerName = string.Empty;

    /// <summary>
    /// How many entries are held verbatim because their shape is unrecognised.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpaqueServers))]
    private int _opaqueServerCount;

    /// <summary>True when at least one entry is held verbatim.</summary>
    public bool HasOpaqueServers => OpaqueServerCount > 0;

    /// <summary>Add a server from <see cref="NewServerName"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanAddServer))]
    private void AddServer()
    {
        string name = NewServerName.Trim();
        if (name.Length == 0
            || Servers.Any(s => string.Equals(s.Name, name, StringComparison.Ordinal)))
        {
            return;
        }

        OpenCodeMcpServerViewModel entry = NewServer(name);
        entry.Kind = OpenCodeMcpKind.Local;
        Servers.Add(entry);
        NewServerName = string.Empty;
    }

    private bool CanAddServer() => !string.IsNullOrWhiteSpace(NewServerName);

    private OpenCodeMcpServerViewModel NewServer(string name) =>
        new(name, onRemove: entry => Servers.Remove(entry));

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to the SDK codec so the written shape stays adjacent to the reader. ⚠ Returns
    /// value-currency types only — never a <c>JsonNode</c>, which the currency conversion has no
    /// case for and would stringify, landing the whole MCP block in the file as one quoted string.
    /// </remarks>
    public override object? ToValue() => OpenCodeMcpCodec.WriteMap(BuildEntries());

    private List<KeyValuePair<string, OpenCodeMcpServer>> BuildEntries()
    {
        List<KeyValuePair<string, OpenCodeMcpServer>> entries = [];
        foreach (OpenCodeMcpServerViewModel server in Servers)
        {
            entries.Add(new KeyValuePair<string, OpenCodeMcpServer>(
                server.Name.Trim(),
                server.ToModel()));
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

            foreach (OpenCodeMcpServerViewModel entry in Servers)
            {
                UnsubscribeServer(entry);
            }

            Servers.Clear();

            foreach ((string name, OpenCodeMcpServer server) in
                     OpenCodeMcpCodec.ReadMap(value.GetValueAt(editingScope)))
            {
                OpenCodeMcpServerViewModel entry = NewServer(name);

                // Populated BEFORE the entry joins the collection, which is exactly why
                // SubscribeServer has to hook the rows already present rather than only future
                // additions.
                entry.Load(server);
                Servers.Add(entry);
            }

            IsModified = value.IsDefinedAt(editingScope);
            UpdateOtherScopesWithData(value, editingScope);
            UpdateInheritedDisplay(value, editingScope);
        }
        finally
        {
            _isLoading = false;
        }

        RefreshOpaqueCount();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reloads the last-loaded value rather than clearing, so reset restores what is on disk.
    /// </remarks>
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
            Servers.Clear();
        }
        finally
        {
            _isLoading = false;
        }

        RefreshOpaqueCount();
    }

    // ── Modification plumbing ────────────────────────────────────────────────

    /// <summary>
    /// Force-fire <c>PropertyChanged(IsModified)</c> on every user mutation, even when the flag was
    /// already true from the prior load.
    /// </summary>
    /// <remarks>
    /// <c>[ObservableProperty]</c>'s generated setter elides equal assignments, so a bare
    /// <c>IsModified = true</c> after a load that already set it is a no-op — and the live-write
    /// and save-enable subscriptions both watch the event rather than the value.
    /// </remarks>
    private void MarkModified()
    {
        if (_isLoading)
        {
            return;
        }

        RefreshOpaqueCount();

        if (IsModified)
        {
            OnPropertyChanged(nameof(IsModified));
        }
        else
        {
            IsModified = true;
        }
    }

    private void RefreshOpaqueCount() =>
        OpaqueServerCount = Servers.Count(s => s.Kind == OpenCodeMcpKind.Unrecognised);

    private void OnServersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (OpenCodeMcpServerViewModel entry in e.OldItems)
            {
                UnsubscribeServer(entry);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (OpenCodeMcpServerViewModel entry in e.NewItems)
            {
                SubscribeServer(entry);
            }
        }

        MarkModified();
    }

    /// <remarks>
    /// ⚠ <b>Three levels, and the third is the one that gets forgotten.</b> The server's own
    /// properties, its three nested collections, and the rows <i>already inside</i> those
    /// collections — because <see cref="LoadFromValue"/> fills them before adding the server. Hook
    /// only future additions and every loaded row goes silent: inline edits never reach Save. This
    /// is the documented trap from the Claude MCP editor, reproduced here on purpose.
    /// </remarks>
    private void SubscribeServer(OpenCodeMcpServerViewModel entry)
    {
        entry.PropertyChanged += OnServerPropertyChanged;

        entry.Command.Rows.CollectionChanged += OnNestedCollectionChanged;
        entry.Environment.Rows.CollectionChanged += OnNestedCollectionChanged;
        entry.Headers.Rows.CollectionChanged += OnNestedCollectionChanged;

        entry.Command.PropertyChanged += OnNestedListPropertyChanged;
        entry.Environment.PropertyChanged += OnNestedListPropertyChanged;
        entry.Headers.PropertyChanged += OnNestedListPropertyChanged;

        foreach (OpenCodeStringRowViewModel row in entry.Command.Rows)
        {
            row.PropertyChanged += OnNestedRowPropertyChanged;
        }

        foreach (OpenCodePairRowViewModel row in entry.Environment.Rows)
        {
            row.PropertyChanged += OnNestedRowPropertyChanged;
        }

        foreach (OpenCodePairRowViewModel row in entry.Headers.Rows)
        {
            row.PropertyChanged += OnNestedRowPropertyChanged;
        }
    }

    /// <remarks>
    /// Mirrors <see cref="SubscribeServer"/> exactly. Asymmetry here means a reload accumulates
    /// handlers and <see cref="MarkModified"/> fires N times per keystroke, which is invisible
    /// until it is a performance bug.
    /// </remarks>
    private void UnsubscribeServer(OpenCodeMcpServerViewModel entry)
    {
        entry.PropertyChanged -= OnServerPropertyChanged;

        entry.Command.Rows.CollectionChanged -= OnNestedCollectionChanged;
        entry.Environment.Rows.CollectionChanged -= OnNestedCollectionChanged;
        entry.Headers.Rows.CollectionChanged -= OnNestedCollectionChanged;

        entry.Command.PropertyChanged -= OnNestedListPropertyChanged;
        entry.Environment.PropertyChanged -= OnNestedListPropertyChanged;
        entry.Headers.PropertyChanged -= OnNestedListPropertyChanged;

        foreach (OpenCodeStringRowViewModel row in entry.Command.Rows)
        {
            row.PropertyChanged -= OnNestedRowPropertyChanged;
        }

        foreach (OpenCodePairRowViewModel row in entry.Environment.Rows)
        {
            row.PropertyChanged -= OnNestedRowPropertyChanged;
        }

        foreach (OpenCodePairRowViewModel row in entry.Headers.Rows)
        {
            row.PropertyChanged -= OnNestedRowPropertyChanged;
        }
    }

    private void OnNestedCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (object row in e.OldItems)
            {
                Detach(row);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (object row in e.NewItems)
            {
                Attach(row);
            }
        }

        // A row added through the UI arrives with no owner, and every row's reorder CanExecute
        // depends on its index, so ownership is re-stamped across the board after any structural
        // change rather than only on the list that raised this.
        foreach (OpenCodeMcpServerViewModel server in Servers)
        {
            server.Command.Adopt();
            server.Environment.Adopt();
            server.Headers.Adopt();
        }

        MarkModified();
    }

    private void Attach(object row)
    {
        switch (row)
        {
            case OpenCodeStringRowViewModel a:
                a.PropertyChanged += OnNestedRowPropertyChanged;
                break;
            case OpenCodePairRowViewModel p:
                p.PropertyChanged += OnNestedRowPropertyChanged;
                break;
            default:
                break;
        }
    }

    private void Detach(object row)
    {
        switch (row)
        {
            case OpenCodeStringRowViewModel a:
                a.PropertyChanged -= OnNestedRowPropertyChanged;
                break;
            case OpenCodePairRowViewModel p:
                p.PropertyChanged -= OnNestedRowPropertyChanged;
                break;
            default:
                break;
        }
    }

    /// <remarks>
    /// The filtered names are computed from <c>Kind</c> or <c>OauthMode</c>, whose own notifications
    /// already mark the edit. <c>OpaqueNotice</c> is written by the load path.
    /// </remarks>
    private void OnServerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenCodeMcpServerViewModel.IsLocal)
            or nameof(OpenCodeMcpServerViewModel.IsRemote)
            or nameof(OpenCodeMcpServerViewModel.IsEditable)
            or nameof(OpenCodeMcpServerViewModel.IsOpaque)
            or nameof(OpenCodeMcpServerViewModel.ShowOAuthFields)
            or nameof(OpenCodeMcpServerViewModel.OpaqueNotice))
        {
            return;
        }

        MarkModified();
    }

    /// <remarks>
    /// The add boxes. Marking the editor modified per keystroke makes the Save button flicker and,
    /// on a live-write host, writes half-typed values to disk.
    /// </remarks>
    private void OnNestedListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenCodeStringListViewModel.NewValue)
            or nameof(OpenCodePairListViewModel.NewKey))
        {
            return;
        }

        MarkModified();
    }

    private void OnNestedRowPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        MarkModified();
}
