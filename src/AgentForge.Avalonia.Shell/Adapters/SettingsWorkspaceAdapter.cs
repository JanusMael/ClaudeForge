using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;

/// <summary>
/// Wraps a <see cref="SettingsWorkspace"/> as an <see cref="IEditorWorkspace"/>.
/// Translates between the library's currency-contract values and
/// <c>System.Text.Json.Nodes.JsonNode</c>, and raises <see cref="ValueChanged"/>
/// after every successful mutation so that listening editors can refresh.
/// </summary>
public sealed class SettingsWorkspaceAdapter : IEditorWorkspace
{
    private readonly SettingsWorkspace _inner;

    public SettingsWorkspaceAdapter(SettingsWorkspace inner)
    {
        _inner = inner;

        // Build scope list ordered highest-priority first (matches library contract)
        AvailableScopes = inner.Documents
                               .Select(d => (IEditorScope)ConfigScopeAdapter.For(d.Scope))
                               .OrderByDescending(s => s.Priority)
                               .ToList();
    }

    // ── IEditorWorkspace ───────────────────────────────────────────────────────

    public IReadOnlyList<IEditorScope> AvailableScopes { get; }

    public IEditorValue GetValue(string path)
    {
        LayeredValue layered = _inner.GetLayeredValue(path);
        return new LayeredValueAdapter(layered);
    }

    public void SetValue(string path, object? value, IEditorScope scope)
    {
        ConfigScope configScope = ConfigScopeAdapter.ToConfigScope(scope);
        JsonNode? node = LayeredValueAdapter.Coerce(value);
        _inner.SetValue(path, node, configScope);
        ValueChanged?.Invoke(this, new ValueChangedEventArgs(path, scope));
    }

    public void RemoveValue(string path, IEditorScope scope)
    {
        ConfigScope configScope = ConfigScopeAdapter.ToConfigScope(scope);
        _inner.RemoveValue(path, configScope);
        ValueChanged?.Invoke(this, new ValueChangedEventArgs(path, scope));
    }

    public event EventHandler<ValueChangedEventArgs>? ValueChanged;
}