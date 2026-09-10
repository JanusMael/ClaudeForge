namespace Bennewitz.Ninja.AgentForge.Core.Schema;

/// <summary>
/// Forces schema loading down one branch of the chain, for diagnosis.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Both states are otherwise hard to reach on purpose.</b> Network-first means a healthy
/// machine always exercises the fetch, so the bundled path — the one every offline, throttled or
/// firewalled user gets — is the one a developer never sees. Unplugging the network to test it
/// also disables everything else, which is why this exists as a switch rather than a suggestion
/// to pull the cable.
/// </para>
/// <para>
/// ⚠ <b><see cref="Fetched"/> makes a failed fetch FATAL rather than falling back.</b> That is the
/// point: a "test the fetched path" run that silently used bundled instead proves nothing, and the
/// resulting screenshot would be indistinguishable from success. The load throws
/// <see cref="SchemaUnavailableException"/> so the failure is impossible to mistake.
/// </para>
/// </remarks>
public enum SchemaSourceOverride
{
    /// <summary>
    /// Skip the network entirely and load the copy embedded in the binary.
    /// </summary>
    /// <remarks>
    /// Equivalent to what every offline user gets, without touching the machine's networking.
    /// </remarks>
    Bundled,

    /// <summary>
    /// Require the fetched copy: no bundled fallback, and a failure throws.
    /// </summary>
    Fetched,
}
