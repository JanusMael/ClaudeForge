using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.AgentForge.Sdk.Diagnostics;
using Bennewitz.Ninja.AgentForge.Sdk.Internal;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Save;

/// <summary>
/// Builds the <see cref="SaveChangesDialogViewModel"/> shown before the save / restore
/// confirmation modal.
/// </summary>
/// <remarks>
/// Pure functions over the SDK dirty-document snapshots; the lifetime-bearing state
/// (the clients themselves) is passed in by the caller. Nothing here knows which
/// product it is describing — the sources arrive already paired with the name their
/// changes are grouped under, and the wording arrives as
/// <see cref="SaveDialogText"/>.
/// </remarks>
public static class SaveDialogBuilder
{
    /// <summary>
    /// Builds the structured view-model for the save-confirmation dialog. Returns
    /// <see langword="null"/> when no content actually differs from the baseline (for
    /// example the user pressed Save twice without editing anything).
    /// </summary>
    /// <param name="sources">
    /// Open clients paired with the name their changes are grouped under. A sequence
    /// rather than one parameter per product — the dialog renders whatever it is
    /// handed, in order, and never needed to know how many there were.
    /// </param>
    /// <param name="text">The host's wording for titles, buttons and labels.</param>
    /// <param name="isRestoreContext">
    /// Switches the wording from "will be written to" to "will be restored to", and
    /// the title / buttons with it.
    /// </param>
    public static SaveChangesDialogViewModel? Build(
        IEnumerable<(AgentConfigClientCore Client, string DisplayName)> sources,
        SaveDialogText text,
        bool isRestoreContext = false)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(text);

        List<SaveChangeSectionViewModel> sections = [];
        SaveDialogMode mode = isRestoreContext ? SaveDialogMode.Restore : SaveDialogMode.Save;
        string actionVerb = text.ActionVerbFor(mode);

        foreach ((AgentConfigClientCore client, string displayName) in sources)
        {
            AppendSdkSections(sections, client.SnapshotDirtyDocuments(), displayName, actionVerb, text);
        }

        return sections.Count == 0
            ? null
            : new SaveChangesDialogViewModel
            {
                Sections = sections,
                Mode = mode,
                Text = text,
            };
    }

    /// <summary>
    /// Build per-document <see cref="SaveChangeSectionViewModel"/> entries from the SDK
    /// dirty-doc snapshots, computing diffs via <see cref="JsonDiff.Compute"/> so the
    /// dialog and the rolling-log path see exactly the same structural diff.
    /// </summary>
    private static void AppendSdkSections(
        List<SaveChangeSectionViewModel> sections,
        IReadOnlyList<DirtyDocumentSnapshot> snapshots,
        string workspaceName,
        string actionVerb,
        SaveDialogText text)
    {
        foreach (DirtyDocumentSnapshot doc in snapshots)
        {
            IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(doc.BaselineRoot, doc.CurrentRoot);
            if (diffs.Count == 0)
            {
                continue;
            }

            AppendSection(sections, workspaceName, doc.Scope, doc.FilePath, diffs, actionVerb, text);
        }
    }

    /// <summary>Build one section from pre-computed diffs and append it.</summary>
    private static void AppendSection(
        List<SaveChangeSectionViewModel> sections,
        string workspaceName,
        ConfigScope scope,
        string filePath,
        IReadOnlyList<PropertyDiff> diffs,
        string actionVerb,
        SaveDialogText text)
    {
        List<SaveChangeEntryViewModel> entries = diffs.Select(d => new SaveChangeEntryViewModel
        {
            Kind = d.Kind,
            Key = d.Key,
            OldValue = d.OldValue is null ? null : TruncateJson(d.OldValue),
            NewValue = d.NewValue is null ? null : TruncateJson(d.NewValue),
            FullOldValue = d.OldValue,
            FullNewValue = d.NewValue,
            KindAccessibleName = text.AccessibleNameFor(d.Kind),
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
    /// Converts an absolute file path into a display-friendly form: paths under the
    /// user's home directory are shown with a leading <c>~/</c> for consistency with
    /// the scope-legend table; paths outside the user profile are shown verbatim.
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
    /// Truncate a JSON string to <paramref name="maxLen"/> characters, appending an
    /// ellipsis when truncation occurred. Returns <c>"(null)"</c> for null/empty input
    /// so the dialog never renders blank cells.
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
