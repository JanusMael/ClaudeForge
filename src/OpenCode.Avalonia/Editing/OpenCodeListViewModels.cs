using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Editing;

/// <summary>
/// One element of an editable, ordered list of strings.
/// </summary>
/// <remarks>
/// <para>
/// Shared by every OpenCode editor that surfaces a JSON <c>string[]</c> — an MCP server's argv, a
/// formatter's or language server's <c>command</c>, a language server's <c>extensions</c>. It began
/// life in the <c>mcp</c> editor as <c>OpenCodeMcpArgumentViewModel</c> and moved here when
/// <c>formatter</c> / <c>lsp</c> became the second and third consumers, on the same reasoning that
/// moved <c>OrderedPropertyMap</c> into Abstractions in 9a-3: a type two editors need does not
/// belong inside one of them.
/// </para>
/// <para>
/// <b>Whether position carries meaning is the caller's claim, not this type's.</b> It is meaning
/// for argv — <c>["npx", "-y", "pkg"]</c> is not <c>["-y", "npx", "pkg"]</c> — and merely a
/// convenience for a set of file extensions. Reordering is offered either way because file order is
/// preserved either way; each consumer's own documentation says which case it is.
/// </para>
/// <para>
/// The reorder and remove commands live on the row rather than on the list, because per-row buttons
/// bound to list-level commands need <c>{Binding $parent[ItemsControl].DataContext.…}</c> — an
/// ancestor binding that resolves by reflection and trips <c>IL2026</c>, which is a build error
/// under this repo's warnings-as-errors.
/// </para>
/// </remarks>
public sealed partial class OpenCodeStringRowViewModel : ObservableObject
{
    /// <summary>The text, verbatim.</summary>
    [ObservableProperty] private string _value = string.Empty;

    /// <summary>The list holding this row, or null while detached.</summary>
    internal OpenCodeStringListViewModel? Owner { get; set; }

    /// <summary>Remove this row.</summary>
    [RelayCommand]
    private void Remove() => Owner?.Remove(this);

    /// <summary>Move this row one position earlier.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Owner?.Move(this, -1);

    /// <summary>Move this row one position later.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Owner?.Move(this, +1);

    private bool CanMoveUp() => Owner is { } o && o.Rows.IndexOf(this) > 0;

    private bool CanMoveDown() =>
        Owner is { } o && o.Rows.IndexOf(this) is var i && i >= 0 && i < o.Rows.Count - 1;

    /// <summary>
    /// Re-evaluate the reorder commands after the list changed shape around this row.
    /// </summary>
    /// <remarks>
    /// Their <c>CanExecute</c> depends on this row's index, which nothing on this object notifies
    /// about: inserting a row above changes whether the row below may move up without touching
    /// either row's properties.
    /// </remarks>
    internal void RefreshMoveCommands()
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>
/// An editable, ordered list of strings.
/// </summary>
public sealed partial class OpenCodeStringListViewModel : ObservableObject
{
    /// <summary>The rows, in order.</summary>
    public ObservableCollection<OpenCodeStringRowViewModel> Rows { get; } = [];

    /// <summary>Text for the add box. Transient — never marks an editor modified.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newValue = string.Empty;

    /// <summary>Append a row from <see cref="NewValue"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        string text = NewValue.Trim();
        if (text.Length == 0)
        {
            return;
        }

        Rows.Add(new OpenCodeStringRowViewModel { Value = text });
        NewValue = string.Empty;
        Adopt();
    }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewValue);

    /// <summary>Remove <paramref name="row"/>.</summary>
    internal void Remove(OpenCodeStringRowViewModel row) => Rows.Remove(row);

    /// <summary>Move <paramref name="row"/> by <paramref name="offset"/>, clamped to the list.</summary>
    internal void Move(OpenCodeStringRowViewModel row, int offset)
    {
        int index = Rows.IndexOf(row);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= Rows.Count)
        {
            return;
        }

        Rows.Move(index, target);
    }

    /// <summary>Replace every row, for the load path.</summary>
    internal void Reset(IEnumerable<string> values)
    {
        Rows.Clear();
        foreach (string value in values)
        {
            Rows.Add(new OpenCodeStringRowViewModel { Value = value });
        }

        Adopt();
    }

    /// <summary>
    /// Stamp ownership on every row and refresh their reorder commands.
    /// </summary>
    /// <remarks>
    /// Re-stamped on every structural change rather than only on insert: a row added before this
    /// list existed would otherwise keep disabled buttons for the editor's whole life.
    /// </remarks>
    internal void Adopt()
    {
        foreach (OpenCodeStringRowViewModel row in Rows)
        {
            row.Owner = this;
            row.RefreshMoveCommands();
        }
    }

    /// <summary>The values as written, skipping blank rows.</summary>
    internal IReadOnlyList<string> ToValues() =>
        [.. Rows.Select(r => r.Value).Where(v => !string.IsNullOrWhiteSpace(v))];
}

/// <summary>
/// One <c>key: value</c> pair of a string-to-string map.
/// </summary>
/// <remarks>
/// Shared by the MCP editor's <c>environment</c> / <c>headers</c> maps and by the
/// <c>formatter</c> / <c>lsp</c> editors' environment maps. ⚠ The two tooling keys are spelled
/// differently in the schema — <c>environment</c> for a formatter, <c>env</c> for a language server
/// — and that difference lives in the codec, not here: this type carries pairs and has no opinion
/// about which key holds them.
/// </remarks>
public sealed partial class OpenCodePairRowViewModel : ObservableObject
{
    /// <summary>The key, verbatim.</summary>
    [ObservableProperty] private string _key = string.Empty;

    /// <summary>The value, verbatim.</summary>
    [ObservableProperty] private string _value = string.Empty;

    /// <summary>The list holding this pair, or null while detached.</summary>
    internal OpenCodePairListViewModel? Owner { get; set; }

    /// <summary>Remove this pair.</summary>
    [RelayCommand]
    private void Remove() => Owner?.Remove(this);
}

/// <summary>
/// A string-to-string map.
/// </summary>
/// <remarks>
/// No reordering offered: unlike argv and unlike a permission map, these carry no order-dependent
/// meaning. File order is still preserved on write, but that is about keeping a diff readable
/// rather than about behaviour, so there is nothing here for the user to arrange.
/// </remarks>
public sealed partial class OpenCodePairListViewModel : ObservableObject
{
    /// <summary>The pairs, in file order.</summary>
    public ObservableCollection<OpenCodePairRowViewModel> Rows { get; } = [];

    /// <summary>Key for the add box. Transient.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newKey = string.Empty;

    /// <summary>Value for the add box. Transient.</summary>
    [ObservableProperty] private string _newValue = string.Empty;

    /// <summary>Append a pair from <see cref="NewKey"/> / <see cref="NewValue"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        string key = NewKey.Trim();
        if (key.Length == 0)
        {
            return;
        }

        Rows.Add(new OpenCodePairRowViewModel { Key = key, Value = NewValue });
        NewKey = string.Empty;
        NewValue = string.Empty;
        Adopt();
    }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewKey);

    /// <summary>Remove <paramref name="row"/>.</summary>
    internal void Remove(OpenCodePairRowViewModel row) => Rows.Remove(row);

    /// <summary>Replace every row, for the load path.</summary>
    internal void Reset(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        Rows.Clear();
        foreach ((string key, string value) in pairs)
        {
            Rows.Add(new OpenCodePairRowViewModel { Key = key, Value = value });
        }

        Adopt();
    }

    /// <summary>Stamp ownership on every row.</summary>
    internal void Adopt()
    {
        foreach (OpenCodePairRowViewModel row in Rows)
        {
            row.Owner = this;
        }
    }

    /// <summary>The pairs as written, skipping rows with a blank key.</summary>
    internal IReadOnlyList<KeyValuePair<string, string>> ToPairs() =>
    [
        .. Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Key))
            .Select(r => new KeyValuePair<string, string>(r.Key, r.Value)),
    ];
}
