using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Agents;

/// <summary>
/// One <c>tool → enabled</c> flag in an agent's deprecated <c>tools</c> map.
/// </summary>
public sealed partial class OpenCodeAgentToolViewModel : ObservableObject
{
    /// <summary>The tool name, verbatim.</summary>
    [ObservableProperty] private string _tool = string.Empty;

    /// <summary>Whether the tool is enabled for this agent.</summary>
    [ObservableProperty] private bool _enabled;

    /// <summary>The list holding this flag, or null while detached.</summary>
    internal OpenCodeAgentToolListViewModel? Owner { get; set; }

    /// <summary>Remove this flag.</summary>
    [RelayCommand]
    private void Remove() => Owner?.Remove(this);
}

/// <summary>
/// An agent's deprecated <c>tools</c> map.
/// </summary>
/// <remarks>
/// ⚠ <b>Deprecated by the schema itself</b> — the description on <c>AgentConfig.tools</c> reads
/// "@deprecated Use 'permission' field instead". It stays editable because existing configs contain
/// it and dropping a user's flags would change how their agent behaves; the view surfaces the
/// deprecation and offers no way to add the map where one does not already exist.
/// </remarks>
public sealed partial class OpenCodeAgentToolListViewModel : ObservableObject
{
    /// <summary>The flags, in file order.</summary>
    public ObservableCollection<OpenCodeAgentToolViewModel> Rows { get; } = [];

    /// <summary>Tool name for the add box. Transient.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newTool = string.Empty;

    /// <summary>Add a flag from <see cref="NewTool"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        string tool = NewTool.Trim();
        if (tool.Length == 0)
        {
            return;
        }

        Rows.Add(new OpenCodeAgentToolViewModel { Tool = tool, Enabled = true, Owner = this });
        NewTool = string.Empty;
    }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewTool);

    /// <summary>Remove <paramref name="row"/>.</summary>
    internal void Remove(OpenCodeAgentToolViewModel row) => Rows.Remove(row);

    /// <summary>Replace every row, for the load path.</summary>
    internal void Reset(IEnumerable<KeyValuePair<string, bool>> flags)
    {
        Rows.Clear();
        foreach ((string tool, bool enabled) in flags)
        {
            Rows.Add(new OpenCodeAgentToolViewModel { Tool = tool, Enabled = enabled, Owner = this });
        }
    }

    /// <summary>The flags as written, skipping rows with a blank name.</summary>
    internal IReadOnlyList<KeyValuePair<string, bool>> ToPairs() =>
    [
        .. Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Tool))
            .Select(r => new KeyValuePair<string, bool>(r.Tool, r.Enabled)),
    ];
}
