using System.Xml.Linq;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Both shipping apps trim on a mode Avalonia actually supports, and each one carries the
/// <c>TrimmableAssembly</c> entry that mode requires.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b><c>TrimMode=link</c> is UNSUPPORTED BY AVALONIA and nothing in the build says so.</b>
/// <see href="https://github.com/AvaloniaUI/Avalonia/issues/16697">AvaloniaUI/Avalonia#16697</see>
/// states that COM interop is not supported under it and reports access violations in
/// <c>UiaReturnRawElementProvider</c> for users running Magnifier or a screen reader. The issue's
/// own summary is the reason this guard exists: applications "build successfully with minimal
/// warnings, creating a false sense of security". A clean six-RID publish matrix is exactly that
/// false sense — it ran green for months over this setting.
/// </para>
/// <para>
/// ⚠ <b>The two apps drifting apart is the specific failure this closes.</b> OpenCodeForge once
/// published UNTRIMMED while CI reported a green "Trim Check", because its csproj lacked the
/// block ClaudeForge had. A guard that checks one app would not have caught that, so this checks
/// both from one list.
/// </para>
/// <para>
/// ⭐ <b><c>TrimmableAssembly</c> is not decoration — without it the publish FAILS.</b> Under
/// <c>partial</c>, an assembly that is not marked trimmable is copied whole, so
/// <c>Avalonia.DesignerSupport</c>'s unreachable remote-designer entry point stops being dead code,
/// gets analysed, and its <c>IL2026</c>/<c>IL2072</c>/<c>IL2075</c> escalate to
/// <c>NETSDK1144: Optimizing assemblies for size failed</c>. Marking that one assembly trimmable
/// restores the <c>link</c> treatment for it alone: the dead code is removed again rather than the
/// warnings suppressed over code that would still ship.
/// </para>
/// <para>
/// ⓘ <b>This guard is about a SUPPORTED CONFIGURATION, not about an accessibility defect.</b> The
/// trimmed <c>link</c> build exposed a full UIA tree (168 descendants, measured); <c>F5</c> in
/// <c>docs/RETEST-FINDINGS.md</c> claimed otherwise and is refuted. Do not re-derive a
/// justification for this test from that finding.
/// </para>
/// </remarks>
public sealed class TrimModeIntegrityTests
{
    /// <summary>The apps that publish a trimmed, self-contained artifact.</summary>
    // TWO-APP GUARD NARROWED — plans/00003 Phase 0. "OpenCodeForge" was here; restore it when
    // OpenCodeForge rejoins. ⚠ TheTwoApps_DoNotDriftApartOnTrimSettings indexes [0] and [1] and
    // therefore cannot run with one entry — it is skipped below rather than weakened, because a
    // drift check between one app and itself is a test that cannot fail.
    private static readonly string[] ShippingApps =
    {
        "ClaudeForge",
    };

    private const string RequiredTrimMode = "partial";
    private const string DesignerSupport = "Avalonia.DesignerSupport";

    [Fact]
    public void BothApps_TrimOnASupportedMode_NotLink()
    {
        string repoRoot = FindRepoRoot();
        List<string> failures = [];

        foreach (string app in ShippingApps)
        {
            XDocument csproj = LoadAppCsproj(repoRoot, app);

            string[] modes = csproj.Descendants()
                .Where(e => e.Name.LocalName == "TrimMode")
                .Select(e => e.Value.Trim())
                .ToArray();

            // The premise: a missing element is not a pass. TrimMode defaults to `full` for a
            // trimmed publish, which is `link` under its other name — so silence here is the
            // unsupported configuration, not the absence of one.
            if (modes.Length == 0)
            {
                failures.Add(
                    $"  • {app}.csproj declares no <TrimMode>. That is not neutral: a trimmed "
                    + $"publish defaults to full/link, the mode Avalonia does not support. "
                    + $"Set <TrimMode>{RequiredTrimMode}</TrimMode>.");
                continue;
            }

            foreach (string mode in modes)
            {
                if (!string.Equals(mode, RequiredTrimMode, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"  • {app}.csproj sets <TrimMode>{mode}</TrimMode>. Avalonia does not "
                        + $"support 'link'/'full' (AvaloniaUI/Avalonia#16697: COM interop "
                        + $"unsupported, access violations under Magnifier / screen readers), and "
                        + $"the build emits no warning about it. Expected '{RequiredTrimMode}'.");
                }
            }
        }

        MessageAssert.Equal(
            0,
            failures.Count,
            "Trim mode must be supported by Avalonia in every shipping app:\n"
            + string.Join("\n", failures));
    }

    [Fact]
    public void BothApps_MarkDesignerSupportTrimmable_OrThePublishCannotSucceed()
    {
        string repoRoot = FindRepoRoot();
        List<string> failures = [];

        foreach (string app in ShippingApps)
        {
            XDocument csproj = LoadAppCsproj(repoRoot, app);

            bool marked = csproj.Descendants()
                .Where(e => e.Name.LocalName == "TrimmableAssembly")
                .Select(e => e.Attribute("Include")?.Value?.Trim())
                .Any(v => string.Equals(v, DesignerSupport, StringComparison.Ordinal));

            if (!marked)
            {
                failures.Add(
                    $"  • {app}.csproj does not mark {DesignerSupport} trimmable. Under "
                    + $"TrimMode={RequiredTrimMode} that assembly is copied WHOLE, its unreachable "
                    + $"remote-designer entry point becomes analysable, and the publish dies with "
                    + $"NETSDK1144. Add <TrimmableAssembly Include=\"{DesignerSupport}\"/> rather "
                    + $"than suppressing its IL2026/IL2072/IL2075 — suppression would silence a "
                    + $"real analysis over code that would then still be shipped.");
            }
        }

        MessageAssert.Equal(
            0,
            failures.Count,
            $"TrimMode={RequiredTrimMode} requires the {DesignerSupport} entry:\n"
            + string.Join("\n", failures));
    }

    /// <summary>
    /// The two apps agree, rather than each being independently correct today.
    /// </summary>
    /// <remarks>
    /// ⚠ Kept separate from the two above on purpose. Those assert an absolute value, so if the
    /// supported mode ever changes they must both be edited and could be edited one at a time;
    /// this one fails the moment the pair stops matching, which is the state that let
    /// OpenCodeForge publish untrimmed under a green CI check.
    /// </remarks>
    [Fact]
    public void TheTwoApps_DoNotDriftApartOnTrimSettings()
    {
        string repoRoot = FindRepoRoot();

        // TWO-APP GUARD NARROWED — plans/00003 Phase 0. There is one shipping app on this branch,
        // so there is no pair to compare. ⛔ Inconclusive rather than a trivial pass: a drift check
        // between one app and itself would report success without taking a measurement, which is
        // the shape of test this class was written to replace.
        if (ShippingApps.Length < 2)
        {
            Assert.Skip(
                "Needs two shipping apps; this branch has "
                + ShippingApps.Length
                + ". Restore OpenCodeForge to ShippingApps when it rejoins.");
        }

        string[] modes = ShippingApps
            .Select(app => LoadAppCsproj(repoRoot, app)
                .Descendants()
                .Where(e => e.Name.LocalName == "TrimMode")
                .Select(e => e.Value.Trim())
                .FirstOrDefault() ?? "(none)")
            .ToArray();

        MessageAssert.Equal(
            modes[0],
            modes[1],
            $"{ShippingApps[0]} and {ShippingApps[1]} must trim identically, and they no longer do "
            + $"({ShippingApps[0]}='{modes[0]}', {ShippingApps[1]}='{modes[1]}'). Divergent trim "
            + "settings are how this repo once shipped one app untrimmed while CI reported a green "
            + "trim check.");
    }

    private static XDocument LoadAppCsproj(string repoRoot, string app)
    {
        string path = Path.Combine(repoRoot, "src", app, app + ".csproj");
        Assert.True(
            File.Exists(path),
            $"Expected an app project at '{path}'. If the app was renamed or removed, update "
            + $"{nameof(ShippingApps)} in the same commit — deliberately, not by accident.");

        return XDocument.Load(path);
    }

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
