using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Asserts that the committed OpenCode install snapshot
/// (<c>docs/opencode-install-probe.json</c>) carries measurements and no secrets.
/// </summary>
/// <remarks>
/// <para>
/// The snapshot is produced by <c>scripts/probe-opencode.ps1</c> against a maintainer's real
/// machine and then <b>committed to a public repository</b>. That combination is the entire
/// reason this file exists. The probe measures a SQLite database whose schema includes
/// <c>account</c> and <c>control_account</c> (each holding an <c>access_token</c> and a
/// <c>refresh_token</c>), <c>credential</c> (a store with a <c>value text NOT NULL</c>) and
/// <c>session_share</c> (a <c>secret</c>) — so a probe that grew a row dump, or lost its path
/// redaction, would publish provider tokens and a home directory in a routine "re-ran the
/// numbers" commit.
/// </para>
/// <para>
/// ⛔ <b>Nothing else can catch that.</b> The probe runs by hand, on one machine, and its output
/// is data rather than code — no compiler sees it, and a reviewer skimming a 7 KB JSON diff for
/// changed byte counts is exactly the reader who will not notice a new field. The check has to
/// live where it runs on every build.
/// </para>
/// <para>
/// ⚠ These tests assert on the SNAPSHOT, not on the script. That is deliberate: the script could
/// be rewritten in another language and the contract that matters — what is in the committed
/// file — would be unchanged.
/// </para>
/// <para>
/// ⓘ <b>A missing snapshot reddens all five, and only one of them explains why.</b> The other
/// four throw <see cref="FileNotFoundException"/>, which is left alone rather than softened to an
/// inconclusive: an absent baseline is a real failure, and a guard that quietly skips itself when
/// its input disappears is the failure mode this whole file exists to avoid. Read
/// <see cref="SnapshotExists_SoTheProbeHasActuallyBeenRun"/>'s message first.
/// </para>
/// <para>
/// ✅ Every assertion here was canaried by injecting its own defect into the snapshot — a
/// drive-letter path, a bare GUID, a 40-character hex run, a <c>rows</c> key, a removed
/// <c>usage</c> object, and a one-child <c>children</c> that arrived as a bare object — and each
/// reddened exactly the test named for it.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeInstallProbeSnapshotTests
{
    private const string SnapshotRelativePath = "docs/opencode-install-probe.json";

    /// <summary>
    /// Absolute-path shapes that must never appear. A Windows drive letter or a POSIX home
    /// prefix means the redaction that turns them into <c>~</c> and <c>&lt;temp&gt;</c> stopped
    /// running.
    /// </summary>
    private static readonly (string Name, Regex Pattern)[] ForbiddenShapes =
    [
        ("a Windows drive-letter path", new Regex(@"[A-Za-z]:[\\/]", RegexOptions.Compiled)),
        ("a POSIX home path", new Regex(@"/(?:home|Users)/[A-Za-z0-9._-]+", RegexOptions.Compiled)),
        ("a UNC path", new Regex(@"\\\\\\\\[A-Za-z0-9._-]+", RegexOptions.Compiled)),
        // A GUID or a long hex run is a lock token, a session id or an api key. The probe emits
        // key NAMES for lock metadata precisely so none of these can reach the file. The 40-hex
        // lock directory name is reduced to the literal '<sha1>' for the same reason.
        ("a GUID", new Regex(
            @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
            RegexOptions.Compiled)),
        ("a long hex run (>=32)", new Regex(@"\b[0-9a-fA-F]{32,}\b", RegexOptions.Compiled)),
    ];

    /// <summary>
    /// Keys whose presence means the probe started emitting row CONTENT. It only ever issues
    /// <c>COUNT(*)</c>, <c>PRAGMA</c> and <c>sqlite_master</c> reads; a key like these appearing
    /// is the signature of that having changed.
    /// </summary>
    /// <remarks>
    /// ⚠ Exact key names, and deliberately NOT <c>data</c> — <c>roots.data</c> is one of the four
    /// measured directories, so forbidding it would fail on a healthy snapshot and the fix would
    /// be to weaken the guard. <c>rowsByTable</c> and <c>walCarriedRows</c> are likewise fine:
    /// they hold counts, and neither is an exact match for <c>rows</c>.
    /// </remarks>
    private static readonly string[] ForbiddenKeys =
    [
        "rows", "values", "sample", "sampleRows", "contents", "rowData", "records",
        "accessToken", "access_token", "refreshToken", "refresh_token", "secretValue",
    ];

    [TestMethod]
    public void SnapshotExists_SoTheProbeHasActuallyBeenRun()
    {
        string path = SnapshotPath();
        Assert.IsTrue(
            File.Exists(path),
            $"'{SnapshotRelativePath}' is missing. Phase 16's whole point is that a re-check is a "
            + "diff rather than a re-investigation, which needs a committed baseline. Run "
            + "'pwsh -NoProfile -File scripts/probe-opencode.ps1'.");
    }

    [TestMethod]
    public void SnapshotContainsNoAbsolutePathsOrTokens()
    {
        string text = File.ReadAllText(SnapshotPath());
        List<string> found = [];

        foreach ((string name, Regex pattern) in ForbiddenShapes)
        {
            foreach (Match m in pattern.Matches(text))
            {
                found.Add($"{name}: '{m.Value}'");
            }
        }

        Assert.AreEqual(
            0,
            found.Count,
            $"'{SnapshotRelativePath}' leaks host detail into a public repository. The probe "
            + "redacts the profile to '~' and the temp root to '<temp>', and emits lock metadata "
            + "as key NAMES because its values are a token, a pid and a hostname. Found:"
            + Environment.NewLine + string.Join(Environment.NewLine, found));
    }

    [TestMethod]
    public void SnapshotCarriesNoRowContent()
    {
        JsonDocument document;
        using (FileStream stream = File.OpenRead(SnapshotPath()))
        {
            document = JsonDocument.Parse(stream);
        }

        List<string> offenders = [];
        using (document)
        {
            WalkForForbiddenKeys(document.RootElement, "$", offenders);
        }

        Assert.AreEqual(
            0,
            offenders.Count,
            $"'{SnapshotRelativePath}' looks like it now contains database row content. The probe "
            + "must only ever issue COUNT(*), PRAGMA and sqlite_master reads — that is what makes "
            + "it safe to commit the output of a database with a 'credential' table. Found:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The snapshot must state whether this is a used install, because Phase 14 is gated on it
    /// and a reader must not have to re-form that judgement from a wall of byte counts.
    /// </summary>
    [TestMethod]
    public void SnapshotStatesItsUsageVerdict()
    {
        JsonDocument document;
        using (FileStream stream = File.OpenRead(SnapshotPath()))
        {
            document = JsonDocument.Parse(stream);
        }

        using (document)
        {
            Assert.IsTrue(
                document.RootElement.TryGetProperty("usage", out JsonElement usage),
                $"'{SnapshotRelativePath}' has no 'usage' object.");

            foreach (string required in new[] { "isUsedInstall", "blocksPhase14" })
            {
                Assert.IsTrue(
                    usage.TryGetProperty(required, out JsonElement value),
                    $"'usage.{required}' is missing. It is the field a re-check reads.");
                Assert.IsTrue(
                    value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    $"'usage.{required}' must be a boolean, not {value.ValueKind}.");
            }
        }
    }

    /// <summary>
    /// ⛔ Guards the guard. Every check above passes trivially against an empty or truncated
    /// file, so the one thing that must be asserted separately is that there is something to
    /// check — a snapshot whose roots never got measured would sail through the leak scans.
    /// </summary>
    [TestMethod]
    public void SnapshotHasMeasurements_SoTheOtherTestsAreNotVacuous()
    {
        JsonDocument document;
        using (FileStream stream = File.OpenRead(SnapshotPath()))
        {
            document = JsonDocument.Parse(stream);
        }

        using (document)
        {
            Assert.IsTrue(
                document.RootElement.TryGetProperty("roots", out JsonElement roots),
                $"'{SnapshotRelativePath}' has no 'roots' object, so the leak scans above would "
                + "pass without inspecting a single measured path.");

            foreach (string role in new[] { "config", "data", "state", "cache" })
            {
                Assert.IsTrue(
                    roots.TryGetProperty(role, out JsonElement root),
                    $"'roots.{role}' is missing.");

                // 'children' is an ARRAY for every root. A root with exactly one child once
                // serialized as a bare object, because `return $result` in PowerShell enumerates
                // a collection and a one-element array arrives as the bare item.
                Assert.IsTrue(
                    root.TryGetProperty("children", out JsonElement children),
                    $"'roots.{role}.children' is missing.");
                Assert.AreEqual(
                    JsonValueKind.Array,
                    children.ValueKind,
                    $"'roots.{role}.children' must be an array even when it has one element — a "
                    + "consumer diffing two different shapes for the same field breaks on it.");
            }
        }
    }

    private static void WalkForForbiddenKeys(JsonElement element, string path, List<string> offenders)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (ForbiddenKeys.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{path}.{property.Name}");
                    }

                    WalkForForbiddenKeys(property.Value, $"{path}.{property.Name}", offenders);
                }

                break;

            case JsonValueKind.Array:
                int i = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WalkForForbiddenKeys(item, $"{path}[{i++}]", offenders);
                }

                break;
        }
    }

    private static string SnapshotPath()
        => Path.Combine(
            FindRepoRoot(),
            SnapshotRelativePath.Replace('/', Path.DirectorySeparatorChar));

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
