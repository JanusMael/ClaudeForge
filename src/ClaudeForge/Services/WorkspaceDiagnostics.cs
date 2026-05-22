using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Diagnostics;
using Bennewitz.Ninja.ClaudeForge.Sdk.Internal;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Services;

/// <summary>
/// App-side save-time logging of pending workspace changes.  Iterates the
/// SDK's dirty-document snapshots, computes diffs via
/// <see cref="JsonDiff.Compute"/>, and writes a redacted summary to the
/// rolling Serilog log so save-time decisions are inspectable post-mortem.
/// </summary>
/// <remarks>
/// <para>
/// This class is App-side (not SDK) because it depends on Serilog and on
/// the localized <see cref="Strings"/> resource bundle — both are
/// consumer-of-SDK concerns.  The SDK itself remains
/// logging-framework-agnostic.
/// </para>
/// <para>
/// SDK-first migration history (2026-05-01 → 2026-05-05):
/// <list type="bullet">
///   <item>Diff machinery moved to <see cref="JsonDiff"/> in
///     <c>ClaudeForge.Sdk.Diagnostics</c>.</item>
///   <item>Sensitive-key classifier moved to <see cref="SensitiveKeys"/>
///     in the same SDK namespace.</item>
///   <item>Compat wrappers (<c>DiffJsonObjects</c>, <c>IsSensitiveKey</c>,
///     <c>RedactedMarker</c>) removed; this file now calls the SDK
///     directly.</item>
/// </list>
/// </para>
/// </remarks>
internal static class WorkspaceDiagnostics
{
    /// <summary>
    /// Render every dirty-doc diff across both products into the rolling
    /// log. SDK clients may be <see langword="null"/> (pre-load); each
    /// non-null one contributes its own snapshot.
    /// </summary>
    internal static void LogPendingChanges(
        ClaudeConfigClientCore? claudeCodeSdk,
        ClaudeConfigClientCore? claudeDesktopSdk)
    {
        if (claudeCodeSdk is not null)
        {
            LogSdkChanges(claudeCodeSdk.SnapshotDirtyDocuments(), Strings.WorkspaceNameClaudeCode);
        }

        if (claudeDesktopSdk is not null)
        {
            LogSdkChanges(claudeDesktopSdk.SnapshotDirtyDocuments(), Strings.WorkspaceNameClaudeDesktop);
        }
    }

    /// <summary>
    /// Render the SDK dirty-doc snapshots into the rolling log. Mirrors
    /// the per-document iteration the GUI's save flow uses — one log
    /// section per dirty document, with the value-side redacted for
    /// secret-bearing keys.
    /// </summary>
    private static void LogSdkChanges(
        IReadOnlyList<DirtyDocumentSnapshot> snapshots,
        string workspaceName)
    {
        foreach (DirtyDocumentSnapshot doc in snapshots)
        {
            IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(doc.BaselineRoot, doc.CurrentRoot);
            if (diffs.Count == 0)
            {
                continue;
            }

            LogDiffs(workspaceName, doc.Scope, diffs);
        }
    }

    /// <summary>
    /// Render one document's pre-computed diffs into the rolling log,
    /// redacting any value-side text whose key is sensitive (see
    /// <see cref="SensitiveKeys.IsSensitive"/>).
    /// </summary>
    /// <remarks>
    /// Without redaction the full JSON value (including ANTHROPIC_API_KEY
    /// etc.) would land in the rolling log file and F12 debug window —
    /// both of which travel with bug reports.
    /// </remarks>
    internal static void LogDiffs(string workspaceName, ConfigScope scope, IReadOnlyList<PropertyDiff> diffs)
    {
        Log.Information("[Save] {Workspace} — {Scope}: {Count} pending change(s)",
            workspaceName, scope, diffs.Count);

        foreach (PropertyDiff d in diffs)
        {
            bool sensitive = SensitiveKeys.IsSensitive(d.Key);
            const string redact = SensitiveKeys.RedactedMarker;
            string detail = d.Kind switch
            {
                ChangeKind.Added => $"new = {(sensitive ? redact : d.NewValue)}",
                ChangeKind.Removed => $"old = {(sensitive ? redact : d.OldValue ?? "(null)")}",
                ChangeKind.Modified => sensitive
                    ? $"{redact} → {redact}"
                    : $"{d.OldValue ?? "(null)"} → {d.NewValue ?? "(null)"}",
                var _ => string.Empty,
            };
            Log.Information("[Save]   {Kind} {Key}: {Detail}", d.Kind, d.Key, detail);
        }
    }
}