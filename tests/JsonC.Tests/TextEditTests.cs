namespace Bennewitz.Ninja.JsonC.Tests;

/// <summary>
/// <see cref="TextEdit.Apply"/> is the narrowest, most reused piece of the library, so
/// its edge cases are worth pinning individually rather than only through the editor.
/// </summary>
public sealed class TextEditTests
{
    [Fact]
    public void NoEdits_ReturnsTheOriginalInstanceContent()
    {
        Assert.Equal("abc", TextEdit.Apply("abc", []));
    }

    [Fact]
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

        Assert.Equal("XXXXX Y", TextEdit.Apply("abc def", ascending));
        Assert.Equal("XXXXX Y", TextEdit.Apply("abc def", descending));
    }

    [Fact]
    public void Insertion_IsAZeroLengthEdit()
    {
        Assert.Equal("abXc", TextEdit.Apply("abc", [new TextEdit(2, 0, "X")]));
    }

    [Fact]
    public void Deletion_IsAnEmptyReplacement()
    {
        Assert.Equal("ac", TextEdit.Apply("abc", [new TextEdit(1, 1, string.Empty)]));
    }

    [Fact]
    public void EditAtTheVeryEnd_IsAllowed()
    {
        Assert.Equal("abc!", TextEdit.Apply("abc", [new TextEdit(3, 0, "!")]));
    }

    [Fact]
    public void OverlappingEdits_Throw_RatherThanProducingMangledText()
    {
        MessageAssert.Throws<InvalidOperationException>(
            () => TextEdit.Apply("abcdef", [new TextEdit(0, 3, "X"), new TextEdit(2, 3, "Y")]),
            "Two edits fighting over one span means the caller built an incoherent change "
            + "set; picking a winner would hide the bug.");
    }

    [Fact]
    public void InsertionInsideAnotherEditsSpan_CountsAsOverlapping()
    {
        Assert.Throws<InvalidOperationException>(
            () => TextEdit.Apply("abcdef", [new TextEdit(0, 3, "X"), new TextEdit(1, 0, "Y")]));
    }

    [Fact]
    public void AdjacentEdits_AreNotOverlapping()
    {
        Assert.Equal("XY", TextEdit.Apply("abcdef",
                                             [new TextEdit(0, 3, "X"), new TextEdit(3, 3, "Y")]));
    }

    [Fact]
    public void EditPastTheEnd_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextEdit.Apply("abc", [new TextEdit(2, 5, "X")]));
    }

    [Fact]
    public void NegativeOffsets_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextEdit.Apply("abc", [new TextEdit(-1, 1, "X")]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextEdit.Apply("abc", [new TextEdit(0, -1, "X")]));
    }
}
