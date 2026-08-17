namespace Bennewitz.Ninja.AgentForge.Jsonc.Tests;

/// <summary>
/// <see cref="TextEdit.Apply"/> is the narrowest, most reused piece of the library, so
/// its edge cases are worth pinning individually rather than only through the editor.
/// </summary>
[TestClass]
public sealed class TextEditTests
{
    [TestMethod]
    public void NoEdits_ReturnsTheOriginalInstanceContent()
    {
        Assert.AreEqual("abc", TextEdit.Apply("abc", []));
    }

    [TestMethod]
    public void MultipleEdits_ApplyAgainstOriginalOffsets_RegardlessOfOrderGiven()
    {
        // Given in ascending order, the naive implementation (apply front to back
        // without tracking a delta) gets the second edit wrong. Both orders must agree.
        TextEdit[] ascending =
        [
            new(0, 3, "XXXXX"),
            new(4, 3, "Y"),
        ];
        TextEdit[] descending = [ascending[1], ascending[0]];

        Assert.AreEqual("XXXXX Y", TextEdit.Apply("abc def", ascending));
        Assert.AreEqual("XXXXX Y", TextEdit.Apply("abc def", descending));
    }

    [TestMethod]
    public void Insertion_IsAZeroLengthEdit()
    {
        Assert.AreEqual("abXc", TextEdit.Apply("abc", [new TextEdit(2, 0, "X")]));
    }

    [TestMethod]
    public void Deletion_IsAnEmptyReplacement()
    {
        Assert.AreEqual("ac", TextEdit.Apply("abc", [new TextEdit(1, 1, string.Empty)]));
    }

    [TestMethod]
    public void EditAtTheVeryEnd_IsAllowed()
    {
        Assert.AreEqual("abc!", TextEdit.Apply("abc", [new TextEdit(3, 0, "!")]));
    }

    [TestMethod]
    public void OverlappingEdits_Throw_RatherThanProducingMangledText()
    {
        Assert.ThrowsExactly<InvalidOperationException>(
            () => TextEdit.Apply("abcdef", [new TextEdit(0, 3, "X"), new TextEdit(2, 3, "Y")]),
            "Two edits fighting over one span means the caller built an incoherent change "
            + "set; picking a winner would hide the bug.");
    }

    [TestMethod]
    public void InsertionInsideAnotherEditsSpan_CountsAsOverlapping()
    {
        Assert.ThrowsExactly<InvalidOperationException>(
            () => TextEdit.Apply("abcdef", [new TextEdit(0, 3, "X"), new TextEdit(1, 0, "Y")]));
    }

    [TestMethod]
    public void AdjacentEdits_AreNotOverlapping()
    {
        Assert.AreEqual("XY", TextEdit.Apply("abcdef",
                                             [new TextEdit(0, 3, "X"), new TextEdit(3, 3, "Y")]));
    }

    [TestMethod]
    public void EditPastTheEnd_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => TextEdit.Apply("abc", [new TextEdit(2, 5, "X")]));
    }

    [TestMethod]
    public void NegativeOffsets_Throw()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => TextEdit.Apply("abc", [new TextEdit(-1, 1, "X")]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => TextEdit.Apply("abc", [new TextEdit(0, -1, "X")]));
    }
}
