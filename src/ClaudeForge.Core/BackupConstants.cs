using System.Reflection;

namespace Bennewitz.Ninja.ClaudeForge.Core;

/// <summary>
/// Cross-cutting constants and metadata helpers shared by Core, the UI layer,
/// and the backup pipeline. Centralised so mismatches between the producer
/// (MainWindowViewModel writing -b4Forge files) and the consumers
/// (BackupEngine + ZipArchiveWriter skipping them during archival) cannot drift
/// silently — they all reference one symbol.
/// </summary>
public static class BackupConstants
{
    /// <summary>
    /// Suffix appended to a config file's path when ClaudeForge first writes to it,
    /// preserving the pre-edit content as <c>&lt;path&gt;-b4Forge</c>. Three call sites
    /// must agree on this exact spelling — the writer in MainWindowViewModel, the
    /// home-dir backup walker in BackupEngine, and the project-config archive walker
    /// in ZipArchiveWriter all skip files whose name ends with this suffix.
    /// </summary>
    public const string B4ForgeSuffix = "-b4Forge";

    /// <summary>
    /// Version string read from the entry assembly's <c>&lt;Version&gt;</c> attribute
    /// (set in the .csproj). Falls back to <c>"dev"</c> when running outside a
    /// published build (test runners, design-time tooling).
    /// </summary>
    /// <remarks>
    /// Computed on every access rather than cached in a static readonly so test
    /// harnesses that swap entry assemblies see the right value. The lookup is
    /// cheap; production code calls it at most a handful of times per session.
    /// </remarks>
    public static string AppVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "dev";
}