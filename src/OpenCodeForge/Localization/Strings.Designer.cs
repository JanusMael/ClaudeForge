// ⚠ The compiler treats any *.Designer.cs as auto-generated, which switches OFF the project's
// nullable context — so nullable annotations here need this directive or they are CS8669.
// The file name is not cosmetic: it is also what exempts this file from the repo's resx
// dynamic-access guard.
#nullable enable

using System.Globalization;
using System.Resources;

namespace Bennewitz.Ninja.OpenCodeForge.Localization;

/// <summary>
/// This app's user-facing strings, read from <c>Strings.resx</c>.
/// </summary>
/// <remarks>
/// <para>
/// Hand-maintained, but deliberately named <c>Strings.Designer.cs</c>: that is the file name the
/// repo's resx guard exempts from its dynamic-access check, because this file IS the accessor the
/// guard wants every other file to go through. <c>PublicResXFileCodeGenerator</c> only runs inside
/// an IDE, so the alternative is a generated file that silently drifts whenever the resource is
/// edited outside one.
/// </para>
/// <para>
/// ⚠ <b>Every key must also be referenced somewhere as a literal <c>Strings.Key</c>.</b> The dead-
/// key guard scans for exactly that form, so a key reachable only through this file's own
/// <c>nameof</c> would be reported unused and deleted. All seven are referenced by the app.
/// </para>
/// <para>
/// ⚠ English-only today, and structured so that is a translation gap rather than a code change:
/// adding <c>Strings.&lt;culture&gt;.resx</c> beside the resource is sufficient, since the csproj
/// deliberately leaves <c>SatelliteResourceLanguages</c> unset.
/// </para>
/// <para>
/// ⚠ The resource base name is a literal. It must match <c>RootNamespace</c> plus this folder, and
/// a mismatch fails at RUNTIME with a missing-manifest exception rather than at build time — which
/// is why <c>OpenCodeForgeStringsTests</c> asserts every key resolves to something other than its
/// own name.
/// </para>
/// </remarks>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("Bennewitz.Ninja.OpenCodeForge.Localization.Strings", typeof(Strings).Assembly);

    /// <summary>Overrides the lookup culture. Test seam; null follows the UI culture.</summary>
    internal static CultureInfo? CultureOverride { get; set; }

    private static string Get(string key) =>
        Manager.GetString(key, CultureOverride ?? CultureInfo.CurrentUICulture) ?? key;

    /// <summary>The application's display name.</summary>
    public static string AppTitle => Get(nameof(AppTitle));

    /// <summary>Header for a settings page's properties tab.</summary>
    public static string HeaderTabProperties => Get(nameof(HeaderTabProperties));

    /// <summary>Header for a settings page's effective-value tab.</summary>
    public static string HeaderTabEffective => Get(nameof(HeaderTabEffective));

    /// <summary>Header for the JSON tab when every schema key is shown.</summary>
    public static string HeaderTabJsonAll => Get(nameof(HeaderTabJsonAll));

    /// <summary>Header for the JSON tab when only keys present in the file are shown.</summary>
    public static string HeaderTabJsonActive => Get(nameof(HeaderTabJsonActive));

    /// <summary>Navigation header for the main OpenCode configuration section.</summary>
    public static string SectionOpenCode => Get(nameof(SectionOpenCode));

    /// <summary>Navigation header for the terminal-UI configuration section.</summary>
    public static string SectionOpenCodeTui => Get(nameof(SectionOpenCodeTui));
}
