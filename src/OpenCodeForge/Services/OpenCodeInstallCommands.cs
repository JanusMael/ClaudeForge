using System.Runtime.InteropServices;

namespace Bennewitz.Ninja.OpenCodeForge.Services;

/// <summary>Which family of Linux distribution is running, for picking a package manager.</summary>
public enum LinuxFamily
{
    /// <summary>Not Linux, or the distribution could not be identified.</summary>
    Unknown = 0,

    /// <summary>Arch and derivatives — <c>pacman</c>.</summary>
    Arch,

    /// <summary>Debian, Ubuntu and derivatives — <c>apt</c>.</summary>
    Debian,

    /// <summary>Fedora, RHEL and derivatives — <c>dnf</c>.</summary>
    Fedora,
}

/// <summary>One way to install the agent, as shown in the install banner.</summary>
/// <param name="Label">Short name of the method, e.g. "Homebrew".</param>
/// <param name="Command">The exact command to run.</param>
/// <param name="Note">Optional caveat the user needs before running it.</param>
public sealed record InstallOption(string Label, string Command, string? Note = null);

/// <summary>
/// The install commands offered when the agent is not detected.
/// </summary>
/// <remarks>
/// <para>
/// ⚠⚠ <b>These are SOURCED, NOT VERIFIED.</b> Spike S10 took them from the vendor docs at the
/// v1.17.9 tag rather than guessing, and its own condition still stands: <b>each must be run on a
/// clean machine before shipping.</b> A wrong command in a prominent banner is a
/// high-visibility bug, which is why nothing here is invented.
/// </para>
/// <para>
/// ⛔ <b>There is no APT package and no DNF package.</b> The docs list a distribution package for
/// Arch only. Debian/Ubuntu and Fedora users are therefore offered the vendor's own installer
/// script — which the docs call the recommended path on any platform — rather than a
/// <c>sudo apt install opencode</c> that would simply fail. If a native package appears later,
/// add it here; do not add one on the assumption that it exists.
/// </para>
/// <para>
/// ⚠ <b>Homebrew must use the tap, not the plain formula.</b> The docs state the plain
/// <c>opencode</c> formula lags behind, so the command is
/// <c>brew install anomalyco/tap/opencode</c>.
/// </para>
/// <para>
/// ⚠ <b>No winget package exists</b> — notable, since this editor itself ships via winget. And on
/// Windows the vendor recommends WSL for full feature compatibility, so the Windows options carry
/// that note; omitting it would be misleading.
/// </para>
/// </remarks>
public static class OpenCodeInstallCommands
{
    /// <summary>The vendor's own installer, documented as the recommended path on any platform.</summary>
    private static readonly InstallOption VendorScript = new(
        "Official installer",
        "curl -fsSL https://opencode.ai/install | bash");

    /// <summary>Works on macOS and Linux alike.</summary>
    private static readonly InstallOption Homebrew = new(
        "Homebrew",
        "brew install anomalyco/tap/opencode",
        "Installs from the vendor's tap. The plain 'opencode' formula lags behind.");

    private static readonly InstallOption Npm = new(
        "npm",
        "npm install -g opencode-ai");

    /// <summary>Options for the running platform, best first.</summary>
    public static IReadOnlyList<InstallOption> ForCurrentPlatform() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ForWindows()
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ForMacOs()
        : ForLinux(DetectLinuxFamily());

    /// <summary>macOS: Homebrew first, as requested.</summary>
    public static IReadOnlyList<InstallOption> ForMacOs() => [Homebrew, VendorScript, Npm];

    /// <summary>
    /// Linux, by distribution family.
    /// </summary>
    /// <remarks>
    /// Only Arch gets a distribution package, because only Arch has one. The other families get
    /// the installer script first — it is the documented recommendation and it works everywhere —
    /// with Homebrew and npm as alternatives for users who already have them.
    /// </remarks>
    public static IReadOnlyList<InstallOption> ForLinux(LinuxFamily family) => family switch
    {
        LinuxFamily.Arch =>
        [
            new InstallOption("pacman", "sudo pacman -S opencode"),
            new InstallOption("AUR", "paru -S opencode-bin", "Development build from the AUR."),
            VendorScript,
        ],

        // Debian and Fedora deliberately share the same list: neither has a native package, so
        // presenting them differently would imply a distinction that does not exist.
        LinuxFamily.Debian or LinuxFamily.Fedora or LinuxFamily.Unknown =>
        [
            VendorScript,
            Homebrew,
            Npm,
        ],

        var _ => [VendorScript],
    };

    /// <summary>
    /// Windows. Sourced from the docs, which list no winget package and recommend WSL.
    /// </summary>
    public static IReadOnlyList<InstallOption> ForWindows() =>
    [
        new InstallOption("Chocolatey", "choco install opencode",
            "The vendor recommends WSL on Windows for full feature compatibility."),
        new InstallOption("Scoop", "scoop install opencode",
            "The vendor recommends WSL on Windows for full feature compatibility."),
        Npm with { Note = "The vendor recommends WSL on Windows for full feature compatibility." },
    ];

    /// <summary>Identify the running distribution family.</summary>
    public static LinuxFamily DetectLinuxFamily()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return LinuxFamily.Unknown;
        }

        try
        {
            // /usr/lib/os-release is the fallback the spec defines when /etc is absent, which is
            // the case on some immutable and container images.
            foreach (string path in new[] { "/etc/os-release", "/usr/lib/os-release" })
            {
                if (File.Exists(path))
                {
                    return ParseOsRelease(File.ReadAllText(path));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable os-release is not worth failing over: Unknown yields the installer
            // script, which works on every distribution anyway.
            _ = ex;
        }

        return LinuxFamily.Unknown;
    }

    /// <summary>
    /// Map <c>os-release</c> content to a family.
    /// </summary>
    /// <remarks>
    /// <c>ID</c> is checked first, then <c>ID_LIKE</c> — that is what makes derivatives work:
    /// Linux Mint reports <c>ID=linuxmint</c> with <c>ID_LIKE="ubuntu debian"</c>, and Manjaro
    /// reports <c>ID_LIKE=arch</c>. Matching only <c>ID</c> would leave every derivative Unknown.
    /// Values may be quoted and space-separated, per the spec.
    /// </remarks>
    internal static LinuxFamily ParseOsRelease(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        string? id = null;
        string? idLike = null;

        foreach (string raw in content.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("ID=", StringComparison.Ordinal))
            {
                id = Unquote(line["ID=".Length..]);
            }
            else if (line.StartsWith("ID_LIKE=", StringComparison.Ordinal))
            {
                idLike = Unquote(line["ID_LIKE=".Length..]);
            }
        }

        foreach (string token in Tokens(id).Concat(Tokens(idLike)))
        {
            switch (token)
            {
                case "arch" or "archarm" or "manjaro" or "endeavouros":
                    return LinuxFamily.Arch;
                case "debian" or "ubuntu" or "raspbian" or "linuxmint" or "pop":
                    return LinuxFamily.Debian;
                case "fedora" or "rhel" or "centos" or "rocky" or "almalinux":
                    return LinuxFamily.Fedora;
            }
        }

        return LinuxFamily.Unknown;

        static IEnumerable<string> Tokens(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? []
                : value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Select(t => t.ToLowerInvariant());

        static string Unquote(string value) => value.Trim().Trim('"', '\'');
    }
}
