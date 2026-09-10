namespace Bennewitz.Ninja.OpenCode.Avalonia.Essentials;

/// <summary>
/// An APP-level boolean preference an Essentials card can show, supplied by the host app.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>Every other card on this page is schema-backed</b> — it names a JSON path and reads and
/// writes it through the config client. An app preference has no JSON path: it is not part of
/// the document the user is editing, it lives in the app's own state file, and the app is the
/// only thing that knows where that is. This project cannot reach it: it sits below the app
/// assembly, so it cannot reference its <c>WindowStateService</c>, and reversing that dependency
/// to add one toggle would invert the layering.
/// </para>
/// <para>
/// So the app hands the card everything it needs — including its TEXT, which stays in the app's
/// own resx rather than being duplicated here. That keeps one copy of a string the app also
/// shows elsewhere, and keeps this type free of any particular preference's identity: it
/// describes "a boolean the host owns", not "the update check".
/// </para>
/// <para>
/// ⚠ <see cref="Get"/> is called on every refresh, not cached. A preference the user changes on
/// another surface must be reflected the next time the page is read, and re-reading is cheap
/// next to being stale.
/// </para>
/// </remarks>
/// <param name="Id">Stable card id, used for ordering and for tests to find the card.</param>
/// <param name="Title">Card heading, from the host's resx.</param>
/// <param name="Body">One or two sentences saying what the toggle does, from the host's resx.</param>
/// <param name="Get">Reads the current value.</param>
/// <param name="Set">Persists a new value. Called on every toggle.</param>
public sealed record EssentialsAppPreference(
    string Id,
    string Title,
    string Body,
    Func<bool> Get,
    Action<bool> Set);
