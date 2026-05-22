using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// Shared observable scope state that keeps the active editing scope in sync
/// across all <see cref="SettingsGroupEditorViewModel"/> instances within the
/// same product section (Claude Code or Claude Desktop).
///
/// One instance is owned by <see cref="MainWindowViewModel"/> per product
/// section and injected into every group editor via
/// <see cref="Services.NavigationTreeBuilder.BuildGroups"/>.  When the user
/// changes the scope dropdown on any page the change propagates to all other
/// pages in the same section automatically.
///
/// <see cref="AvailableScopes"/> is set by <see cref="MainWindowViewModel"/>
/// after each workspace reload and reflects only the scopes that have loaded
/// documents.  This prevents the scope selector from offering "Local" or
/// "Project" when no project folder is open.
/// </summary>
public sealed partial class SharedScopeContext : ObservableObject
{
    public SharedScopeContext(ConfigScope initialScope = ConfigScope.User)
    {
        _editingScope = initialScope;
        _availableScopes = [ConfigScope.User]; // safe minimum; updated by MainWindowViewModel
    }

    [ObservableProperty] private ConfigScope _editingScope;

    /// <summary>
    /// The set of scopes that the user may choose from in the editing-scope dropdown.
    /// Defaults to <c>[User]</c> and is expanded to include <c>Project</c> and
    /// <c>Local</c> by <see cref="MainWindowViewModel"/> when those documents are loaded
    /// (i.e. when a project folder is open).
    /// </summary>
    [ObservableProperty] private IReadOnlyList<ConfigScope> _availableScopes;
}