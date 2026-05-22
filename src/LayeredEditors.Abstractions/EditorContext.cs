namespace Bennewitz.Ninja.LayeredEditors.Abstractions;

/// <summary>
/// Host-supplied services and callbacks that some editors need. Passed into
/// `IPropertyEditorFactory.Create` so factories don't accumulate
/// ever-growing parameter lists, and so specialized editors can look up
/// services without reaching into DI containers or globals.
/// </summary>
/// <param name="BrowsePath">
/// Open a directory picker and return the chosen path (or <c>null</c> if the
/// user cancelled). Consumed by <c>PathPropertyEditor</c> types.
/// </param>
/// <param name="BrowseFile">
/// Open a file picker and return the chosen path (or <c>null</c> if cancelled).
/// </param>
public sealed record EditorContext(
    Func<Task<string?>>? BrowsePath = null,
    Func<Task<string?>>? BrowseFile = null)
{
    /// <summary>Convenience instance with no callbacks — useful for tests.</summary>
    public static EditorContext Empty { get; } = new();
}