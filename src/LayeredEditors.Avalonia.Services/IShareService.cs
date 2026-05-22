namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

/// <summary>
/// Invokes the native OS share sheet for text or file payloads.
/// </summary>
/// <remarks>
/// Platform behaviour:
/// <list type="bullet">
///   <item><b>Windows 10+</b> — MAUI Essentials opens the native Windows share flyout.</item>
///   <item><b>macOS</b> — Files are revealed in Finder (the user can invoke the Finder Share
///     menu). URIs are opened in the default browser via <c>open</c>.</item>
///   <item><b>Linux</b> — The file's parent directory is opened via <c>xdg-open</c>.
///     Text payloads fall back to a <c>mailto:</c> URL.</item>
/// </list>
/// </remarks>
public interface IShareService
{
    /// <summary>
    /// Opens the native share sheet with a text (and optional URL) payload.
    /// </summary>
    Task ShareTextAsync(string title, string text, string? uri = null);

    /// <summary>
    /// Opens the native share sheet with a file payload.
    /// The file should already exist on disk.
    /// </summary>
    Task ShareFileAsync(string title, string filePath);
}