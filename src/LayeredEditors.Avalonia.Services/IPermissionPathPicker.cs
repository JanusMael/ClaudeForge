namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

/// <summary>
/// Lets a rule builder's path field invoke a file or folder picker without binding to a
/// specific window. The host implements it over its Avalonia <c>StorageProvider</c> (or
/// dialog service); a headless/test host can supply a canned result.
/// </summary>
/// <remarks>
/// Filed here rather than beside the permission editors because nothing about it is a
/// permissions type — it is a file picker, and it sits alongside the other neutral host
/// services (<see cref="IDialogService"/>, <see cref="IShareService"/>,
/// <see cref="IShellLauncher"/>) for the same reason they do.
/// </remarks>
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
