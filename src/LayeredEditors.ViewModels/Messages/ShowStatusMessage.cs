namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Messages;

/// <summary>How the shell should render a <see cref="ShowStatusMessage"/>.</summary>
public enum StatusSeverity
{
    /// <summary>A completed action — green tick, auto-clears.</summary>
    Success,

    /// <summary>Something didn't work but nothing is broken — amber, auto-clears.</summary>
    Warning,
}

/// <summary>
/// Sent via <see cref="CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger"/> when a
/// page-level view-model needs to say something in the application's centre status bar.
///
/// <para>
/// Exists because the status bar belongs to the shell (<c>MainWindowViewModel</c> owns
/// the typed <c>SetStatus*</c> helpers), while the things worth announcing — "deep link
/// copied", "that link didn't resolve" — happen inside individual editors. An editor
/// raising this instead of reaching for the shell keeps the dependency pointing one way.
/// </para>
/// <para>
/// Deliberately carries a <see cref="StatusSeverity"/> rather than a pre-rendered
/// string style: the shell decides how each severity looks, so the pill, icon, and
/// auto-clear lifecycle stay consistent with every other status emission.
/// </para>
/// </summary>
/// <param name="Text">Already-localized text to display.</param>
/// <param name="Severity">How the shell should render it.</param>
public sealed record ShowStatusMessage(string Text, StatusSeverity Severity);
