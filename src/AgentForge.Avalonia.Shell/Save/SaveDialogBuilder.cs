using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.AgentForge.Sdk.Diagnostics;
using Bennewitz.Ninja.AgentForge.Sdk.Internal;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

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
    /// Open clients paired with the name their changes are grouped under and the danger policy
    /// that applies to them. A sequence rather than one parameter per product — the dialog
    /// renders whatever it is handed, in order, and never needed to know how many there were.
    /// ⛔ The policy rides on each source rather than being one parameter here, because this
    /// dialog shows several products at once; see <see cref="DirtySource"/>.
    /// </param>
    /// <param name="text">The host's wording for titles, buttons and labels.</param>
    /// <param name="isRestoreContext">
    /// Switches the wording from "will be written to" to "will be restored to", and
    /// the title / buttons with it.
    /// </param>
    public static SaveChangesDialogViewModel? Build(
        IEnumerable<DirtySource> sources,
        SaveDialogText text,
        bool isRestoreContext = false)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(text);

        List<SaveChangeSectionViewModel> sections = [];
        SaveDialogMode mode = isRestoreContext ? SaveDialogMode.Restore : SaveDialogMode.Save;
        string actionVerb = text.ActionVerbFor(mode);

        foreach (DirtySource source in sources)
        {
            AppendSdkSections(sections, source.Client.SnapshotDirtyDocuments(),
                source.DisplayName, actionVerb, text, source.Danger);
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
        SaveDialogText text,
        IDangerClassifier? danger)
    {
        foreach (DirtyDocumentSnapshot doc in snapshots)
        {
            IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(doc.BaselineRoot, doc.CurrentRoot);
            if (diffs.Count == 0)
            {
                continue;
            }

            AppendSection(sections, workspaceName, doc.Scope, doc.FilePath, diffs, actionVerb, text,
                danger, doc.CurrentRoot);
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
        SaveDialogText text,
        IDangerClassifier? danger,
        JsonObject? currentRoot)
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
            Danger = Assess(danger, d, scope, currentRoot),
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
    /// Assess one pending change: how much the setting it touches matters, and whether the value
    /// about to be written is the unsafe one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>This surface CLASSIFIES rather than asking an editor</b>, for the same reason the
    /// effective view does: a pending change is a (path, target scope, new value) triple, which is
    /// exactly what classification takes. There is also no editor to ask — the dialog is built
    /// from document snapshots, not from the page the user was on, and the change may have come
    /// from a page that is no longer open.
    /// </para>
    /// <para>
    /// ⛔⛔ <b>The value comes from <paramref name="currentRoot"/>, NOT from
    /// <see cref="PropertyDiff.NewValue"/>, and the difference is a correctness bug rather than a
    /// tidiness one.</b> For an array change <c>JsonDiff</c> emits the ARRAY's path as the key but
    /// only the added/removed ELEMENT as the value. A rule written for
    /// <c>permissions.allow</c> expects a list and would be handed one element's string, match no
    /// type pattern, and answer "nothing wrong right now" — a silent false negative on exactly the
    /// keys this dialog exists to catch. Resolving the key against the document root yields the
    /// whole value that will be on disk after the save, which is the thing the user is actually
    /// about to commit to.
    /// </para>
    /// <para>
    /// ⚠ The scope is the DOCUMENT's — the file being written — which is what makes escalation
    /// meaningful here: writing a secret into the committed project file is the case that
    /// escalates, and the save dialog is the last moment anyone can stop it.
    /// </para>
    /// <para>
    /// A <see cref="ChangeKind.Removed"/> key simply does not resolve, so the value is
    /// <see langword="null"/> — correct, since the save removes it and "not set" is what the
    /// file will hold.
    /// </para>
    /// </remarks>
    private static DangerAssessment Assess(
        IDangerClassifier? danger,
        PropertyDiff diff,
        ConfigScope scope,
        JsonObject? currentRoot)
    {
        if (danger is null)
        {
            return DangerAssessment.Unremarkable;
        }

        return danger.Classify(
            diff.Key,
            ConfigScopeAdapter.For(scope),
            JsonCurrency.FromJsonNode(ResolveByPath(currentRoot, diff.Key)));
    }

    /// <summary>
    /// Walk a dotted key against a JSON object, returning the node it names or
    /// <see langword="null"/> when any segment is missing or not an object.
    /// </summary>
    private static JsonNode? ResolveByPath(JsonNode? root, string key)
    {
        JsonNode? node = root;
        foreach (string segment in key.Split('.'))
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(segment, out node))
            {
                return null;
            }
        }

        return node;
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
