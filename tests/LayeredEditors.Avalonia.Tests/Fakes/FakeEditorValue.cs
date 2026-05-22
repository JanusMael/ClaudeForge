namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IEditorValue"/>.
/// Stores per-scope values keyed by scope object; merges by highest-priority scope.
/// </summary>
public sealed class FakeEditorValue : IEditorValue
{
    private readonly List<(IEditorScope Scope, object? Value)> _entries = [];

    public FakeEditorValue(string path)
    {
        Path = path;
    }

    public string Path { get; }

    /// <summary>Fluently define an explicit value at the given scope.</summary>
    public FakeEditorValue With(IEditorScope scope, object? value)
    {
        _entries.RemoveAll(e => e.Scope.Id == scope.Id);
        _entries.Add((scope, value));
        return this;
    }

    // ── IEditorValue ────────────────────────────────────────────────────────────

    public IEditorScope? EffectiveScope =>
        _entries.OrderByDescending(e => e.Scope.Priority).Select(e => e.Scope).FirstOrDefault();

    public object? EffectiveValue =>
        EffectiveScope is { } s ? GetValueAt(s) : null;

    public bool IsOverridden => _entries.Count > 1;

    public object? GetValueAt(IEditorScope scope)
    {
        return _entries.FirstOrDefault(e => e.Scope.Id == scope.Id).Value;
    }

    public bool IsDefinedAt(IEditorScope scope)
    {
        return _entries.Any(e => e.Scope.Id == scope.Id);
    }

    /// <summary>
    /// mirror of <c>ClaudeValueAdapter.EnumerateDefinedScopes()</c>
    /// for the fake test double.  Returns every scope that has an explicit
    /// entry; deduplication isn't needed because <c>With</c> overwrites
    /// existing entries at the same scope.
    /// </summary>
    public IEnumerable<IEditorScope> EnumerateDefinedScopes()
    {
        return _entries.Select(e => e.Scope);
    }
}