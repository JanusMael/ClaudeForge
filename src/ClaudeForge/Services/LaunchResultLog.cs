using Bennewitz.Ninja.AppServices.Abstractions;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Services;

/// <summary>
/// Logs what an <see cref="IShellLauncher"/> call reported, the same way at every site that used to
/// fire it and forget. The previous API returned <c>void</c> or a bare <c>bool</c>, so none of these
/// outcomes ever reached the log.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <see cref="LaunchStatus.Unsupported"/> is information, never a failure: it describes the
/// platform, not this click. On the three desktop platforms ClaudeForge ships to, the reveal,
/// open-in-editor and URL members return it only for a <see cref="PlatformNotSupportedException"/>,
/// which those platforms do not raise for these calls — so these sites log it rather than hide
/// their affordance. The terminal launch is different, returns it for real when no terminal is
/// installed, and is handled at its own site.
/// </para>
/// <para>
/// One helper rather than a switch at each of the six sites: a switch written six times is six
/// chances to answer the same status differently.
/// </para>
/// </remarks>
internal static class LaunchResultLog
{
    /// <summary>Logs <paramref name="result"/> unless it succeeded or was cancelled.</summary>
    /// <param name="context">A short tag naming the site, e.g. <c>[About] Reveal config</c>.</param>
    /// <param name="result">What the launcher reported.</param>
    public static void Report(string context, LaunchResult result)
    {
        switch (result.Status)
        {
            case LaunchStatus.Succeeded:
            case LaunchStatus.Cancelled:
                return;

            case LaunchStatus.Unsupported:
                Log.Information("{Context}: unsupported on this platform ({Detail})", context, result.Detail);
                return;

            default:
                Log.Warning("{Context}: {Status} ({Detail})", context, result.Status, result.Detail);
                return;
        }
    }
}
