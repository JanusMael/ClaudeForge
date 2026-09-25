using System.Xml.Linq;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// No project declares <c>&lt;TargetFrameworks&gt;</c> while the repo root sets the singular
/// <c>&lt;TargetFramework&gt;</c>, because the plural form is then silently ignored.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>Three projects did, and all three were dead for the life of the declaration.</b>
/// <c>src/ClaudeForge</c>, <c>src/OpenCodeForge</c> and
/// <c>src/LayeredEditors.Avalonia.Services</c> each added
/// <c>net10.0-windows10.0.19041.0</c> so <c>DefaultShareService</c> could compile against MAUI
/// Essentials. MSBuild cross-targets only when <c>TargetFramework</c> is EMPTY, and
/// <c>Directory.Build.props</c> sets it — so the plural form never took effect, the Windows TFM
/// was never built, and no shipped binary has ever contained that code. All three csproj files
/// carried comments asserting the opposite.
/// </para>
/// <para>
/// ⭐ <b>Nothing failed, which is the whole problem.</b> The build succeeded, the suite stayed
/// green, and <c>bin/Release/</c> quietly held one TFM directory instead of two. It surfaced only
/// because <c>dotnet pack</c> reads <c>TargetFrameworks</c> for the nuspec while the build honours
/// the singular, and produced NU5026 for a file no build had ever written.
/// </para>
/// <para>
/// ⚠ <b>This is not a ban on multi-targeting.</b> It is a ban on declaring it in a way that does
/// nothing. A project that genuinely needs a second TFM clears the inherited property first —
/// <c>&lt;TargetFramework&gt;&lt;/TargetFramework&gt;</c> ahead of the plural form — and this test
/// accepts exactly that.
/// </para>
/// </remarks>
public sealed class SingleTargetFrameworkTests
{
    [Fact]
    public void NoProjectDeclaresAnIgnoredTargetFrameworksList()
    {
        string repoRoot = FindRepoRoot();

        // The premise. If the root ever stops setting the singular form, the plural becomes
        // effective everywhere and this guard is measuring nothing — so it fails rather than
        // passes, and whoever made that change reads why.
        string rootProps = File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props"));
        Assert.True(
            XDocument.Parse(rootProps).Descendants()
                .Any(e => e.Name.LocalName == "TargetFramework" && !string.IsNullOrWhiteSpace(e.Value)),
            "Directory.Build.props no longer sets a singular <TargetFramework>. That is the only "
            + "reason a plural <TargetFrameworks> is ignored, so this guard's premise is gone: "
            + "re-read it before deleting it.");

        List<string> offenders = [];
        int scanned = 0;

        foreach (string csproj in EnumerateProjects(repoRoot))
        {
            scanned++;
            XDocument doc = XDocument.Load(csproj);

            bool declaresPlural = doc.Descendants()
                .Any(e => e.Name.LocalName == "TargetFrameworks" && !string.IsNullOrWhiteSpace(e.Value));

            if (!declaresPlural)
            {
                continue;
            }

            // The sanctioned escape: clear the inherited singular, then multi-target.
            bool clearsSingular = doc.Descendants()
                .Any(e => e.Name.LocalName == "TargetFramework" && string.IsNullOrWhiteSpace(e.Value));

            if (!clearsSingular)
            {
                offenders.Add(Path.GetRelativePath(repoRoot, csproj));
            }
        }

        Assert.True(scanned > 0,
            "Scanned no project files; the scan has been narrowed to nothing.");

        MessageAssert.Equal(0, offenders.Count,
            "These projects declare <TargetFrameworks> without clearing the inherited singular "
            + "<TargetFramework>, so MSBuild ignores the list and builds ONE framework. Nothing "
            + "will fail — the extra TFM simply never gets built, as a Windows TFM silently did "
            + "not for three projects. Add <TargetFramework></TargetFramework> before the plural "
            + "form. Offenders: " + string.Join(", ", offenders));
    }

    /// <summary>Every <c>.csproj</c> under <c>src/</c> and <c>tests/</c>, excluding build output.</summary>
    private static IEnumerable<string> EnumerateProjects(string repoRoot)
        => new[] { "src", "tests" }
            .Select(d => Path.Combine(repoRoot, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.csproj", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>Matches <c>BuildFilePathIntegrityTests.FindRepoRoot()</c>.</summary>
    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "tests")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root by walking up from '{AppContext.BaseDirectory}'.");
    }
}
