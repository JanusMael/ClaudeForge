using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Diagnostics;
using Bennewitz.Ninja.ClaudeForge.Sdk.Internal;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Services;

/// <summary>
/// Builds the <see cref="SaveChangesDialogViewModel"/> shown before the
/// save / restore confirmation modal. Extracted from
/// <see cref="MainWindowViewModel"/> so the
/// god-class shrinks and the dialog assembly is independently testable.
/// </summary>
/// <remarks>
/// All members <c>internal static</c>. Pure functions over the SDK
/// dirty-doc snapshots; lifetime-bearing state (the SDK clients
/// themselves) is passed in by the caller.
/// </remarks>
internal static class SaveDialogBuilder
{
    /// <summary>
    /// Builds the structured ViewModel for the rich save-confirmation
    /// dialog. Returns <see langword="null"/> when no content actually
    /// differs from the baseline (e.g. the user pressed Save twice
    /// without editing anything).
    /// </summary>
    /// <param name="claudeCodeSdk">SDK client for the Claude Code product, or null pre-load.</param>
    /// <param name="claudeDesktopSdk">SDK client for the Claude Desktop product, or null pre-load.</param>
    /// <param name="isRestoreContext">
    /// When <see langword="true"/>, formats the dialog for a restore-flow
    /// confirmation ("will be restored to") rather than save ("will be
    /// written to").
    /// </param>
    internal static SaveChangesDialogViewModel? Build(
        ClaudeConfigClientCore? claudeCodeSdk,
        ClaudeConfigClientCore? claudeDesktopSdk,
        bool isRestoreContext = false)
    {
        List<SaveChangeSectionViewModel> sections = new();
        SaveDialogMode mode = isRestoreContext ? SaveDialogMode.Restore : SaveDialogMode.Save;
        string? actionVerb = mode == SaveDialogMode.Restore
            ? Strings.LabelWillBeRestoredTo
            : Strings.LabelWillBeWrittenTo;

        if (claudeCodeSdk is not null)
        {
            AppendSdkSections(sections, claudeCodeSdk.SnapshotDirtyDocuments(), Strings.WorkspaceNameClaudeCode,
                actionVerb);
        }

        if (claudeDesktopSdk is not null)
        {
            AppendSdkSections(sections, claudeDesktopSdk.SnapshotDirtyDocuments(), Strings.WorkspaceNameClaudeDesktop,
                actionVerb);
        }

        return sections.Count == 0
            ? null
            : new SaveChangesDialogViewModel
            {
                Sections = sections,
                Mode = mode,
            };
    }

    /// <summary>
    /// Build per-document <see cref="SaveChangeSectionViewModel"/> entries
    /// from the SDK dirty-doc snapshots, computing diffs via
    /// <see cref="JsonDiff.Compute"/> so the dialog and the rolling-log
    /// path see exactly the same structural diff.
    /// </summary>
    private static void AppendSdkSections(
        List<SaveChangeSectionViewModel> sections,
        IReadOnlyList<DirtyDocumentSnapshot> snapshots,
        string workspaceName,
        string actionVerb)
    {
        foreach (DirtyDocumentSnapshot doc in snapshots)
        {
            IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(doc.BaselineRoot, doc.CurrentRoot);
            if (diffs.Count == 0)
            {
                continue;
            }

            AppendSection(sections, workspaceName, doc.Scope, doc.FilePath, diffs, actionVerb);
        }
    }

    /// <summary>
    /// Build one <see cref="SaveChangeSectionViewModel"/> from pre-computed
    /// diffs and append it.
    /// </summary>
    private static void AppendSection(
        List<SaveChangeSectionViewModel> sections,
        string workspaceName,
        ConfigScope scope,
        string filePath,
        IReadOnlyList<PropertyDiff> diffs,
        string actionVerb)
    {
        List<SaveChangeEntryViewModel> entries = diffs.Select(d => new SaveChangeEntryViewModel
        {
            Kind = d.Kind,
            Key = d.Key,
            OldValue = d.OldValue is null ? null : TruncateJson(d.OldValue),
            NewValue = d.NewValue is null ? null : TruncateJson(d.NewValue),
            FullOldValue = d.OldValue,
            FullNewValue = d.NewValue,
        }).ToList();

        sections.Add(new SaveChangeSectionViewModel
        {
            WorkspaceName = workspaceName,
            ScopeText = scope.ToString().ToLowerInvariant(),
            Scope = scope,
            Entries = entries,
            FilePath = ToDisplayPath(filePath),
            ActionVerb = actionVerb,
        });
    }

    /// <summary>
    /// Converts an absolute file path into a display-friendly form:
    /// paths under the user's home directory are shown with a leading
    /// <c>~/</c> for consistency with the scope-legend table; paths
    /// outside the user profile are shown verbatim.
    /// </summary>
    private static string ToDisplayPath(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
        {
            return string.Empty;
        }

        string home = PlatformPaths.UserProfile;
        if (string.IsNullOrEmpty(home))
        {
            return absolutePath;
        }

        if (absolutePath.StartsWith(home, StringComparison.OrdinalIgnoreCase))
        {
            return "~" + absolutePath[home.Length..].Replace('\\', '/');
        }

        return absolutePath;
    }

    /// <summary>
    /// Truncate a JSON string to <paramref name="maxLen"/> characters,
    /// appending an ellipsis when truncation occurred. Returns
    /// <c>"(null)"</c> for null/empty input so the dialog never renders
    /// blank cells.
    /// </summary>
    private static string TruncateJson(string? s, int maxLen = 80)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "(null)";
        }

        if (s.Length <= maxLen)
        {
            return s;
        }

        return string.Concat(s.AsSpan(0, maxLen), "…");
    }
}