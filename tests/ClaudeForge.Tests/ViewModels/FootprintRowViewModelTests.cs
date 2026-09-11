using Bennewitz.Ninja.AgentForge.Sdk.Memory;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// pure-data wrapper for
/// <see cref="FootprintCategoryStats"/>; tests pin every category branch
/// of the HumanLabel + Tooltip switches plus the FormatBytes size
/// brackets (B / KB / MB / GB) + the Yes/No / passthrough plumbing.
/// </summary>
[TestClass]
public sealed class FootprintRowViewModelTests
{
    private static FootprintCategoryStats Stats(
        FootprintCategory category,
        string path = "/tmp/footprint",
        int fileCount = 0,
        long totalBytes = 0,
        bool inBackup = false)
    {
        return new FootprintCategoryStats(category, path, fileCount, totalBytes, inBackup);
    }

    // ── Passthrough getters (cover lines 19, 22) ────────────────────

    [TestMethod]
    public void PassthroughGetters_MirrorTheUnderlyingStats()
    {
        FootprintRowViewModel row = new(
            Stats(FootprintCategory.PromptHistory,
                path: "/home/u/.claude/history.jsonl",
                fileCount: 3,
                totalBytes: 4096,
                inBackup: true));

        Assert.AreEqual(FootprintCategory.PromptHistory, row.Category);
        Assert.AreEqual("/home/u/.claude/history.jsonl", row.AbsolutePath);
        Assert.AreEqual(3, row.FileCount);
        Assert.AreEqual(4096, row.TotalBytes);
        Assert.IsTrue(row.IsInStandardBackup);
    }

    // ── HumanLabel switch (lines 25-35) ─────────────────────────────

    // ⚠ These were seven [DataRow(FootprintCategory.X)] attributes each. FootprintCategory became
    // a struct when the category set moved into product data, and a struct is not a constant
    // expression, so it cannot be an attribute argument. Enumerating the catalog is the better
    // shape anyway: adding a category to the catalog now extends the test automatically, where
    // seven hand-written rows would have silently kept covering six of seven.
    [TestMethod]
    public void HumanLabel_EveryCategory_ReturnsNonEmptyLocalisedLabel()
    {
        Assert.AreEqual(7, FootprintCategory.All.Count, "Claude's catalog is seven categories.");

        foreach (FootprintCategory category in FootprintCategory.All)
        {
            FootprintRowViewModel row = new(Stats(category));
            Assert.IsFalse(string.IsNullOrEmpty(row.HumanLabel),
                $"Category {category} must have a localised HumanLabel.");
        }
    }

    [TestMethod]
    public void HumanLabel_CategoryFromAnotherProductsCatalog_FallsBackToToString()
    {
        // ⭐ This replaces a `(FootprintCategory)999` cast, and it is a better test than the one it
        // replaces. An out-of-range enum value was never a state the app could reach; a category
        // from a DIFFERENT product's catalog is exactly the state this conversion introduces, and
        // it is what the fallback arm is now for.
        FootprintRowViewModel row = new(Stats(OtherProductCategory()));

        Assert.AreEqual("SomethingElse", row.HumanLabel,
            "A category this app has no resx label for must fall back to its name, not crash.");
    }

    // ── Tooltip switch (line 58 default) ────────────────────────────

    [TestMethod]
    public void Tooltip_EveryCategory_ReturnsNonEmptyDescription()
    {
        foreach (FootprintCategory category in FootprintCategory.All)
        {
            FootprintRowViewModel row = new(Stats(category));
            Assert.IsFalse(string.IsNullOrEmpty(row.Tooltip),
                $"Category {category} must have a localised Tooltip.");
        }
    }

    [TestMethod]
    public void Tooltip_CategoryFromAnotherProductsCatalog_ReturnsEmpty()
    {
        FootprintRowViewModel row = new(Stats(OtherProductCategory()));
        Assert.AreEqual(string.Empty, row.Tooltip);
    }

    /// <summary>
    /// A category from a catalog that is not <see cref="FootprintCatalog.Default"/> — the stand-in
    /// for a second product whose footprint this app has no labels for.
    /// </summary>
    private static FootprintCategory OtherProductCategory()
    {
        FootprintCatalog catalog = new(
        [
            new FootprintCategoryDefinition(
                Id: "something-else",
                Sources: [FootprintSource.Directory("data", "somewhere")],
                Anchor: FootprintSource.Directory("data", "somewhere"),
                IsInStandardBackup: true),
        ]);

        return catalog.CategoryAt(0);
    }

    // ── InStandardBackupLabel (line 42) ─────────────────────────────

    [TestMethod]
    public void InStandardBackupLabel_True_ReturnsLocalisedYes()
    {
        FootprintRowViewModel row = new(Stats(FootprintCategory.PromptHistory, inBackup: true));
        // The exact text depends on locale; just verify it's the Yes
        // branch — non-empty and != the No-branch text.
        FootprintRowViewModel rowFalse = new(Stats(FootprintCategory.PromptHistory, inBackup: false));
        Assert.IsFalse(string.IsNullOrEmpty(row.InStandardBackupLabel));
        Assert.AreNotEqual(row.InStandardBackupLabel, rowFalse.InStandardBackupLabel,
            "Yes and No branches must produce distinct text.");
    }

    // ── HumanSize / FormatBytes brackets (lines 69-71) ──────────────

    [TestMethod]
    public void HumanSize_BytesUnder1Kb_RendersAsBytes()
    {
        FootprintRowViewModel row = new(Stats(FootprintCategory.Todos, totalBytes: 512));
        Assert.AreEqual("512 B", row.HumanSize);
    }

    [TestMethod]
    public void HumanSize_KilobyteBracket_RendersAsKb()
    {
        // Just over 1 KB — exercises the KB branch (line 71).
        FootprintRowViewModel row = new(Stats(FootprintCategory.Todos, totalBytes: 2 * 1024));
        Assert.AreEqual("2.0 KB", row.HumanSize);
    }

    [TestMethod]
    public void HumanSize_MegabyteBracket_RendersAsMb()
    {
        // 2.5 MB — exercises the MB branch (line 70).
        long bytes = (long)(2.5 * 1024 * 1024);
        FootprintRowViewModel row = new(Stats(FootprintCategory.Todos, totalBytes: bytes));
        Assert.AreEqual("2.5 MB", row.HumanSize);
    }

    [TestMethod]
    public void HumanSize_GigabyteBracket_RendersAsGb()
    {
        // 1.5 GB — exercises the GB branch (line 69).  This is also the
        // user-reported 3.3 GB scenario (smaller, but same bracket).
        long bytes = (long)(1.5 * 1024L * 1024 * 1024);
        FootprintRowViewModel row = new(Stats(FootprintCategory.SessionTranscripts, totalBytes: bytes));
        Assert.AreEqual("1.5 GB", row.HumanSize);
    }

    [TestMethod]
    public void HumanSize_ZeroBytes_RendersAsBytes()
    {
        // Edge case: a category with no files at all.  Must NOT crash on
        // the zero-bytes path.
        FootprintRowViewModel row = new(Stats(FootprintCategory.Todos, totalBytes: 0));
        Assert.AreEqual("0 B", row.HumanSize);
    }
}