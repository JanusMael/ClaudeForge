using System.Collections.Specialized;
using System.ComponentModel;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Sdk.Plugins;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Plugins;

/// <summary>
/// Editor for a <c>plugin</c> array: a list of specifiers, each optionally paired with an options
/// object.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>One editor serves both products.</b> <c>Config.plugin</c> and the TUI's <c>plugin</c>
/// declare the identical <c>anyOf</c>, so registering by property name covers both — the same reuse
/// the permission grid gets from <c>Config.permission</c> and <c>AgentConfig.permission</c> being
/// one <c>$ref</c>.
/// </para>
/// <para>
/// <b>Why not the generic editor.</b> The generic dispatch sees an array whose items are an
/// <c>anyOf</c> it cannot classify, so every plugin renders as raw JSON — and the distinction that
/// matters, bare specifier versus specifier-with-options, is invisible in a blob.
/// </para>
/// <para>
/// ⛔ The plan recommends copying <c>MarketplaceListEditorViewModel</c> here. Phase 9a-3 measured
/// that template: it <b>drops</b> an unknown variant on load and again on save, the opposite of what
/// the plan credits it with. This follows 9a-3's per-entry preservation instead.
/// </para>
/// </remarks>
public sealed partial class OpenCodePluginEditorViewModel : PropertyEditorViewModel
{
    private bool _isLoading;
    private IEditorValue? _lastValue;
    private IEditorScope? _lastScope;

    /// <summary>Creates the editor for <paramref name="schema"/>.</summary>
    public OpenCodePluginEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
        Plugins = new OpenCodePluginListViewModel();
        Plugins.PropertyChanged += OnListPropertyChanged;
        Plugins.Rows.CollectionChanged += OnRowsChanged;
    }

    /// <summary>The plugin list.</summary>
    public OpenCodePluginListViewModel Plugins { get; }

    /// <summary>How many elements are held verbatim because they matched neither arm.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpaqueEntries))]
    private int _opaqueEntryCount;

    /// <summary>True when at least one element is held verbatim.</summary>
    public bool HasOpaqueEntries => OpaqueEntryCount > 0;

    /// <summary>How many elements have unparseable options JSON.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOptionErrors))]
    private int _optionErrorCount;

    /// <summary>True when at least one element's options JSON is invalid.</summary>
    public bool HasOptionErrors => OptionErrorCount > 0;

    /// <inheritdoc />
    public override object? ToValue() => OpenCodePluginCodec.WriteList(Plugins.ToEntries());

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

            // ⚠⚠ The collection handler is DETACHED for the rebuild, and that is what makes the
            // explicit re-subscribe below correct rather than doubled. The earlier comment here
            // claimed "Reset() repopulates the collection while the guard is up, so
            // CollectionChanged never hooked these rows" — measured in 9a-8, that was false: the
            // `_isLoading` guard suppresses MarkModified, not the subscription, so every Add hooked
            // its row AND the loop below hooked it again. Two handlers per row, MarkModified firing
            // twice per keystroke, and no test noticed because the cascade test only bounds the
            // count at four.
            Plugins.Rows.CollectionChanged -= OnRowsChanged;
            try
            {
                foreach (OpenCodePluginRowViewModel row in Plugins.Rows)
                {
                    row.PropertyChanged -= OnRowPropertyChanged;
                }

                Plugins.Reset(OpenCodePluginCodec.ReadList(value.GetValueAt(editingScope)));
            }
            finally
            {
                Plugins.Rows.CollectionChanged += OnRowsChanged;
            }

            foreach (OpenCodePluginRowViewModel row in Plugins.Rows)
            {
                row.PropertyChanged += OnRowPropertyChanged;
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
            Plugins.Rows.Clear();
        }
        finally
        {
            _isLoading = false;
        }

        RefreshCounts();
    }

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
        OpaqueEntryCount = Plugins.Rows.Count(r => r.IsOpaqueEntry);
        OptionErrorCount = Plugins.Rows.Count(r => r.HasOptionsError);
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (OpenCodePluginRowViewModel row in e.OldItems)
            {
                row.PropertyChanged -= OnRowPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (OpenCodePluginRowViewModel row in e.NewItems)
            {
                row.PropertyChanged += OnRowPropertyChanged;
            }
        }

        Plugins.Adopt();
        MarkModified();
    }

    /// <remarks>
    /// The add box only — marking the editor modified per keystroke makes Save flicker and, on a
    /// live-write host, writes half-typed specifiers to disk.
    /// </remarks>
    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenCodePluginListViewModel.NewName))
        {
            return;
        }

        MarkModified();
    }

    /// <remarks>
    /// ⚠ The derived names are recomputed by <see cref="RefreshCounts"/>, which
    /// <see cref="MarkModified"/> calls — so without the filter one keystroke becomes
    /// mark → recount → row changed → mark.
    /// </remarks>
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenCodePluginRowViewModel.HasOptionsError)
            or nameof(OpenCodePluginRowViewModel.ShowOptions)
            or nameof(OpenCodePluginRowViewModel.IsEditable)
            or nameof(OpenCodePluginRowViewModel.IsOpaqueEntry))
        {
            return;
        }

        MarkModified();
    }
}

/// <summary>
/// Editor for the TUI's <c>plugin_enabled</c> map: plugin name → on/off.
/// </summary>
/// <remarks>
/// TUI-only, and separate from <c>plugin</c> on purpose: this map toggles plugins that something
/// else declares, so a name here need not appear in any <c>plugin</c> array this editor can see.
/// </remarks>
public sealed partial class OpenCodePluginEnabledEditorViewModel : PropertyEditorViewModel
{
    private bool _isLoading;
    private IEditorValue? _lastValue;
    private IEditorScope? _lastScope;

    /// <summary>Creates the editor for <paramref name="schema"/>.</summary>
    public OpenCodePluginEnabledEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
        Toggles = new OpenCodePluginToggleListViewModel();
        Toggles.PropertyChanged += OnListPropertyChanged;
        Toggles.Rows.CollectionChanged += OnRowsChanged;
    }

    /// <summary>The toggle list.</summary>
    public OpenCodePluginToggleListViewModel Toggles { get; }

    /// <inheritdoc />
    public override object? ToValue() => OpenCodePluginCodec.WriteEnabledMap(Toggles.ToToggles());

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

            // Detached for the rebuild, for the reason spelled out in the sibling editor above:
            // otherwise each Add subscribes the row and the explicit loop subscribes it again.
            Toggles.Rows.CollectionChanged -= OnRowsChanged;
            try
            {
                foreach (OpenCodePluginToggleViewModel row in Toggles.Rows)
                {
                    row.PropertyChanged -= OnRowPropertyChanged;
                }

                Toggles.Reset(OpenCodePluginCodec.ReadEnabledMap(value.GetValueAt(editingScope)));
            }
            finally
            {
                Toggles.Rows.CollectionChanged += OnRowsChanged;
            }

            foreach (OpenCodePluginToggleViewModel row in Toggles.Rows)
            {
                row.PropertyChanged += OnRowPropertyChanged;
            }

            IsModified = value.IsDefinedAt(editingScope);
            UpdateOtherScopesWithData(value, editingScope);
            UpdateInheritedDisplay(value, editingScope);
        }
        finally
        {
            _isLoading = false;
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
            Toggles.Rows.Clear();
        }
        finally
        {
            _isLoading = false;
        }
    }

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

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (OpenCodePluginToggleViewModel row in e.OldItems)
            {
                row.PropertyChanged -= OnRowPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (OpenCodePluginToggleViewModel row in e.NewItems)
            {
                row.PropertyChanged += OnRowPropertyChanged;
            }
        }

        MarkModified();
    }

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenCodePluginToggleListViewModel.NewName))
        {
            return;
        }

        MarkModified();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenCodePluginToggleViewModel.IsEditable)
            or nameof(OpenCodePluginToggleViewModel.IsOpaqueEntry))
        {
            return;
        }

        MarkModified();
    }
}
