using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Save;

/// <summary>
/// One open product whose pending changes are about to be shown or logged: its client, the name
/// its changes are grouped under, and the danger policy that applies to them.
/// </summary>
/// <param name="Client">The open client holding the dirty documents.</param>
/// <param name="DisplayName">
/// Localized name the changes are grouped under. Resolved per call rather than stored, because it
/// is resource-backed and a cached copy would go stale after a culture change.
/// </param>
/// <param name="Danger">
/// This product's danger policy, or <see langword="null"/> for a product that declares none.
/// </param>
/// <remarks>
/// <para>
/// ⛔⛔ <b>The policy travels PER SOURCE, and that is the whole reason this is a record rather
/// than the <c>(Client, DisplayName)</c> tuple it replaced.</b> A save dialog renders every open
/// product at once — ClaudeForge hands it Claude Code <i>and</i> Claude Desktop in one call — so a
/// single classifier parameter on the builder would label one product's pending writes with the
/// other product's threat model. The two schemas share almost no key names, which makes the
/// failure quiet: most rows simply lose their dot, and any name that collides (<c>env</c> is in
/// both) gets a confident mislabel. Same hazard, same answer, as
/// <c>ClaudeEditorFactoryConfig.CreateDefault</c> and OpenCode's per-document factories.
/// </para>
/// <para>
/// <see cref="Danger"/> is optional so a caller that has no policy — tests, a product with no
/// table — constructs one positionally and gets today's behaviour: no severity anywhere.
/// </para>
/// </remarks>
public sealed record DirtySource(
    AgentConfigClientCore Client,
    string DisplayName,
    IDangerClassifier? Danger = null);
