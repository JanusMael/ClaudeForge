using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Asserts that <c>src/publish/PublishApps.ps1</c> — the table every publish script reads to
/// learn what it is building — still describes the apps this repository actually ships.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>Nothing here runs until a release is being cut</b>, which is the entire problem. The
/// publish scripts are not compiled, not referenced by any project, and not exercised by the
/// Debug suite. A stale row in that table is discovered at the moment a tag is pushed — the one
/// moment when the cost of finding out is highest and the person finding out is least able to
/// take their time over it.
/// </para>
/// <para>
/// ⚠ <b>The startup-token check is the one with teeth.</b> Before the table existed,
/// <c>Smoke-PublishedBinary.ps1</c> asserted the literal <c>"Starting ClaudeForge"</c> against
/// whatever app it had just published — so it would have failed a perfectly healthy
/// OpenCodeForge publish and reported it as a boot crash. Moving the token into a table does not
/// by itself stop that; it only moves where the wrong string would live. What stops it is
/// checking the token against the log line the app's <c>Program.cs</c> actually writes, which is
/// what <see cref="EveryRowsStartupTokenIsWhatTheAppActuallyLogs"/> does.
/// </para>
/// <para>
/// ⓘ <b>What this does NOT guard, deliberately:</b> <c>LogFilePattern</c>. ClaudeForge's log
/// name is chosen inside <c>AvaloniaDiagnostics</c>' bucketed rolling sink from an
/// <c>AppName</c> option, OpenCodeForge's by a Serilog <c>rollingInterval</c> applied to a path
/// literal — two different mechanisms, neither of which yields the resulting glob by
/// substring-matching a source file. A guard that pretended otherwise would pass for the wrong
/// reason. The smoke gate failing to find any log file is what catches that one, and it says so.
/// </para>
/// </remarks>
[TestClass]
public sealed class PublishAppTableTests
{
    private const string TableRelativePath = PublishAppTable.RelativePath;

    /// <summary>
    /// The startup line an app's <c>Program.cs</c> writes. Captures the message template only —
    /// the structured arguments that follow are irrelevant to what a log-file scan can match.
    /// </summary>
    private static readonly Regex StartupLogRegex = new(
        @"Log\.Information\(\s*""(?<template>Starting [^""]*)""",
        RegexOptions.Compiled);

    /// <summary>A <c>{Placeholder}</c> inside a Serilog message template.</summary>
    private static readonly Regex PlaceholderRegex = new(
        @"\{(?<name>[A-Za-z][A-Za-z0-9]*)\}",
        RegexOptions.Compiled);

    /// <summary>Delegates to the shared reader; see <see cref="PublishAppTable"/>.</summary>
    private static string FindRepoRoot() => PublishAppTable.FindRepoRoot();

    private static List<PublishAppTable.Row> ReadTable(string repoRoot) =>
        PublishAppTable.Read(repoRoot);

    /// <summary>Every <c>src/</c> project that produces an app rather than a library.</summary>
    /// <remarks>
    /// <c>OutputType</c> is the discriminator because it is the property that decides whether a
    /// project produces something publishable at all — a name-prefix convention would need
    /// updating for a third app whose name does not end in "Forge", and would fail open.
    /// </remarks>
    private static List<string> ShippingAppProjects(string repoRoot)
    {
        List<string> apps = [];
        string srcDir = Path.Combine(repoRoot, "src");

        foreach (string projectFile in Directory.GetFiles(srcDir, "*.csproj", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(repoRoot, projectFile).Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }

            string? outputType = XDocument
                .Load(projectFile)
                .Descendants("OutputType")
                .Select(e => e.Value.Trim())
                .FirstOrDefault();

            if (string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase))
            {
                apps.Add(relative);
            }
        }

        return apps;
    }

    [TestMethod]
    public void TableParses_SoThisTestIsNotVacuous()
    {
        List<PublishAppTable.Row> rows = ReadTable(FindRepoRoot());

        Assert.IsTrue(
            rows.Count >= 2,
            $"Parsed {rows.Count} row(s) out of {TableRelativePath}, expected at least the two "
            + "shipping apps. Either the table's shape changed and this regex no longer reads it, "
            + "or an app was removed — the first case makes every other test in this class pass "
            + "without checking anything.");
    }

    [TestMethod]
    public void EveryShippingAppHasAPublishAppRow()
    {
        string repoRoot = FindRepoRoot();
        List<PublishAppTable.Row> rows = ReadTable(repoRoot);
        List<string> apps = ShippingAppProjects(repoRoot);

        Assert.IsTrue(
            apps.Count > 0,
            "Found no app projects (OutputType Exe/WinExe) under src/. This test would pass "
            + "without checking anything.");

        HashSet<string> declared = rows
            .Select(r => r.ProjectPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> unpublishable = [.. apps.Where(a => !declared.Contains(a))];

        Assert.IsTrue(
            unpublishable.Count == 0,
            $"{unpublishable.Count} app project(s) have no row in {TableRelativePath}, so no "
            + "publish script can build them and nothing but a release attempt would tell you:\n  "
            + string.Join("\n  ", unpublishable.Order(StringComparer.Ordinal)));
    }

    [TestMethod]
    public void EveryPublishAppRowNamesAProjectThatExists()
    {
        string repoRoot = FindRepoRoot();
        List<PublishAppTable.Row> rows = ReadTable(repoRoot);

        List<string> broken = [];
        foreach (PublishAppTable.Row row in rows)
        {
            string absolute = Path.Combine(
                repoRoot, row.ProjectPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                broken.Add($"{row.Name}: '{row.ProjectPath}'");
            }
        }

        Assert.IsTrue(
            broken.Count == 0,
            $"{broken.Count} row(s) in {TableRelativePath} point at a project that does not "
            + "exist:\n  " + string.Join("\n  ", broken));
    }

    /// <summary>
    /// Each row's <c>StartupLogToken</c> is a prefix of the line that app's <c>Program.cs</c>
    /// actually logs at startup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The smoke gate fails a publish when it cannot find this token in the app's log, so a token
    /// that does not match the real line turns the gate into a source of false release failures —
    /// and it only fires when a release is being cut.
    /// </para>
    /// <para>
    /// ⚠ <b>The two apps write that line differently, and that is why this resolves placeholders
    /// rather than comparing literals.</b> ClaudeForge writes the name inline
    /// (<c>"Starting ClaudeForge v{Version}"</c>); OpenCodeForge writes
    /// <c>"Starting {App} v{Version}"</c> and passes <c>Strings.AppTitle</c>. A literal
    /// comparison would either reject the second app or be weakened until it accepted anything.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EveryRowsStartupTokenIsWhatTheAppActuallyLogs()
    {
        string repoRoot = FindRepoRoot();
        List<PublishAppTable.Row> rows = ReadTable(repoRoot);

        List<string> mismatches = [];
        int checkedCount = 0;

        foreach (PublishAppTable.Row row in rows)
        {
            string projectDir = Path.GetDirectoryName(
                Path.Combine(repoRoot, row.ProjectPath.Replace('/', Path.DirectorySeparatorChar)))!;
            string programPath = Path.Combine(projectDir, "Program.cs");

            Assert.IsTrue(
                File.Exists(programPath),
                $"{row.Name}: no Program.cs at '{programPath}'. This check cannot read the startup "
                + "line, so it would silently vouch for a token nothing produces.");

            Match startup = StartupLogRegex.Match(File.ReadAllText(programPath));
            Assert.IsTrue(
                startup.Success,
                $"{row.Name}: no `Log.Information(\"Starting …\")` call found in Program.cs. Either "
                + "the app stopped logging a startup line — which makes the smoke gate unable to "
                + "pass for any token — or it now writes it somewhere this check does not look.");

            string template = startup.Groups["template"].Value;

            // Resolve the app-name placeholder from the resx the app passes to it. Anything else
            // is a placeholder this check has never seen: fail rather than guess, because a
            // silently-unresolved '{X}' would make the comparison below meaningless.
            string resolved = PlaceholderRegex.Replace(template, m =>
            {
                string name = m.Groups["name"].Value;
                if (name == "Version")
                {
                    return m.Value;
                }

                if (name == "App")
                {
                    return ResxValue(projectDir, "AppTitle")
                        ?? throw new InvalidOperationException(
                            $"{row.Name}: Program.cs logs '{{App}}' but Localization/Strings.resx "
                            + "has no AppTitle to resolve it from.");
                }

                throw new InvalidOperationException(
                    $"{row.Name}: startup template '{template}' contains an unrecognised "
                    + $"placeholder '{{{name}}}'. Teach this test how to resolve it rather than "
                    + "letting it compare against an unexpanded brace.");
            });

            checkedCount++;
            if (!resolved.StartsWith(row.StartupLogToken, StringComparison.Ordinal))
            {
                mismatches.Add(
                    $"{row.Name}: table says '{row.StartupLogToken}', Program.cs logs '{resolved}'");
            }
        }

        Assert.IsTrue(
            checkedCount > 0,
            "No startup tokens were checked. This test would pass without guarding anything.");

        Assert.IsTrue(
            mismatches.Count == 0,
            $"{mismatches.Count} row(s) declare a startup token the app never writes. "
            + "Smoke-PublishedBinary.ps1 fails a publish when it cannot find that token, so this "
            + "reads as a boot crash in an app that started perfectly well:\n  "
            + string.Join("\n  ", mismatches));
    }

    /// <summary>The value of one string in an app's default resx, or <see langword="null"/>.</summary>
    private static string? ResxValue(string projectDir, string name)
    {
        string resxPath = Path.Combine(projectDir, "Localization", "Strings.resx");
        if (!File.Exists(resxPath))
        {
            return null;
        }

        return XDocument
            .Load(resxPath)
            .Descendants("data")
            .Where(d => (string?)d.Attribute("name") == name)
            .Select(d => (string?)d.Element("value"))
            .FirstOrDefault();
    }
}
