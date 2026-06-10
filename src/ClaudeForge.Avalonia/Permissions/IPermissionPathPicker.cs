namespace Bennewitz.Ninja.ClaudeForge.Avalonia.Permissions;

/// <summary>
/// Lets the guided builder's Read/Edit/Write path field invoke a file or folder
/// picker without binding to a specific window. The host implements it over its
/// Avalonia <c>StorageProvider</c> (or dialog service); a headless/test host can
/// supply a canned result.
/// </summary>
public interface IPermissionPathPicker
{
    /// <summary>
    /// Prompt the user to choose a file. Returns the absolute path, or
    /// <see langword="null"/> when cancelled.
    /// </summary>
    Task<string?> PickFileAsync();

    /// <summary>
    /// Prompt the user to choose a folder. Returns the absolute path, or
    /// <see langword="null"/> when cancelled.
    /// </summary>
    Task<string?> PickFolderAsync();
}
