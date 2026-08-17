using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

/// <summary>
/// Turns a desired config root into the text to write to disk.
/// </summary>
/// <remarks>
/// <para>
/// Exists so the comment-preserving writer can be swapped for the older
/// whole-document re-serializer at runtime. That escape hatch is deliberate: this is
/// the highest-consequence code path in the product — a bug here corrupts user config
/// — so one release ships with a one-command way back.
/// </para>
/// <para>
/// The interface lives here rather than beside its implementations because the
/// selection happens in the app (which parses the flag) while the call site is deep in
/// the config loader. Neither can reference the other, and this is the seam that lets
/// both depend on something neutral instead.
/// </para>
/// </remarks>
public interface IConfigWriter
{
    /// <summary>
    /// Produce the full file text for <paramref name="root"/>.
    /// </summary>
    /// <param name="originalText">
    /// The file's current on-disk text, or <see langword="null"/> when it does not exist
    /// yet. An edit-based writer needs this to know what it is allowed to leave alone; a
    /// re-serializing writer ignores it.
    /// </param>
    /// <param name="baselineRoot">
    /// The root as it was when <paramref name="originalText"/> was read, so a writer can
    /// tell which paths actually changed. <see langword="null"/> when unknown, which a
    /// writer must treat as "assume everything changed" rather than "nothing did".
    /// </param>
    /// <param name="root">The desired root.</param>
    /// <param name="headerComment">
    /// Optional provenance stamp, or <see langword="null"/> to write none. Where and how
    /// it lands is the writer's business — Claude's schema tolerates a <c>"//"</c> JSON
    /// key, while a JSONC target can carry a real comment.
    /// </param>
    /// <returns>The complete text to write.</returns>
    string Render(string? originalText, JsonObject? baselineRoot, JsonObject root, string? headerComment);

    /// <summary>
    /// Short identifier for logs and diagnostics — e.g. <c>"jsonc"</c>, <c>"legacy"</c>.
    /// </summary>
    /// <remarks>
    /// Worth having because the two writers produce different bytes for identical input.
    /// A bug report that does not say which one ran is much harder to act on.
    /// </remarks>
    string Name { get; }
}
