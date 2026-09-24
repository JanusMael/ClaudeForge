using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace Bennewitz.Ninja.AgentForge.Core.Schema;

/// <summary>
/// What a schema artifact on disk was made from, written beside it as <c>&lt;name&gt;.meta.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>The artifact on disk is the RESOLVED document</b> — already stripped of external
/// <c>$ref</c>s and already overlaid — so loading it is a plain parse with no further steps. That
/// is the whole point of the design: one file that is exactly what the editors and save-validation
/// see, inspectable by a user or a support request without re-deriving anything.
/// </para>
/// <para>
/// ⚠ <b>Which means the overlay is BAKED IN, and that is what <see cref="OverlaySha256"/> is
/// for.</b> Edit an overlay and every artifact built from the old one is silently wrong — it would
/// keep validating against rules nobody ships any more. Recording the overlay's digest lets the
/// loader notice and rebuild instead.
/// </para>
/// <para>
/// ⛔ <b><see cref="Source"/> is not decoration — it decides whether bundled may overwrite this
/// file.</b> A copy that came from the network must never be clobbered by the binary's own copy
/// just because today's launch is offline; that would silently downgrade a user who fetched a
/// newer schema yesterday. A copy that came from bundled, however, SHOULD be replaced when the
/// app ships newer bundled bytes — which is what <see cref="BundledSourceSha256"/> detects.
/// </para>
/// </remarks>
/// <param name="Source">
/// <c>"Fetched"</c> or <c>"Bundled"</c>. A string rather than the enum because this is a persisted
/// format: renaming an enum member must not silently invalidate every cache on disk.
/// </param>
/// <param name="FetchedUtc">When the download completed, or <see langword="null"/> for bundled.</param>
/// <param name="Sha256">
/// Digest of the artifact bytes this sidecar describes. Re-checked on read, so a truncated or
/// half-written artifact is detected rather than parsed into a schema that permits the wrong things.
/// </param>
/// <param name="ETag">
/// The response ETag, so the next launch can ask "anything new?" with a conditional GET. A
/// <c>304</c> answers that question AND avoids re-downloading in a single round trip, which is
/// what keeps a blocking launch cheap.
/// </param>
/// <param name="OverlaySha256">Digest of the overlay source, or empty when the schema has none.</param>
/// <param name="BundledSourceSha256">
/// Digest of the raw bundled bytes this artifact was built from, when <paramref name="Source"/> is
/// bundled. <see langword="null"/> for a fetched artifact.
/// </param>
/// <param name="AppVersion">Diagnostic only — which build wrote this.</param>
/// <param name="WrittenUtc">Diagnostic only — when.</param>
/// <remarks>
/// ⚠ <b>Every name is pinned with <see cref="JsonPropertyNameAttribute"/>.</b> This file is
/// written to a user's disk and is meant to be readable by a person looking at it — and the
/// on-disk spelling must not change because someone renamed a C# property, which is the same
/// reason <see cref="Source"/> is a string rather than the enum.
/// </remarks>
internal sealed record SchemaCacheSidecar(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("fetchedUtc")] DateTimeOffset? FetchedUtc,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("etag")] string? ETag,
    [property: JsonPropertyName("overlaySha256")] string OverlaySha256,
    [property: JsonPropertyName("bundledSourceSha256")] string? BundledSourceSha256,
    [property: JsonPropertyName("appVersion")] string AppVersion,
    [property: JsonPropertyName("writtenUtc")] DateTimeOffset WrittenUtc);

/// <summary>
/// The on-disk store for resolved schema artifacts.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Disk is not a tier in a precedence chain here — it is the materialised result.</b> The old
/// design asked three sources in order and the answer depended on which replied; this one resolves
/// once per launch and writes what it resolved, so there is exactly one path into the memory cache
/// and exactly one file that describes what the app is validating against.
/// </para>
/// <para>
/// ⛔ <b>A null directory means NO DISK, and that is deliberately the default.</b> It mirrors the
/// <c>HttpClient</c> convention this registry already uses, for the same measured reason: 34 test
/// sites construct a registry without one, and if the default wrote to disk every one of them
/// would scribble into a real user profile. Production supplies a directory by name, per app —
/// there is no neutral default, because <c>~/.claude/cache/schemas</c> is Claude's answer and
/// putting OpenCode's schemas under it would be wrong.
/// </para>
/// </remarks>
internal sealed class SchemaDiskCache
{
    /// <summary>
    /// One semaphore per artifact path, shared process-wide.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>Two registries in one process CAN target the same file.</b> Nothing stops a host
    /// building more than one — its own, plus one inside each client it constructs without passing
    /// one — and they then resolve the same schemas concurrently. Without this, two interleaved
    /// writes produce an artifact whose digest matches neither sidecar, which reads as corruption
    /// on the next launch. ⚠ This is the same hazard <c>cafe89c</c> fixed on main for the previous
    /// disk cache; it returns with the disk cache, so it is guarded from the start rather than
    /// rediscovered.
    /// <para>
    /// ⓘ <b>Corrected 2026-09-23.</b> This said <i>"OpenCodeForge builds three registries at
    /// launch"</i>, present tense. That was true when written and is not now —
    /// <c>CLAUDE.md</c> carries the dated correction, and <c>SharedSchemaRegistryTests</c> pins
    /// both clients sharing one instance. ⛔ <b>That test is NOT on this branch</b>, and neither is
    /// the shell it covers: both live on <c>feat/agentforge-opencodeforge</c>, parked by
    /// <c>plans/00003</c> Phase 0. Naming a guard without saying where it is readable is the
    /// second half of the same defect — see <c>AGENTS.md</c> §6, which this comment failed on its
    /// first attempt. The specific host is removed rather than re-counted,
    /// because <b>this is a packaged neutral library</b>: the hazard belongs to any consumer that
    /// builds more than one registry, and a count of one host's registries is a fact this file can
    /// never see change. ⛔ The guard is unaffected and was never wrong — per-artifact-path
    /// locking is still correct. Only the rationale had gone stale, which is why it is reworded
    /// rather than deleted: deleting it would take the reason with it.
    /// </para>
    /// </remarks>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Writers = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _directory;

    internal SchemaDiskCache(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    internal string ArtifactPath(string cacheFileName) => Path.Combine(_directory, cacheFileName);

    private string SidecarPath(string cacheFileName) => Path.Combine(_directory, cacheFileName + ".meta.json");

    /// <summary>Digest helper — one spelling, so a read and a write cannot disagree.</summary>
    internal static string Digest(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// Read the artifact and its sidecar, or <see langword="false"/> when there is nothing
    /// trustworthy there.
    /// </summary>
    /// <remarks>
    /// Returns false for missing, unreadable, malformed, digest-mismatched, or overlay-stale
    /// entries alike. The caller's response to every one of those is the same — resolve afresh —
    /// so distinguishing them in the signature would buy nothing; the reason is logged.
    /// </remarks>
    internal bool TryRead(
        string cacheFileName,
        string expectedOverlaySha,
        out byte[] artifact,
        out SchemaCacheSidecar sidecar)
    {
        artifact = [];
        sidecar = null!;

        string artifactPath = ArtifactPath(cacheFileName);
        string sidecarPath = SidecarPath(cacheFileName);

        try
        {
            if (!File.Exists(artifactPath) || !File.Exists(sidecarPath))
            {
                return false;
            }

            SchemaCacheSidecar? read = JsonSerializer.Deserialize(
                File.ReadAllText(sidecarPath), CoreJsonContext.Default.SchemaCacheSidecar);
            if (read is null)
            {
                return false;
            }

            byte[] bytes = File.ReadAllBytes(artifactPath);

            // ⛔ Integrity before trust. A half-written artifact parses into a schema that permits
            // the WRONG THINGS rather than failing, and an over-permissive schema reports success
            // for a document nothing has really checked.
            if (!string.Equals(Digest(bytes), read.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning(
                    "[Schema] Cached {File} does not match its recorded digest; rebuilding", cacheFileName);
                return false;
            }

            // The overlay is baked into the artifact, so an overlay edit invalidates it.
            if (!string.Equals(read.OverlaySha256, expectedOverlaySha, StringComparison.OrdinalIgnoreCase))
            {
                Log.Information(
                    "[Schema] Overlay for {File} changed since the cache was written; rebuilding", cacheFileName);
                return false;
            }

            artifact = bytes;
            sidecar = read;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warning(ex, "[Schema] Could not read the cached copy of {File}; rebuilding", cacheFileName);
            return false;
        }
    }

    /// <summary>
    /// Write an artifact and its sidecar, atomically and one writer at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>Artifact first, sidecar second, and the order is load-bearing.</b> A crash between the
    /// two leaves an artifact whose sidecar is missing or describes the previous bytes, and
    /// <see cref="TryRead"/> rejects both — so the failure mode is "rebuild it", never "trust
    /// something nobody finished writing".
    /// </para>
    /// <para>
    /// ⚠ Failure is logged and swallowed: the cache is an optimisation and a diagnostic aid, and a
    /// read-only or full disk must not take the app down when the schema is already resolved in
    /// memory. The caller has what it needs before this is ever called.
    /// </para>
    /// </remarks>
    internal async Task WriteAsync(string cacheFileName, byte[] artifact, SchemaCacheSidecar sidecar, CancellationToken ct)
    {
        string artifactPath = ArtifactPath(cacheFileName);
        SemaphoreSlim gate = Writers.GetOrAdd(artifactPath, static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            await WriteAtomicAsync(artifactPath, artifact, ct).ConfigureAwait(false);

            byte[] meta = System.Text.Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(sidecar, CoreJsonContext.Default.SchemaCacheSidecar));
            await WriteAtomicAsync(SidecarPath(cacheFileName), meta, ct).ConfigureAwait(false);

            Log.Debug("[Schema] Cached {File} ({Source})", cacheFileName, sidecar.Source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "[Schema] Could not cache {File} to disk; continuing from memory", cacheFileName);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Write via a temp file in the same directory, then replace.</summary>
    /// <remarks>
    /// Same directory so the move is a rename rather than a cross-volume copy — a copy is not
    /// atomic and would reintroduce the torn-file case this exists to prevent.
    /// </remarks>
    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken ct)
    {
        string temp = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // An orphaned temp is cosmetic; failing the write over it is not.
                    _ = ex;
                }
            }
        }
    }
}
