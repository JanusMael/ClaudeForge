using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Internal;

/// <summary>
/// Internal-only snapshot of one dirty workspace document — exposed via
/// <c>ClaudeConfigClientCore.SnapshotDirtyDocuments()</c> so the GUI's
/// save-confirmation dialog, change-log, and pre-save B4Forge backup
/// helpers can introspect what's about to be written without reaching
/// for <c>workspace.DirtyDocuments()</c> directly.
/// </summary>
/// <param name="FilePath">Absolute on-disk path of the document.</param>
/// <param name="Scope">Which scope the document writes to.</param>
/// <param name="BaselineRoot">
/// Snapshot of the document's <em>last-saved</em> root (deep clone). The
/// caller can compare against <see cref="CurrentRoot"/> to compute a diff
/// without racing against further mutations.
/// </param>
/// <param name="CurrentRoot">
/// Snapshot of the in-memory root (deep clone) at the moment the snapshot
/// was taken.
/// </param>
/// <remarks>
/// <para>
/// 4.3.7 step 9: lifted out of <c>MainWindowViewModel</c>'s direct
/// <c>workspace.DirtyDocuments()</c> calls. Kept <c>internal</c> rather
/// than promoted to the public SDK surface because:
/// </para>
/// <list type="bullet">
///   <item>
///     <see cref="JsonObject"/> in a public return type would violate the
///     <c>PublicSurfaceContractTests.NoPublicApi_LeaksSystemTextJsonNodes</c>
///     contract. Promoting this would require designing a JSON-string-or-typed-diff
///     return shape, which is open-ended work that should land when an
///     external (MCP / CLI) consumer actually needs a dirty-doc surface.
///   </item>
///   <item>
///     The current consumers are all GUI-internal (logging, dialog text,
///     B4Forge snapshot). The <c>InternalsVisibleTo("ClaudeForge")</c>
///     grant on the SDK csproj covers them.
///   </item>
/// </list>
/// </remarks>
internal sealed record DirtyDocumentSnapshot(
    string FilePath,
    ConfigScope Scope,
    JsonObject BaselineRoot,
    JsonObject CurrentRoot);