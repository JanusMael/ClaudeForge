namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Fakes;

/// <summary>
/// In-memory test double for <see cref="IEditorWorkspace"/>.
/// Stores values in a plain dictionary; raises <see cref="ValueChanged"/> after every mutation.
/// </summary>
public sealed class FakeEditorWorkspace : IEditorWorkspace
{
    // path → (scopeId → value)
    private readonly Dictionary<string, Dictionary<string, object?>> _store = new();
    private readonly List<IEditorScope> _scopes;

    public FakeEditorWorkspace(IEnumerable<IEditorScope>? scopes = null)
    {
        _scopes = scopes?.OrderByDescending(s => s.Priority).ToList()
                  ?? [FakeEditorScope.Managed, FakeEditorScope.User, FakeEditorScope.Project, FakeEditorScope.Local];
    }

    // ── IEditorWorkspace ────────────────────────────────────────────────────────

    public IReadOnlyList<IEditorScope> AvailableScopes => _scopes;

    public IEditorValue GetValue(string path)
    {
        FakeEditorValue value = new(path);

        if (_store.TryGetValue(path, out Dictionary<string, object?>? byScope))
        {
            foreach (IEditorScope scope in _scopes)
            {
                if (byScope.TryGetValue(scope.Id, out object? v))
                {
                    value.With(scope, v);
                }
            }
        }

        return value;
    }

    public void SetValue(string path, object? value, IEditorScope scope)
    {
        if (scope.IsReadOnly)
        {
            throw new InvalidOperationException($"Scope '{scope.Id}' is read-only.");
        }

        if (!_store.TryGetValue(path, out Dictionary<string, object?>? byScope))
        {
            byScope = new Dictionary<string, object?>();
            _store[path] = byScope;
        }

        byScope[scope.Id] = value;
        ValueChanged?.Invoke(this, new ValueChangedEventArgs(path, scope));
    }

    public void RemoveValue(string path, IEditorScope scope)
    {
        if (_store.TryGetValue(path, out Dictionary<string, object?>? byScope) && byScope.Remove(scope.Id))
        {
            ValueChanged?.Invoke(this, new ValueChangedEventArgs(path, scope));
        }
    }

    public event EventHandler<ValueChangedEventArgs>? ValueChanged;

    // ── Test helpers ────────────────────────────────────────────────────────────

    /// <summary>Pre-seed a value without raising ValueChanged.</summary>
    public FakeEditorWorkspace Seed(string path, IEditorScope scope, object? value)
    {
        if (!_store.TryGetValue(path, out Dictionary<string, object?>? byScope))
        {
            byScope = new Dictionary<string, object?>();
            _store[path] = byScope;
        }

        byScope[scope.Id] = value;
        return this;
    }

    /// <summary>Number of ValueChanged events raised since construction (or last reset).</summary>
    public int EventCount { get; private set; }

    public void ResetEventCount()
    {
        EventCount = 0;
    }

    // Track event count automatically
    public FakeEditorWorkspace TrackEvents()
    {
        ValueChanged += (_, _) => TrackedEventCount++;
        return this;
    }

    public int TrackedEventCount { get; private set; }
}