using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Adapters;

/// <summary>
/// Wraps a <see cref="SettingsWorkspace"/> as an <see cref="IEditorWorkspace"/>.
/// Translates between the library's currency-contract values and
/// <c>System.Text.Json.Nodes.JsonNode</c>, and raises <see cref="ValueChanged"/>
/// after every successful mutation so that listening editors can refresh.
/// </summary>
public sealed class ClaudeWorkspaceAdapter : IEditorWorkspace
{
    private readonly SettingsWorkspace _inner;

    public ClaudeWorkspaceAdapter(SettingsWorkspace inner)
    {
        _inner = inner;

        // Build scope list ordered highest-priority first (matches library contract)
        AvailableScopes = inner.Documents
                               .Select(d => (IEditorScope)ClaudeScope.For(d.Scope))
                               .OrderByDescending(s => s.Priority)
                               .ToList();
    }

    // ── IEditorWorkspace ───────────────────────────────────────────────────────

    public IReadOnlyList<IEditorScope> AvailableScopes { get; }

    public IEditorValue GetValue(string path)
    {
        LayeredValue layered = _inner.GetLayeredValue(path);
        return new ClaudeValueAdapter(layered);
    }

    public void SetValue(string path, object? value, IEditorScope scope)
    {
        ConfigScope configScope = ClaudeScope.ToConfigScope(scope);
        JsonNode? node = ClaudeValueAdapter.Coerce(value);
        _inner.SetValue(path, node, configScope);
        ValueChanged?.Invoke(this, new ValueChangedEventArgs(path, scope));
    }

    public void RemoveValue(string path, IEditorScope scope)
    {
        ConfigScope configScope = ClaudeScope.ToConfigScope(scope);
        _inner.RemoveValue(path, configScope);
        ValueChanged?.Invoke(this, new ValueChangedEventArgs(path, scope));
    }

    public event EventHandler<ValueChangedEventArgs>? ValueChanged;
}