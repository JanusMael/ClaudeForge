using Bennewitz.Ninja.AgentForge.Sdk.Diagnostics;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Save;

/// <summary>
/// The wording the save / restore confirmation dialog shows, supplied by the host.
///
/// <para>
/// This is the <em>only</em> product knowledge in the save dialog. Everything else —
/// which documents are dirty, how their diffs are computed, how paths are shortened,
/// how values are truncated, how the summary counts read — is the same for any
/// layered-config product, so it lives on this side and the host hands over the
/// words.
/// </para>
/// <para>
/// Strings rather than resource keys, and every one <c>required</c>: a host that adds
/// a product must state its wording rather than inherit another's, and a missing
/// entry is a compile error instead of English leaking into a translated build. The
/// wording is also genuinely product-specific — what a save means, and what the user
/// must do afterwards for it to take effect, differs between agent products.
/// </para>
/// <para>
/// ⚠ Deliberately <b>not</b> a resource set of its own. The app's
/// <c>Strings.resx</c> carries nine locales and a parity test that is hard-wired to
/// that one directory; a second resource set here would be unguarded by it, so moving
/// translated keys into one would silently un-translate them everywhere but English.
/// </para>
/// </summary>
public sealed record SaveDialogText
{
    /// <summary>Window title in save mode.</summary>
    public required string SaveTitle { get; init; }

    /// <summary>Window title in restore mode.</summary>
    public required string RestoreTitle { get; init; }

    /// <summary>Primary button label in save mode.</summary>
    public required string SaveConfirmButton { get; init; }

    /// <summary>Primary button label in restore mode.</summary>
    public required string RestoreConfirmButton { get; init; }

    /// <summary>Dismiss button label, used in both modes.</summary>
    public required string CancelButton { get; init; }

    /// <summary>
    /// Save-mode summary line, a composite format string taking <c>{0}</c> = change
    /// count and <c>{1}</c> = file count.
    /// </summary>
    public required string SaveSummaryFormat { get; init; }

    /// <summary>Restore-mode counterpart of <see cref="SaveSummaryFormat"/>.</summary>
    public required string RestoreSummaryFormat { get; init; }

    /// <summary>Verb phrase above a section's destination path in save mode.</summary>
    public required string WillBeWrittenTo { get; init; }

    /// <summary>Verb phrase above a section's destination path in restore mode.</summary>
    public required string WillBeRestoredTo { get; init; }

    /// <summary>Screen-reader name for the "added" change pill.</summary>
    public required string KindAdded { get; init; }

    /// <summary>Screen-reader name for the "removed" change pill.</summary>
    public required string KindRemoved { get; init; }

    /// <summary>Screen-reader name for the "modified" change pill.</summary>
    public required string KindModified { get; init; }

    /// <summary>Window title for <paramref name="mode"/>.</summary>
    public string TitleFor(SaveDialogMode mode)
    {
        return mode == SaveDialogMode.Restore ? RestoreTitle : SaveTitle;
    }

    /// <summary>Primary button label for <paramref name="mode"/>.</summary>
    public string ConfirmButtonFor(SaveDialogMode mode)
    {
        return mode == SaveDialogMode.Restore ? RestoreConfirmButton : SaveConfirmButton;
    }

    /// <summary>Summary format string for <paramref name="mode"/>.</summary>
    public string SummaryFormatFor(SaveDialogMode mode)
    {
        return mode == SaveDialogMode.Restore ? RestoreSummaryFormat : SaveSummaryFormat;
    }

    /// <summary>Destination-path verb phrase for <paramref name="mode"/>.</summary>
    public string ActionVerbFor(SaveDialogMode mode)
    {
        return mode == SaveDialogMode.Restore ? WillBeRestoredTo : WillBeWrittenTo;
    }

    /// <summary>
    /// Screen-reader name for a change pill. The pill's visible content is a single
    /// +/-/~ glyph in a coloured square — meaningless without context to a blind user,
    /// and '~' is not obviously "modified" to a sighted one either.
    /// </summary>
    public string AccessibleNameFor(ChangeKind kind)
    {
        return kind switch
        {
            ChangeKind.Added => KindAdded,
            ChangeKind.Removed => KindRemoved,
            var _ => KindModified,
        };
    }
}
