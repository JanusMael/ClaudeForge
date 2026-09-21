using System.Xml.Linq;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// A shipping publish states which reference mode produced it, and the release cannot quietly
/// leave package mode.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>The founding defect was a comment standing in for a guard.</b> Two places in this
/// repository — <c>.github/workflows/ci.yml</c> and <c>scripts/package-canary.ps1</c> — called
/// package mode "the mode the release publishes from". Nothing set the switch, so every RID of
/// every release ever cut used <c>ProjectReference</c>, and no build, test or canary ever
/// disagreed. <c>GuardShippingPublishUsesPackages</c> in the root <c>Directory.Build.targets</c>
/// is the guard that claim never had; this class is the guard on the guard.
/// </para>
/// <para>
/// ⭐ <b>The most valuable assertion here is the one about the RELEASE path taking no hatch.</b>
/// The obvious way to "fix" a release that fails the new guard — because the feed is down, or a
/// token expired — is to add <c>-p:AllowProjectReferencePublish=true</c> to
/// <c>Publish-Rid.ps1</c> and move on. That would restore the original defect exactly, and the
/// build would go green while doing it. Everything else in this file is cheap; that test is the
/// point.
/// </para>
/// <para>
/// See <c>plans/00003-release-built-from-shared-packages.md</c>, Phase D step D3.
/// </para>
/// </remarks>
[TestClass]
public sealed class ShippingPublishModeTests
{
    /// <summary>The MSBuild target under guard.</summary>
    private const string GuardTargetName = "GuardShippingPublishUsesPackages";

    /// <summary>The property that selects package mode.</summary>
    private const string PackageModeProperty = "UseSharedPackages";

    /// <summary>The one named way out, which the evidence records.</summary>
    private const string HatchProperty = "AllowProjectReferencePublish";

    /// <summary>
    /// The premise: the guard exists, hangs off the publish pipeline rather than the build, and
    /// fails when neither package mode nor the hatch is set.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The hook is asserted because the wrong one fails SILENTLY.</b> A target hooked on a
    /// target name that does not exist is not an error — MSBuild simply never runs it, the publish
    /// succeeds, and the guard reports nothing forever. The same is true of a condition that stops
    /// selecting this project. Neither shows up as a failure anywhere.
    /// </remarks>
    [TestMethod]
    public void TheGuardExistsAndHangsOffThePublishPipeline()
    {
        XElement guard = LoadGuardTarget();

        string hook = guard.Attribute("BeforeTargets")?.Value ?? string.Empty;
        Assert.IsTrue(
            hook.Contains("Publish", StringComparison.Ordinal),
            $"'{GuardTargetName}' must hang off a PUBLISH target, and its BeforeTargets is "
            + $"'{hook}'. Hooked on a build target it would fail every developer's Release build; "
            + "hooked on a name that does not exist it would never run at all, silently, which is "
            + "the state this guard was written to end.");

        XElement? error = guard.Elements().FirstOrDefault(e => e.Name.LocalName == "Error");
        Assert.IsNotNull(error,
            $"'{GuardTargetName}' has no <Error>. A target that only prints is not a guard: the "
            + "release would go on publishing from ProjectReference while announcing that it had.");

        string errorCondition = error.Attribute("Condition")?.Value ?? string.Empty;
        Assert.IsTrue(
            errorCondition.Contains(PackageModeProperty, StringComparison.Ordinal)
            && errorCondition.Contains(HatchProperty, StringComparison.Ordinal),
            $"The <Error> condition must name both '{PackageModeProperty}' and '{HatchProperty}', "
            + $"and it reads '{errorCondition}'. Naming only the first leaves no way out and every "
            + "credential-free publish fails; naming only the second guards nothing at all.");
    }

    /// <summary>
    /// The good path announces itself, so an archived log never has to be read by the ABSENCE of
    /// something.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>Absence is not evidence, and this is where that bites.</b> If the guard were silent in
    /// package mode, a log proving "the release used the packages" would be a log with no hatch
    /// line in it — which is indistinguishable from a log produced when the target was renamed,
    /// when its condition stopped matching, or when the publish never reached it. The positive
    /// line is what makes the archived provenance readable without re-deriving anything.
    /// </remarks>
    [TestMethod]
    public void BothModesAnnounceThemselvesAtHighImportance()
    {
        XElement guard = LoadGuardTarget();

        List<XElement> messages = guard.Elements()
            .Where(e => e.Name.LocalName == "Message")
            .ToList();

        Assert.AreEqual(2, messages.Count,
            $"'{GuardTargetName}' must carry exactly two <Message> elements — one naming the hatch "
            + "when it is taken, one stating package mode when it is not — and it carries "
            + $"{messages.Count}. Dropping the package-mode line is the regression that makes an "
            + "archived log prove the release's mode only by what is missing from it.");

        foreach (XElement message in messages)
        {
            string importance = message.Attribute("Importance")?.Value ?? string.Empty;
            Assert.AreEqual("high", importance,
                "Every mode announcement must be Importance=\"high\". Publish-Rid.ps1 and the CI "
                + "jobs run at default verbosity, where a normal-importance message is not printed "
                + "— so the evidence would exist and never reach the log that archives it.");
        }

        string texts = string.Concat(messages.Select(m => m.Attribute("Text")?.Value));
        Assert.IsTrue(texts.Contains(HatchProperty, StringComparison.Ordinal),
            $"Neither <Message> names '{HatchProperty}'. The plan's requirement is that a departure "
            + "from package mode names ITSELF; a line saying only that something was unusual sends "
            + "the reader looking for which knob was turned.");
    }

    /// <summary>
    /// ⭐ The one that matters: the release path asks for package mode and takes no hatch.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Comment lines are stripped before the hatch check, deliberately.</b> The hatch is worth
    /// explaining wherever it is relevant, and a test that forbade the WORD would push the next
    /// author into removing the explanation rather than the flag — punishing documentation and
    /// catching nothing. What must not appear is a live argument.
    /// </remarks>
    [TestMethod]
    public void TheReleasePublishTakesPackageModeAndNoHatch()
    {
        string script = ReadRepoFile(Path.Combine("src", "publish", "Publish-Rid.ps1"));

        Assert.IsTrue(
            script.Contains($"-p:{PackageModeProperty}=true", StringComparison.Ordinal),
            "Publish-Rid.ps1 holds the only 'dotnet publish' in the release chain, and it no longer "
            + $"passes -p:{PackageModeProperty}=true. Without that flag every RID of the release is "
            + "built from ProjectReference — which is precisely the defect plans/00003 exists for, "
            + "and it was true for the entire life of the feature while two comments denied it.");

        string[] live = script
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith('#'))
            .ToArray();

        string[] offending = live
            .Where(line => line.Contains(HatchProperty, StringComparison.Ordinal))
            .Select(line => line.Trim())
            .ToArray();

        Assert.AreEqual(0, offending.Length,
            $"Publish-Rid.ps1 passes '{HatchProperty}' on a live line: "
            + string.Join(" | ", offending)
            + ". The hatch exists for the CI trim gate and the boot smoke, which have no feed "
            + "credentials. Handing it to the RELEASE restores the original defect exactly, and "
            + "does it while the build stays green — the shipped app would be built from project "
            + "references again, with the run's own log announcing that nobody read.");
    }

    /// <summary>
    /// Every publish that is NOT the release names its departure, rather than being exempted
    /// somewhere the reader cannot see.
    /// </summary>
    [TestMethod]
    [DataRow("src/publish/Smoke-PublishedBinary.ps1", "the boot smoke")]
    [DataRow(".github/workflows/ci.yml", "the CI trim gate")]
    public void TheNonReleasePublishSitesDeclareTheHatchByName(string relativePath, string what)
    {
        string content = ReadRepoFile(relativePath.Replace('/', Path.DirectorySeparatorChar));

        // ⛔ COMMENT LINES ARE STRIPPED, and the canary is how that was found rather than reasoned
        // about. Both of these sites explain the hatch in a comment right above the flag, so a
        // whole-file search matched the PROSE — this test passed with the live flag deleted from
        // ci.yml's publish command, which is the one thing it exists to catch. YAML and PowerShell
        // both comment with '#', so one rule covers both.
        string live = string.Join(
            '\n',
            content.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

        Assert.IsTrue(
            live.Contains($"-p:{HatchProperty}=true", StringComparison.Ordinal),
            $"{relativePath} publishes a shipping app in Release and does not pass "
            + $"-p:{HatchProperty}=true, so {what} fails the guard. ⚠ The fix is the flag, never a "
            + "narrowing of the guard's condition: this site legitimately wants project references "
            + "because it has no feed credentials and a fork must run it green.");
    }

    /// <summary>
    /// The evidence is archived, not merely produced.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>It was produced and thrown away before D3.</b> <c>Publish-Rid.ps1</c> already tee'd
    /// every publish to <c>src/dist/logs/</c>, and <c>release.yml</c> uploaded only the archives —
    /// so the run that built the shipped binary recorded its mode into a file nothing kept. A guard
    /// whose output is discarded is a guard nobody can cite afterwards.
    /// </remarks>
    [TestMethod]
    public void TheReleaseArchivesTheProvenanceLogs()
    {
        string workflow = ReadRepoFile(Path.Combine(".github", "workflows", "release.yml"));

        // ⚠ `path:` lines only. Counting every mention instead matched the prose explaining WHY the
        // logs are archived, so the number tracked how much the comment said rather than how many
        // jobs actually upload — and it would have drifted on the next edit to that comment.
        int uploads = workflow
            .Split('\n')
            .Count(line => line.TrimStart().StartsWith("path:", StringComparison.Ordinal)
                && line.Contains("src/dist/logs/", StringComparison.Ordinal));

        Assert.AreEqual(3, uploads,
            "release.yml must archive the publish logs from all three publish jobs (Windows, Linux "
            + $"and macOS) and names 'src/dist/logs/' on {uploads} line(s). Each host publishes its "
            + "own RIDs, so a missing job is a platform whose shipped binary has no record of the "
            + "mode that built it.");
    }

    /// <summary>Loads the guard target, failing with a readable reason if it is gone.</summary>
    private static XElement LoadGuardTarget()
    {
        string targetsPath = Path.Combine(FindRepoRoot(), "Directory.Build.targets");

        Assert.IsTrue(File.Exists(targetsPath),
            $"The root Directory.Build.targets is missing ('{targetsPath}'). It carries the "
            + "shared-library reference switch, the publish strip and this guard; the subject is "
            + "gone, not merely renamed.");

        XElement? guard = XDocument.Load(targetsPath).Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Target"
                && e.Attribute("Name")?.Value == GuardTargetName);

        Assert.IsNotNull(guard,
            $"'{GuardTargetName}' is not in the root Directory.Build.targets. Renaming or removing "
            + "it restores a world where the release can publish from ProjectReference while every "
            + "gate stays green — which is exactly what happened for the life of the feature.");

        return guard;
    }

    /// <summary>Reads a repo-relative file, failing with the path if it has moved.</summary>
    private static string ReadRepoFile(string relativePath)
    {
        string fullPath = Path.Combine(FindRepoRoot(), relativePath);

        Assert.IsTrue(File.Exists(fullPath),
            $"'{relativePath}' is missing ('{fullPath}'). This guard's subject moved; point it at "
            + "the new path rather than deleting the assertion.");

        return File.ReadAllText(fullPath);
    }

    /// <summary>Walks up from the test binary to the repo root.</summary>
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
