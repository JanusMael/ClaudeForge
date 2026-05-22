using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// <see cref="MainWindowViewModel.SanitiseExceptionForStatus"/>'s
/// path-scrubbing + truncation contract.
/// </summary>
/// <remarks>
/// Failure-kind status pills never auto-clear (they sit on screen
/// until the user clicks the × dismiss button) so the rendered text
/// must NOT leak filesystem paths, network detail, or other sensitive
/// internal state.  The helper trims absolute paths to their filename
/// component and caps the message length to ~120 chars.
/// </remarks>
[TestClass]
public sealed class SanitiseExceptionForStatusTests
{
    [TestMethod]
    public void StripsWindowsAbsolutePath_FromIOExceptionMessage()
    {
        // Construct the exact .NET IOException shape that motivated M1.
        IOException ex = new(@"Access to the path 'C:\Users\brian\.claude\settings.json' is denied.");
        string output = MainWindowViewModel.SanitiseExceptionForStatus(ex);

        Assert.IsFalse(output.Contains(@"C:\Users\brian", StringComparison.Ordinal),
            "Windows absolute path must not appear in the sanitised status text.");
        Assert.IsTrue(output.Contains("settings.json"),
            "Filename component must survive so the user sees WHICH file failed.");
    }

    [TestMethod]
    public void StripsPosixAbsolutePath_FromPermissionDeniedMessage()
    {
        UnauthorizedAccessException ex = new(
            @"Permission denied accessing /home/brian/.claude/settings.json");
        string output = MainWindowViewModel.SanitiseExceptionForStatus(ex);

        Assert.IsFalse(output.Contains("/home/brian", StringComparison.Ordinal),
            "POSIX home directory must not appear in the sanitised status text.");
        Assert.IsTrue(output.Contains("settings.json"));
    }

    [TestMethod]
    public void NamesExceptionType_AsPrefix()
    {
        IOException ex = new("disk full");
        string output = MainWindowViewModel.SanitiseExceptionForStatus(ex);

        Assert.IsTrue(output.StartsWith("IOException", StringComparison.Ordinal),
            $"Output must start with the exception type name; got: {output}");
    }

    [TestMethod]
    public void WithHintPath_AppendsFilenameOnly()
    {
        // Use a platform-correct hint path so Path.GetFileName (which the
        // production helper uses) extracts the leaf on every OS.  Previously
        // the test hard-coded "C:\\Users\\brian\\.claude\\settings.json", but
        // backslash is not a directory separator on Linux/macOS, so the
        // entire string was returned and the "on settings.json" assertion
        // failed there.  The dir-portion check (NOT-contains "C:\\Users")
        // is similarly skipped on non-Windows since the string never
        // contains that substring there.
        string hintPath = OperatingSystem.IsWindows()
            ? @"C:\Users\brian\.claude\settings.json"
            : "/home/brian/.claude/settings.json";
        string hintDirSubstring = OperatingSystem.IsWindows()
            ? @"C:\Users"
            : "/home/brian";

        IOException ex = new("disk full");
        string output = MainWindowViewModel.SanitiseExceptionForStatus(ex, hintPath: hintPath);

        Assert.IsTrue(output.Contains("on settings.json"),
            $"hintPath must surface its filename component as 'on <name>'. Got: {output}");
        Assert.IsFalse(output.Contains(hintDirSubstring, StringComparison.Ordinal),
            "Directory portion of hintPath must NOT appear.");
    }

    [TestMethod]
    public void WithoutHintPath_OmitsOnSuffix()
    {
        InvalidOperationException ex = new("bad state");
        string output = MainWindowViewModel.SanitiseExceptionForStatus(ex);

        Assert.IsFalse(output.Contains(" on "),
            "When no hint path is provided, 'on <file>' must be omitted.");
    }

    [TestMethod]
    public void TruncatesLongMessage()
    {
        string longMessage = new('x', 500);
        InvalidOperationException ex = new(longMessage);
        string output = MainWindowViewModel.SanitiseExceptionForStatus(ex);

        // Status-bar Failure pills shouldn't carry a 500-char message
        // pinned indefinitely; the cap is ~120 chars total.
        Assert.IsTrue(output.Length <= 150,
            $"Output should be capped near 120 chars; got {output.Length}.");
        Assert.IsTrue(output.EndsWith("…", StringComparison.Ordinal)
                      || output.Length < 150,
            "Truncated output should end with an ellipsis to signal the cap.");
    }

    [TestMethod]
    public void EmptyMessage_ProducesTypeOnlyOutput()
    {
        InvalidOperationException ex = new("");
        string output = MainWindowViewModel.SanitiseExceptionForStatus(ex);

        Assert.IsTrue(output.Contains("InvalidOperationException"),
            $"Type name must still appear when message is empty. Got: {output}");
    }
}