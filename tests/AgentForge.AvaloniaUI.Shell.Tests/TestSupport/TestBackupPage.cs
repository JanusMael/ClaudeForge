using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Backup;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Tests.TestSupport;

/// <summary>
/// The backup page's options as a host other than ClaudeForge would supply them, built without the
/// app (plans/00008 step 4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every string is the name of its own property</b>, followed by the same placeholders the
/// shipped English carries. A test can therefore tell which slot the page used, and a format call
/// still has the arguments it expects, while no product wording reaches this project. ClaudeForge's
/// real options stay under test in <c>ClaudeForge.Tests</c>, which is what proves the app's wiring.
/// </para>
/// <para>
/// <b>The products are still Core's two Claude descriptors</b>, and the engine takes
/// <see cref="ClaudeEnvironment"/>, because Core has no other products or engine today. The neutral
/// test product is step 5; until then this is the honest double: neutral in what the HOST
/// supplies, and Claude-shaped only where Core itself still is.
/// </para>
/// </remarks>
internal static class TestBackupPage
{
    internal static IReadOnlyList<ProductDescriptor> DefaultProducts =>
        [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty), SchemaRegistry.ClaudeDesktopProduct];

    /// <summary>The process names the page warns about. Deliberately not any real agent's.</summary>
    internal static IReadOnlyList<string> AgentProcessNames => ["test-agent", "test-agent-gui"];

    internal const string CredentialsPathDisplay = "~/.test-agent/credentials";

    internal static string CheckboxLabelFor(ProductDescriptor product) => "Include " + product.Id;

    internal static string AbbreviationFor(ProductDescriptor product) => "Abbr-" + product.Id;

    internal static BackupPageOptions Options(IReadOnlyList<ProductDescriptor>? products = null)
    {
        IReadOnlyList<ProductDescriptor> chosen = products ?? DefaultProducts;
        return new BackupPageOptions
        {
            Engine = new BackupEngine(ClaudeEnvironment.Empty),
            Products = chosen,
            AgentProcessNames = AgentProcessNames,
            CredentialsPathDisplay = CredentialsPathDisplay,
            ProductCheckboxLabel = CheckboxLabelFor,
            Text = Text(chosen),
        };
    }

    internal static BackupPageText Text(IReadOnlyList<ProductDescriptor>? products = null)
    {
        IReadOnlyList<ProductDescriptor> chosen = products ?? DefaultProducts;

        // Keyed as the page keys them: abbreviations by archive folder, progress labels by each
        // section's ProgressLabelId plus the engine's own phase ids. Derived from the descriptors,
        // not copied from any host's list, so a section added to a product is labelled here too.
        Dictionary<string, string> abbreviations = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> progress = new(StringComparer.Ordinal);
        foreach (ProductDescriptor product in DefaultProducts.Concat(chosen))
        {
            abbreviations[product.ArchiveFolder] = AbbreviationFor(product);
            foreach (ProductArchiveSection section in product.Backup.Sections)
            {
                progress[section.ProgressLabelId] = "Progress:" + section.ProgressLabelId;
            }
        }

        foreach (string id in new[]
                 {
                     RestoreProgressIds.Applying, RestoreProgressIds.Projects,
                     RestoreProgressIds.Worktrees, RestoreProgressIds.Complete,
                     BackupProgressIds.DiscoveringProjects,
                 })
        {
            progress[id] = "Progress:" + id;
        }

        return new BackupPageText
        {
            ClientAbbreviations = abbreviations,
            ProgressLabels = progress,
            ButtonContinueWithoutSaving = "ButtonContinueWithoutSaving",
            ButtonDiscardAndRestore = "ButtonDiscardAndRestore",
            ButtonIncludeCredentialsConfirm = "ButtonIncludeCredentialsConfirm",
            ButtonOmitCredentials = "ButtonOmitCredentials",
            ButtonRestore = "ButtonRestore",
            ButtonRestoreAnyway = "ButtonRestoreAnyway",
            ButtonSaveDialog = "ButtonSaveDialog",
            DialogTitleBackupFailed = "DialogTitleBackupFailed",
            DialogTitleBackupWithoutSaving = "DialogTitleBackupWithoutSaving",
            DialogTitleCrossPlatformRestore = "DialogTitleCrossPlatformRestore",
            DialogTitleDiscardUnsavedEdits = "DialogTitleDiscardUnsavedEdits",
            DialogTitleDropRestore = "DialogTitleDropRestore",
            DialogTitleDropRestoreInvalid = "DialogTitleDropRestoreInvalid",
            DialogTitleIncludeCredentials = "DialogTitleIncludeCredentials",
            DialogTitleRestoreCompletedSkippedFmt = "DialogTitleRestoreCompletedSkippedFmt {0}",
            DialogTitleRestoreFailed = "DialogTitleRestoreFailed",
            DialogTitleUnsavedChanges = "DialogTitleUnsavedChanges",
            LabelBackupIncludesProject = "LabelBackupIncludesProject {0}",
            LabelBackupNoProjectOpen = "LabelBackupNoProjectOpen",
            LabelLastBackupDaysFmt = "LabelLastBackupDaysFmt {0}",
            LabelLastBackupHoursFmt = "LabelLastBackupHoursFmt {0}",
            LabelLastBackupJustNow = "LabelLastBackupJustNow",
            LabelLastBackupMinutesFmt = "LabelLastBackupMinutesFmt {0}",
            LabelLastBackupNever = "LabelLastBackupNever",
            ProgressCreatingJunction = "ProgressCreatingJunction",
            ProgressPreparing = "ProgressPreparing",
            ProgressRestoring = "ProgressRestoring",
            ProgressStarting = "ProgressStarting",
            StatusBackupDeleteFailedFmt = "StatusBackupDeleteFailedFmt {0}",
            StatusBackupDeletedFmt = "StatusBackupDeletedFmt {0}",
            StatusBackupWarningsFmt = "StatusBackupWarningsFmt {0}",
            StatusChooseBackupFolder = "StatusChooseBackupFolder",
            StatusAgentRunningFmt = "StatusAgentRunningFmt {0}",
            StatusShareArchiveRevealed = "StatusShareArchiveRevealed",
            StatusShareArchiveUnavailable = "StatusShareArchiveUnavailable",
            StatusShareArchiveFailed = "StatusShareArchiveFailed",
            TextCredentialsExplainer = "TextCredentialsExplainer",
            TextCrossPlatformRestoreMiddle = "TextCrossPlatformRestoreMiddle",
            TextCrossPlatformRestorePrefix = "TextCrossPlatformRestorePrefix",
            TextCrossPlatformRestoreSuffix = "TextCrossPlatformRestoreSuffix",
            TextDiscardUnsavedForRestore = "TextDiscardUnsavedForRestore",
            TextDropRestoreNotABackup = "TextDropRestoreNotABackup",
            TextDropRestoreNotZip = "TextDropRestoreNotZip",
            TextDropRestorePromptPrefix = "TextDropRestorePromptPrefix",
            TextDropRestorePromptSuffix = "TextDropRestorePromptSuffix",
            TextProceedWithoutSavingBackup = "TextProceedWithoutSavingBackup",
            TextRestoreSkippedExplainer = "TextRestoreSkippedExplainer",
            TextRestoreSkippedTrailerFmt = "TextRestoreSkippedTrailerFmt {0}",
            TextSaveBeforeBackupPrompt = "TextSaveBeforeBackupPrompt",
            TextSaveBeforeRestorePrompt = "TextSaveBeforeRestorePrompt",
        };
    }
}
