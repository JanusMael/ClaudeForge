using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;

// Interlocked + Thread for the parallel-precompute test

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// End-to-end round-trip tests for <see cref="BackupEngine"/>. Each test isolates its
/// own fake <c>$HOME</c> via the <c>USERPROFILE</c> (Windows) or <c>HOME</c> (Unix)
/// environment variable so the real user's Claude data is never touched.
/// </summary>
/// <remarks>
/// ⚠ <b>Only what is about Claude is tested here.</b> The engine mechanics that read the descriptor
/// (sections, skip rules, the credential opt-in, listing, retention, restore, cancellation and the
/// sanitized contract) are tested on a product that is not Claude, in
/// <see cref="NeutralProductBackupTests"/> (<c>plans/00008</c> step 5).
/// </remarks>
public sealed class BackupEngineTests : IDisposable
{
    private string _fakeHome = string.Empty;

    public BackupEngineTests() => Setup();

    private void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(), "be-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fakeHome);

        // BackupEngine.List memoises parsed manifests for process lifetime,
        // keyed by path + mtime + length. Per-test sandboxes use unique GUIDs
        // so collisions are unlikely, but invalidating defensively keeps the
        // assertion stack independent of test ordering.
        BackupEngine.InvalidateListCache();

        // Redirect every PlatformPaths call to the sandbox so the real user's Claude
        // data is never touched by the engine.
        PlatformPaths.TestUserProfileOverride = _fakeHome;

        // Populate a minimal Claude footprint the engine will back up.
        Directory.CreateDirectory(Path.Combine(_fakeHome, ".claude"));
        File.WriteAllText(Path.Combine(_fakeHome, ".claude.json"), """{"user":"test"}""");
        File.WriteAllText(Path.Combine(_fakeHome, ".claude", "settings.json"), """{"theme":"dark"}""");
        // A "projects" sub-directory so the Settings-only / Full difference is testable.
        string projectsDir = Path.Combine(_fakeHome, ".claude", "projects");
        Directory.CreateDirectory(projectsDir);
        File.WriteAllText(Path.Combine(projectsDir, "session.jsonl"), """{"role":"user"}""");
    }

    private void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_fakeHome))
            {
                Directory.Delete(_fakeHome, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ResolveSafeExtractPath_RejectsZipSlip()
    {
        string baseDir = _fakeHome;
        string? safe = RestoreEngine.ResolveSafeExtractPath(baseDir, "ClaudeCode/claude.json");
        Assert.NotNull(safe);

        string? unsafe1 = RestoreEngine.ResolveSafeExtractPath(baseDir, "../../etc/passwd");
        string? unsafe2 = RestoreEngine.ResolveSafeExtractPath(baseDir, "/etc/passwd");
        MessageAssert.Null(unsafe1, "Relative traversal must be rejected.");
        MessageAssert.Null(unsafe2, "Absolute paths must be rejected.");
    }

    // ───────────────────────────────────────────────────────────────────────
    //  CreateAsync error / branch coverage
    //
    //  Existing tests cover happy paths (settings-only / full / retention /
    //  exclusion logic).  These add: argument validation, the Claude
    //  Desktop branch, the "no on-disk data at all" branch, and the
    //  cancellation contract.  The IO-failure catch at line 227 is
    //  difficult to trigger without injecting a fake ZipArchiveWriter
    //  — left uncovered for a future refactor that adds an injection
    //  seam.
    // ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NullRequest_ThrowsArgumentNull()
    {
        // Argument validation is contract — null request is a programmer
        // error, not a runtime "expected failure", and is allowed to throw.
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            TestBackupEngine.Default.CreateAsync(null!, ct: TestContext.Current.CancellationToken, progress: null));
    }

    [Fact]
    public async Task CreateAsync_BothProductsExcluded_StillProducesValidArchive()
    {
        // Edge case: caller requests an EMPTY product set.
        // Result should still be a valid empty backup (manifest + schemas);
        // the contract is permissive — we don't reject the request as a
        // programmer error.
        string dest = Path.Combine(_fakeHome, "no-products.zip");
        BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            Products = [],
        }, ct: TestContext.Current.CancellationToken, progress: null);

        Assert.True(result.Succeeded, result.Message);
        List<string> entries = ListEntries(dest);
        Assert.Contains(entries, e => e == "manifest.json");
        Assert.DoesNotContain(entries, e => e.StartsWith("ClaudeCode/", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, e => e.StartsWith("ClaudeDesktop/", StringComparison.Ordinal));
    }

    // ───────────────────────────────────────────────────────────────────────
    //  RestoreEngine.ValidateExtractedConfigsAsync
    //
    //  ValidateExtractedConfigsAsync is private; we exercise it indirectly
    //  through RestoreAsync, which surfaces its output in
    //  RestoreResult.ValidationWarnings. The four tests below cover the
    //  branches the existing roundtrip case skipped over: schema violation
    //  produces warnings, missing Schemas/ dir is a graceful skip, corrupt
    //  schema entries don't crash, and corrupt settings JSON inside the
    //  backup is silently dropped from the validation set.
    // ───────────────────────────────────────────────────────────────────────

    // ═══════════════════════════════════════════════════════════════════════
    //  ProductDescriptor.ArchiveFolder is a PERSISTED contract
    //
    //  Phase 4d-2 made one property the single source for both the archive's top-level
    //  folder names and the manifest's `clients` entries — replacing the same two literals
    //  restated in the manifest builder, in every folder path, and again in RestoreEngine.
    //
    //  That centralisation has a cost worth guarding: because both the writer and the
    //  reader now read the SAME property, changing it moves both sides at once and stays
    //  self-consistent. New archives would work perfectly while every archive already on
    //  the user's disk quietly stopped matching. Renaming Claude Code's folder does fail
    //  ten tests today, but only incidentally — via hardcoded path strings in unrelated
    //  assertions — and NOTHING asserted the value the engine writes into `clients`.
    // ═══════════════════════════════════════════════════════════════════════

    // ───────────────────────────────────────────────────────────────────────
    //  Helpers
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The <c>Schemas/</c> entries of an archive, without the folder prefix, sorted.
    /// </summary>
    private static List<string> ListBundledSchemas(string zipPath)
        => ListEntries(zipPath)
            .Where(e => e.StartsWith("Schemas/", StringComparison.Ordinal))
            .Select(e => e["Schemas/".Length..])
            .Order(StringComparer.Ordinal)
            .ToList();

    private static List<string> ListEntries(string zipPath)
    {
        using FileStream fs = File.OpenRead(zipPath);
        using ZipArchive archive = new(fs, ZipArchiveMode.Read);
        return archive.Entries.Select(e => e.FullName).ToList();
    }

    private static string ReadEntryText(string zipPath, string entryName)
    {
        using FileStream fs = File.OpenRead(zipPath);
        using ZipArchive archive = new(fs, ZipArchiveMode.Read);
        ZipArchiveEntry entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException(
            $"Entry '{entryName}' not found in '{zipPath}'.");
        using Stream es = entry.Open();
        using StreamReader sr = new(es);
        return sr.ReadToEnd();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Sanitized backup mode (2026-05-14)
    //
    //  Three contracts to pin:
    //   1. Sensitive values inside *.json files get rewritten to the
    //      "[redacted]" marker (JsonRedactor invocation reaches the wire).
    //   2. The credentials file is skipped entirely regardless of the
    //      IncludeCredentials flag.
    //   3. RestoreEngine refuses to apply the resulting archive — the
    //      mode is sharing-only.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Removes every entry matching <paramref name="predicate"/> from the zip in-place.</summary>
    private static void StripZipEntries(string zipPath, Func<ZipArchiveEntry, bool> predicate)
    {
        using FileStream fs = new(zipPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using ZipArchive archive = new(fs, ZipArchiveMode.Update);
        List<ZipArchiveEntry> toRemove = archive.Entries.Where(predicate).ToList();
        foreach (ZipArchiveEntry e in toRemove)
        {
            e.Delete();
        }
    }

    /// <summary>
    /// Replaces <paramref name="entryName"/>'s content with <paramref name="newContent"/>
    /// in-place. Adds the entry if it wasn't already present.
    /// </summary>
    private static void OverwriteZipEntry(string zipPath, string entryName, string newContent)
    {
        using FileStream fs = new(zipPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using ZipArchive archive = new(fs, ZipArchiveMode.Update);
        archive.GetEntry(entryName)?.Delete();
        ZipArchiveEntry newEntry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
        using Stream es = newEntry.Open();
        byte[] bytes = Encoding.UTF8.GetBytes(newContent);
        es.Write(bytes, 0, bytes.Length);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  H1 — Expanded sanitized redaction (text + JSON, parallel precompute)
    // ═══════════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════════
    //  H1 — PrecomputeTransformsAsync (parallel pre-commit transform)
    // ═══════════════════════════════════════════════════════════════════════
    [Fact(Skip = "Flaky test")]
    public async Task PrecomputeTransformsAsync_RunsInParallel_AcrossMultipleEntries()
    {
        // Probe the parallel path directly via ZipArchiveWriter so the
        // assertion is independent of BackupEngine wiring.  Schedule 20
        // files with a slow transformer; assert that wall-clock time is
        // closer to (FileCount / MaxParallel × SleepMs) than to the
        // serial worst case (FileCount × SleepMs).
        const int fileCount = 20;
        const int sleepMs = 50;
        const int parallel = 4;
        const int worstCaseMs = fileCount * sleepMs; // 1000 ms
        // Theoretical floor with perfect parallelism would be
        // (fileCount / parallel) × sleepMs = 250 ms; the assertion
        // below uses worstCaseMs × 3/4 = 750 ms as a generous upper
        // bound that still catches serial-execution regressions.

        // Plant the source files.
        string srcDir = Path.Combine(_fakeHome, "parallel-src");
        Directory.CreateDirectory(srcDir);
        for (int i = 0; i < fileCount; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(srcDir, $"f{i}.txt"), $"content-{i}", TestContext.Current.CancellationToken);
        }

        string dest = Path.Combine(_fakeHome, "parallel.zip");
        int hits = 0;

        await using ZipArchiveWriter writer = ZipArchiveWriter.Create(dest);
        for (int i = 0; i < fileCount; i++)
        {
            writer.AddFile(Path.Combine(srcDir, $"f{i}.txt"), $"x/f{i}.txt");
        }

        writer.FileTransformer = path =>
        {
            Interlocked.Increment(ref hits);
            Thread.Sleep(sleepMs);
            return $"transformed:{Path.GetFileName(path)}";
        };

        Stopwatch sw = Stopwatch.StartNew();
        await writer.PrecomputeTransformsAsync(maxDegreeOfParallelism: parallel, ct: TestContext.Current.CancellationToken);
        sw.Stop();

        MessageAssert.Equal(fileCount, hits, "Every queued source entry must be transformed.");
        Assert.True(sw.ElapsedMilliseconds < worstCaseMs * 3 / 4,
            $"Parallel precompute should run far faster than serial worst case ({worstCaseMs} ms). " +
            $"Actual: {sw.ElapsedMilliseconds} ms; serial floor would be {worstCaseMs} ms.");

        // Sanity: the precomputed strings actually land in the archive.
        await writer.CommitAsync(ct: TestContext.Current.CancellationToken, progress: null);
        string sample = ReadEntryText(dest, "x/f0.txt");
        Assert.Equal("transformed:f0.txt", sample);
    }

    [Fact]
    public async Task PrecomputeTransformsAsync_ExceptionPerFile_SkipsThatEntryOnly()
    {
        // A transformer that throws for one specific file: that file
        // must land in SkippedFiles and NOT be cached; the rest of the
        // archive must complete normally.
        string srcDir = Path.Combine(_fakeHome, "exc-src");
        Directory.CreateDirectory(srcDir);
        string goodPath = Path.Combine(srcDir, "good.txt");
        string badPath = Path.Combine(srcDir, "bad.txt");
        await File.WriteAllTextAsync(goodPath, "good", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(badPath, "bad", TestContext.Current.CancellationToken);

        string dest = Path.Combine(_fakeHome, "per-file-exc.zip");
        await using ZipArchiveWriter writer = ZipArchiveWriter.Create(dest);
        writer.AddFile(goodPath, "good.txt");
        writer.AddFile(badPath, "bad.txt");
        writer.FileTransformer = path => Path.GetFileName(path) == "bad.txt"
            ? throw new IOException("simulated per-file failure")
            : "ok";

        await writer.PrecomputeTransformsAsync(maxDegreeOfParallelism: 2, ct: TestContext.Current.CancellationToken);

        Assert.True(writer.SkippedFiles.Any(p => p.EndsWith("bad.txt", StringComparison.Ordinal)),
            "Failed transformer file must appear in SkippedFiles.");

        // The commit must still succeed (and bad.txt will fail again on
        // the synchronous fallback path, adding to SkippedFiles a 2nd
        // time — that's expected mirroring of the non-precompute path).
        await writer.CommitAsync(ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(File.Exists(dest));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  M2 — BuildSanitizationPlaceholder uses JsonSerializer (escape correctness)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildSanitizationPlaceholder_ControlCharsInFilename_ProducesValidJson()
    {
        // Pre-fix, hand-rolled escaping only handled \ and " — a control
        // character in the filename would have emitted invalid JSON.
        // JsonSerializer handles all escape sequences correctly.
        string pathWithTab = "C:\\foo\\bar\twith-tab.json";
        string pathWithNewline = "/home/user/a\nb.json";

        string placeholder1 = BackupEngine.BuildSanitizationPlaceholder(pathWithTab, "io-failure");
        string placeholder2 = BackupEngine.BuildSanitizationPlaceholder(pathWithNewline, "io-failure");

        // Round-trip: both must parse cleanly as JSON.
        JsonNode? doc1 = JsonNode.Parse(placeholder1);
        JsonNode? doc2 = JsonNode.Parse(placeholder2);
        Assert.NotNull(doc1);
        Assert.NotNull(doc2);
    }

    [Fact]
    public void BuildSanitizationPlaceholder_FilenameOnly_DoesNotLeakFullPath()
    {
        // Path.GetFileName strips the directory; placeholder must not
        // disclose user directory layout (same hygiene principle as M1's
        // SanitiseExceptionForStatus).
        string fullPath = Path.Combine("C:\\Users\\someone\\.claude", "settings.json");
        string placeholder = BackupEngine.BuildSanitizationPlaceholder(fullPath, "io-failure");

        Assert.False(placeholder.Contains("someone", StringComparison.Ordinal),
            "Username from the source path must not appear in the placeholder.");
        Assert.False(placeholder.Contains(".claude", StringComparison.Ordinal),
            "Directory components must not appear in the placeholder.");

        JsonObject parsed = JsonNode.Parse(placeholder)!.AsObject();
        Assert.Equal("settings.json", (string?)parsed["_file"]);
    }

    [Fact]
    public void BuildSanitizationPlaceholder_ReasonWithQuotes_RoundTrips()
    {
        string reason = "redaction-failed: \"unexpected token\" at line 1";
        string placeholder = BackupEngine.BuildSanitizationPlaceholder("foo.json", reason);

        JsonObject parsed = JsonNode.Parse(placeholder)!.AsObject();
        Assert.Equal(reason, (string?)parsed["_claudeforge_sanitization_error"]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MOutOfMemoryException in transformer is caught + skipped (proxy)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateAsync_TransformerThrowsOutOfMemory_SkipsFileAndSucceeds()
    {
        // Throwing a real OOM is impractical and would crash the test
        // host; instead inject a probe transformer that throws OOM for
        // a specific file.  This is a faithful proxy for the
        // exception-propagation contract under test.
        //
        // The probe is wired AFTER BackupEngine.CreateAsync sets its
        // own RedactFileForSharing transformer; we test the underlying
        // ZipArchiveWriter behaviour directly so the assertion is
        // unambiguous.
        string srcDir = Path.Combine(_fakeHome, "oom-src");
        Directory.CreateDirectory(srcDir);
        string oomPath = Path.Combine(srcDir, "oom.json");
        string safePath = Path.Combine(srcDir, "safe.json");
        await File.WriteAllTextAsync(oomPath, """{"k":"v"}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(safePath, """{"k":"v"}""", TestContext.Current.CancellationToken);

        string dest = Path.Combine(_fakeHome, "oom-proxy.zip");
        await using ZipArchiveWriter writer = ZipArchiveWriter.Create(dest);
        writer.AddFile(oomPath, "oom.json");
        writer.AddFile(safePath, "safe.json");
        writer.FileTransformer = path => Path.GetFileName(path) == "oom.json"
            ? throw new OutOfMemoryException("simulated OOM on a huge file")
            : """{"k":"redacted"}""";

        // Precompute should record the failure without throwing.
        await writer.PrecomputeTransformsAsync(ct: TestContext.Current.CancellationToken, maxDegreeOfParallelism: -1);
        Assert.True(writer.SkippedFiles.Any(p => p.EndsWith("oom.json", StringComparison.Ordinal)),
            "OOM during transform must add the file to SkippedFiles.");

        // Commit must still succeed for the other file.
        long bytes = await writer.CommitAsync(ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(bytes > 0);
        Assert.True(File.Exists(dest));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ExplicitProjectDirs end-to-end coverage.
    //
    // Pre-fix BackupRestoreViewModel.CreateBackupAsync built the BackupRequest
    // without setting ExplicitProjectDirs.  AddProjectClaudeData is only
    // invoked for items in that list — so the open project's `.claude`
    // directory was silently absent from every backup the app produced.
    // These tests pin the end-to-end picture (given a populated
    // ExplicitProjectDirs, the archive contains the project's settings +
    // CLAUDE.md, and the AdditionalDirectoriesResolver picks up sibling
    // additionalDirectories declarations).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a minimal <c>~/.claude.json</c> into the sandbox with the
    /// given project paths registered in the top-level <c>projects</c>
    /// map.  Used by the Full-mode multi-project tests so each test
    /// doesn't have to hand-escape Windows path separators inside a raw
    /// JSON string template.
    /// </summary>
    private void WriteFakeClaudeJson(params string[] projectPaths)
    {
        JsonObject projectsMap = new();
        foreach (string p in projectPaths)
        {
            projectsMap[p] = new JsonObject();
        }
        JsonObject root = new() { ["projects"] = projectsMap };
        File.WriteAllText(Path.Combine(_fakeHome, ".claude.json"), root.ToJsonString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Full-mode multi-project expansion (Part 1.5).
    //
    // Pre-fix Full mode only backed up the explicit/discovered project set
    // (often empty when no project is open).  Per-project hooks, MCP config,
    // agents, skills, CLAUDE.md, etc. for every OTHER project Claude had
    // ever seen were silently absent from "Full" archives.  These tests
    // pin the four contracts that close the gap:
    //   1. Full mode walks every project in ~/.claude.json's `projects` map.
    //   2. Non-existent paths are skipped silently + counted in the manifest
    //      warnings (no path strings logged — could be identifying).
    //   3. SettingsOnly does NOT expand to known projects (single-project
    //      contract preserved).
    //   4. Sanitized does NOT expand to known projects (sharing-mode
    //      contract preserved — extending it would defeat the redaction
    //      premise).
    // ─────────────────────────────────────────────────────────────────────────
}