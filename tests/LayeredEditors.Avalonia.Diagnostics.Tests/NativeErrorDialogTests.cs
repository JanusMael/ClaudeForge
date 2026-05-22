using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Dialogs;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Tests;

/// <summary>
/// Tests for <see cref="NativeErrorDialog.ShowFatalError"/>.
/// <para>
/// The primary contract is that the method never throws, even when the platform
/// helpers (<c>zenity</c>, <c>osascript</c>, Win32 MessageBox) are unavailable.
/// The internal <c>SuppressForTests</c> flag (exposed via
/// <c>InternalsVisibleTo</c>) is set to <c>true</c> so that no real OS dialog is
/// shown during the test run.
/// </para>
/// </summary>
[TestClass]
public sealed class NativeErrorDialogTests
{
    [TestInitialize]
    public void Init()
    {
        NativeErrorDialog.SuppressForTests = true;
    }

    [TestCleanup]
    public void Cleanup()
    {
        NativeErrorDialog.Reset();
    }

    // -----------------------------------------------------------------------
    // Never-throws contract
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ShowFatalError_DoesNotThrow_WithNormalInput()
    {
        NativeErrorDialog.ShowFatalError("Test Title", "Test message.");
    }

    [TestMethod]
    public void ShowFatalError_DoesNotThrow_WithEmptyStrings()
    {
        NativeErrorDialog.ShowFatalError(string.Empty, string.Empty);
    }

    [TestMethod]
    public void ShowFatalError_DoesNotThrow_WithExceptionToString()
    {
        InvalidOperationException ex = new("something went wrong",
            new ArgumentNullException("inner"));
        NativeErrorDialog.ShowFatalError("Sample App — Fatal Error", ex.ToString());
    }

    [TestMethod]
    public void ShowFatalError_DoesNotThrow_WithSpecialCharacters()
    {
        NativeErrorDialog.ShowFatalError(
            "\"Error\" with special chars: \\ / ' \"",
            "Line1\nLine2\r\nLine3\t<tab>");
    }

    // -----------------------------------------------------------------------
    // Argument recording (via the suppression seam)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ShowFatalError_RecordsTitle_WhenSuppressed()
    {
        NativeErrorDialog.ShowFatalError("My Title", "My Message");
        Assert.AreEqual("My Title", NativeErrorDialog.LastSuppressedCall.Title);
    }

    [TestMethod]
    public void ShowFatalError_RecordsMessage_WhenSuppressed()
    {
        NativeErrorDialog.ShowFatalError("Title", "Detailed message text");
        Assert.AreEqual("Detailed message text", NativeErrorDialog.LastSuppressedCall.Message);
    }

    [TestMethod]
    public void Reset_ClearsSuppressedCallRecord()
    {
        NativeErrorDialog.ShowFatalError("A", "B");
        NativeErrorDialog.Reset();

        Assert.IsNull(NativeErrorDialog.LastSuppressedCall.Title);
        Assert.IsNull(NativeErrorDialog.LastSuppressedCall.Message);
        NativeErrorDialog.SuppressForTests = true;
    }
}