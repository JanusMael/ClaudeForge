using System.Globalization;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Backup;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Every restore phase this app can show has a localized label, and each one says what the
/// engine says.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>The failure this guards is silent by design.</b> A progress id with no entry in
/// <c>BackupPageText.RestoreProgressLabels</c> falls back to the engine's English, which is the
/// right behaviour — a blank progress bar would be worse — and is therefore invisible in every
/// locale the developer does not read. Nothing throws, nothing logs, and a translated build
/// simply drops into English mid-restore.
/// </para>
/// <para>
/// ⚠ <b>Coverage is taken from the DESCRIPTORS, never from a list in this file.</b> A test that
/// re-stated the five section ids would agree with a copy of the truth: adding a sixth section
/// would leave it green.
/// </para>
/// </remarks>
[TestClass]
public sealed class ClaudeBackupPageProgressTests
{
    [TestMethod]
    public void EverySectionAndEnginePhaseHasALabel()
    {
        BackupPageText text = BackupPageTestOptions.Create().Text;

        List<string> sectionIds =
        [
            .. ClaudeBackupPage.DefaultProducts
                .SelectMany(p => p.Backup.Sections)
                .Select(s => s.ProgressLabelId)
        ];

        Assert.IsTrue(sectionIds.Count > 0,
            "No archive sections were found on Claude's descriptors, so this assertion covers "
            + "nothing. The layout moved or DefaultProducts is empty.");

        List<string> missing =
        [
            .. sectionIds.Concat(RestoreProgressIds.All)
                         .Concat(BackupProgressIds.All)
                         .Where(id => !text.ProgressLabels.ContainsKey(id))
        ];

        Assert.AreEqual(0, missing.Count,
            "These restore progress ids have no label, so a translated build shows the engine's "
            + "English for them and nothing says so: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Each label means what the engine's English means.
    /// </summary>
    /// <remarks>
    /// ⭐ <b>The sibling above only proves a key EXISTS.</b> Two sections whose keys were swapped
    /// — "Restoring Desktop profiles…" against <c>claude-dir</c> — pass it and mislabel the
    /// progress bar for the life of the release. Comparing against the descriptor's own
    /// <c>ProgressLabel</c> is what makes the mapping itself the thing under test.
    /// <para>
    /// ⚠ Pinned to the invariant culture, which resolves to the neutral resx. Without it this
    /// test asserts the developer's own language and fails on a machine set to any of the eight
    /// translations.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EachLabelMatchesTheEnglishTheEngineWouldHaveShown()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            IReadOnlyDictionary<string, string> labels = BackupPageTestOptions.Create().Text.ProgressLabels;

            List<string> mismatches = [];
            foreach (ProductDescriptor product in ClaudeBackupPage.DefaultProducts)
            {
                foreach (ProductArchiveSection section in product.Backup.Sections)
                {
                    if (labels.TryGetValue(section.ProgressLabelId, out string? english)
                        && !string.Equals(english, section.ProgressLabel, StringComparison.Ordinal))
                    {
                        mismatches.Add(
                            $"{section.ProgressLabelId}: resx '{english}' vs descriptor '{section.ProgressLabel}'");
                    }
                }
            }

            Assert.AreEqual(0, mismatches.Count,
                "A section's English resx value disagrees with the label the engine reports, so "
                + "the localized progress bar says something different from the fallback: "
                + string.Join("; ", mismatches));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
