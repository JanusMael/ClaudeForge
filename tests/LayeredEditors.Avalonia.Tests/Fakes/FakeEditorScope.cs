namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IEditorScope"/>.
/// </summary>
public sealed class FakeEditorScope : IEditorScope
{
    public static readonly FakeEditorScope Managed = new(3, "managed", "MANAGED", isReadOnly: true);
    public static readonly FakeEditorScope User = new(2, "user", "USER", isReadOnly: false);
    public static readonly FakeEditorScope Project = new(1, "project", "PROJECT", isReadOnly: false);
    public static readonly FakeEditorScope Local = new(0, "local", "LOCAL", isReadOnly: false);

    public FakeEditorScope(int priority, string id, string displayName, bool isReadOnly = false)
    {
        Priority = priority;
        Id = id;
        DisplayName = displayName;
        IsReadOnly = isReadOnly;
    }

    public int Priority { get; }
    public string Id { get; }
    public string DisplayName { get; }
    public bool IsReadOnly { get; }

    public override string ToString()
    {
        return Id;
    }
}