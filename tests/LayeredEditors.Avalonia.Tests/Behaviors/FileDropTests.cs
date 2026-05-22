using Bennewitz.Ninja.LayeredEditors.Avalonia.Behaviors;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Behaviors;

/// <summary>
/// Unit tests for the pure-helper surface of the
/// <see cref="LayeredEditors.Avalonia.Behaviors.FileDrop"/> attached
/// behaviour.  The visual-tree-coupled parts (Avalonia property-change
/// hookup, <c>DragOverEvent</c> / <c>DropEvent</c> wiring) require a
/// headless Avalonia harness and are covered by the integration tests
/// in <c>BackupRestoreViewModelTests.RestoreFromDroppedArchive_*</c>
/// which exercise the end-to-end VM-command path.
///
/// This class pins the extension-filter logic — the user-visible
/// "accept .zip but not .json" contract — so the AXAML
/// <c>AllowedExtensions="zip"</c> string contract stays stable.
/// </summary>
[TestClass]
public sealed class FileDropTests
{
    // ── ParseExtensions ──────────────────────────────────────────────────────

    [TestMethod]
    public void ParseExtensions_Null_ReturnsNullForAnyAccept()
    {
        Assert.IsNull(FileDrop.ParseExtensions(null),
            "Null input must return null (the 'accept any file' sentinel) so the " +
            "behaviour skips per-extension filtering for callers that omit the property.");
    }

    [TestMethod]
    public void ParseExtensions_EmptyOrWhitespace_ReturnsNullForAnyAccept()
    {
        Assert.IsNull(FileDrop.ParseExtensions(""));
        Assert.IsNull(FileDrop.ParseExtensions("   "));
    }

    [TestMethod]
    public void ParseExtensions_SingleEntry_StripsLeadingDotAndLowercases()
    {
        CollectionAssert.AreEqual(new[] { "zip" }, FileDrop.ParseExtensions("zip"));
        CollectionAssert.AreEqual(new[] { "zip" }, FileDrop.ParseExtensions(".zip"));
        CollectionAssert.AreEqual(new[] { "zip" }, FileDrop.ParseExtensions("ZIP"));
        CollectionAssert.AreEqual(new[] { "zip" }, FileDrop.ParseExtensions(".ZIP"));
    }

    [TestMethod]
    public void ParseExtensions_MultipleEntries_TrimsAndNormalises()
    {
        CollectionAssert.AreEqual(new[] { "zip", "json" },
            FileDrop.ParseExtensions("zip,json"));
        CollectionAssert.AreEqual(new[] { "zip", "json" },
            FileDrop.ParseExtensions(" zip , json "));
        CollectionAssert.AreEqual(new[] { "zip", "json" },
            FileDrop.ParseExtensions(".ZIP, .Json"));
    }

    [TestMethod]
    public void ParseExtensions_EmptyEntries_AreDropped()
    {
        // Tolerate trailing commas / repeated separators rather than producing
        // empty-string entries that would fail to match anything.
        CollectionAssert.AreEqual(new[] { "zip" }, FileDrop.ParseExtensions("zip,"));
        CollectionAssert.AreEqual(new[] { "zip" }, FileDrop.ParseExtensions(",zip"));
        CollectionAssert.AreEqual(new[] { "zip", "json" },
            FileDrop.ParseExtensions("zip,,json"));
    }

    // ── HasAcceptedExtension ─────────────────────────────────────────────────

    [TestMethod]
    public void HasAcceptedExtension_NullAllowedList_AcceptsAnyFile()
    {
        // Null allowed-list is the "accept any file" sentinel returned by
        // ParseExtensions when AllowedExtensions is unset.  Must accept
        // every reasonable filename, including files with no extension at
        // all.
        Assert.IsTrue(FileDrop.HasAcceptedExtension("foo.zip", null));
        Assert.IsTrue(FileDrop.HasAcceptedExtension("anything.txt", null));
        Assert.IsTrue(FileDrop.HasAcceptedExtension("README", null));
    }

    [TestMethod]
    public void HasAcceptedExtension_EmptyAllowedList_AcceptsAnyFile()
    {
        // Empty array (vs null) treated identically — same "accept any"
        // semantic since the user expressed no filter.
        Assert.IsTrue(FileDrop.HasAcceptedExtension("foo.zip", []));
    }

    [TestMethod]
    public void HasAcceptedExtension_MatchingExtension_Accepted()
    {
        Assert.IsTrue(FileDrop.HasAcceptedExtension("backup-2026.zip", ["zip"]));
        Assert.IsTrue(FileDrop.HasAcceptedExtension("profile.json", ["zip", "json"]));
    }

    [TestMethod]
    public void HasAcceptedExtension_CaseInsensitive()
    {
        // Real-world drag from Windows Explorer often surfaces filenames
        // case-preserved from the user's typing; the filter must NOT
        // reject "FILE.ZIP" when the AXAML says AllowedExtensions="zip".
        Assert.IsTrue(FileDrop.HasAcceptedExtension("BACKUP.ZIP", ["zip"]));
        Assert.IsTrue(FileDrop.HasAcceptedExtension("Backup.Zip", ["zip"]));
        Assert.IsTrue(FileDrop.HasAcceptedExtension("backup.zip", ["zip"]));
    }

    [TestMethod]
    public void HasAcceptedExtension_NonMatchingExtension_Rejected()
    {
        Assert.IsFalse(FileDrop.HasAcceptedExtension("photo.png", ["zip"]));
        Assert.IsFalse(FileDrop.HasAcceptedExtension("doc.txt", ["zip", "json"]));
    }

    [TestMethod]
    public void HasAcceptedExtension_NoExtension_Rejected()
    {
        // A file without a dot must NOT match a filtered list — the user
        // explicitly asked for .zip and the dropped item has no extension
        // at all, so the answer is no.
        Assert.IsFalse(FileDrop.HasAcceptedExtension("README", ["zip"]));
        Assert.IsFalse(FileDrop.HasAcceptedExtension("Makefile", ["zip", "json"]));
    }

    [TestMethod]
    public void HasAcceptedExtension_EmptyFileName_Rejected()
    {
        Assert.IsFalse(FileDrop.HasAcceptedExtension("", ["zip"]));
        Assert.IsFalse(FileDrop.HasAcceptedExtension("", null));
    }

    [TestMethod]
    public void HasAcceptedExtension_MultiDotName_UsesLastSegment()
    {
        // backup.2026-05.zip → extension is "zip" (last segment after the
        // final dot), not "2026-05.zip".  Important for date-stamped
        // filenames which the Backup feature itself produces.
        Assert.IsTrue(FileDrop.HasAcceptedExtension("backup.2026-05.zip", ["zip"]));
        Assert.IsTrue(FileDrop.HasAcceptedExtension("archive.tar.gz", ["gz"]));
        Assert.IsFalse(FileDrop.HasAcceptedExtension("archive.tar.gz", ["tar"]));
    }
}