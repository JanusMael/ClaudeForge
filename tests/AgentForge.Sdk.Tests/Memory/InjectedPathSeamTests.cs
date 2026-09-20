using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Memory;

/// <summary>
/// The seam Phase 10c created stays open: the artifact and footprint services read their roots
/// from an injected <c>ClaudeArtifactPaths</c>, not from the process-global <c>PlatformPaths</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>Without this, the conversion is a one-time cleanup rather than a seam.</b> Every static
/// read that comes back is invisible in review — the code compiles, every test passes, and the
/// service silently ignores the paths it was handed. That is exactly how a baked-in root survives
/// a refactor that was supposed to remove it, and it is why profiles were impossible before.
/// </para>
/// <para>
/// ⚠ <b>Source text, not reflection.</b> A static property read leaves no per-type trace in
/// assembly metadata, so there is nothing to reflect over — the assembly-level reference table that
/// <c>AssemblyLayeringTests</c> uses cannot see which type did the reading. Comments are stripped
/// line-wise (<c>//</c> to end of line), which is sound for these files because none of them
/// contains a <c>//</c> inside a string literal.
/// </para>
/// </remarks>
[TestClass]
public sealed class InjectedPathSeamTests
{
    /// <summary>
    /// The only static path reads the artifact surface may still contain, and why each survives.
    /// </summary>
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        // The documented places the process-global default enters the surface.
        //
        // UserProfile — the single root default. Everything else in that type is derived from the
        // instance root, which is the whole point of the seam.
        //
        // ⛔⛔ The three Managed* members are the DELIBERATE EXCEPTION, and the reason is the
        // opposite of the rule. Enterprise / MDM policy lives in a per-OS SYSTEM directory that
        // every user on the machine shares — it is not derived from any profile root, so there is
        // no injected value that could make it correct. Worse, if it WERE root-relative, pointing
        // the instance at another directory would relocate enterprise policy, which is a
        // policy-escape hatch rather than a testing seam.
        //
        // ⚠ So the exemption is narrow ON PURPOSE: these three member names, not the file. A
        // fourth managed path added later has to come here and justify itself, and any ordinary
        // root-relative path that drifts back to a static still fails.
        // ⓘ Tests reach managed discovery through ConfigFileDiscoverer's managedRoot parameter,
        // which is a value rather than ambient state — that is where the sandbox seam lives.
        ["ClaudeArtifactPaths.cs"] =
        [
            "UserProfile",
            "ManagedSettingsPath",
            "ManagedSettingsDropInDir",
            "ManagedMcpPath",
        ],

        // Pure functions of a project root the caller already supplies — no static root to inject.
        ["ClaudeArtifactSources.cs"] = ["ProjectSettingsPath", "LocalSettingsPath", "ProjectMcpPath"],
    };

    private static readonly Regex StaticRead =
        new(@"PlatformPaths\.(?<member>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    [TestMethod]
    public void TheArtifactSurfaceReadsNoUnexpectedProcessGlobalPath()
    {
        string memoryDir = Path.Combine(FindRepoRoot(), "src", "AgentForge.Sdk", "Memory");
        Assert.IsTrue(Directory.Exists(memoryDir), $"Expected the Memory folder at '{memoryDir}'.");

        List<string> violations = [];
        int allowedSeen = 0;
        int filesScanned = 0;

        foreach (string file in Directory.EnumerateFiles(memoryDir, "*.cs", SearchOption.AllDirectories))
        {
            filesScanned++;
            string name = Path.GetFileName(file);
            string[] allowed = Allowed.TryGetValue(name, out string[]? a) ? a : [];

            int lineNumber = 0;
            foreach (string raw in File.ReadAllLines(file))
            {
                lineNumber++;
                int comment = raw.IndexOf("//", StringComparison.Ordinal);
                string code = comment >= 0 ? raw[..comment] : raw;

                foreach (Match match in StaticRead.Matches(code))
                {
                    string member = match.Groups["member"].Value;
                    if (allowed.Contains(member, StringComparer.Ordinal))
                    {
                        allowedSeen++;
                        continue;
                    }

                    violations.Add($"{name}:{lineNumber}: PlatformPaths.{member}");
                }
            }
        }

        Assert.IsTrue(filesScanned > 5, $"Only {filesScanned} files scanned — the folder moved.");
        Assert.IsTrue(
            allowedSeen > 0,
            "No allowed PlatformPaths reads were found at all. Either the surface stopped using "
            + "them entirely (delete the allow-list and this assertion) or the comment-stripping "
            + "now eats real code — in which case this test guards nothing.");

        Assert.IsTrue(
            violations.Count == 0,
            $"{violations.Count} static path read(s) reintroduced into the artifact surface. These "
            + "make the injected ClaudeArtifactPaths silently ineffective — the service compiles, "
            + "the tests pass, and it reads the process profile anyway:\n  "
            + string.Join("\n  ", violations));
    }

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
