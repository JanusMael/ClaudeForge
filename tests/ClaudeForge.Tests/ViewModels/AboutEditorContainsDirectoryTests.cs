using System.Runtime.InteropServices;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Covers <see cref="AboutEditorViewModel.ContainsDirectory"/> — the
/// static helper used by the "Add to PATH" flow to decide whether the
/// resolved Claude Code install directory is already an entry on the
/// persistent User PATH. False-negatives here would append duplicate
/// entries to the registry; false-positives would silently skip the add.
/// </summary>
[TestClass]
public sealed class AboutEditorContainsDirectoryTests
{
    [TestMethod]
    public void ContainsDirectory_Empty_ReturnsFalse()
    {
        Assert.IsFalse(AboutEditorViewModel.ContainsDirectory("", @"C:/tools"));
        Assert.IsFalse(AboutEditorViewModel.ContainsDirectory(@"C:/tools", ""));
    }

    [TestMethod]
    public void ContainsDirectory_ExactMatch_ReturnsTrue()
    {
        char sep = Path.PathSeparator;
        string dir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\tools" : "/usr/local/bin";
        string other = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\other" : "/opt/bin";
        string path = $"{other}{sep}{dir}{sep}{other}";

        Assert.IsTrue(AboutEditorViewModel.ContainsDirectory(path, dir));
    }

    [TestMethod]
    public void ContainsDirectory_TrailingSeparatorIgnored()
    {
        // A directory like "C:\tools\" should match the same entry without a
        // trailing separator — registry entries and Path.GetDirectoryName()
        // results disagree on trailing slashes, so we normalise both sides.
        char sep = Path.PathSeparator;
        string dir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\tools" : "/usr/local/bin";
        string withTrailing = dir + Path.DirectorySeparatorChar;
        string path = $"{dir}{sep}{withTrailing}";

        Assert.IsTrue(AboutEditorViewModel.ContainsDirectory(path, dir));
        Assert.IsTrue(AboutEditorViewModel.ContainsDirectory(path, withTrailing));
    }

    [TestMethod]
    public void ContainsDirectory_CaseInsensitiveOnWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Case-insensitive comparison is Windows-specific");
            return;
        }

        string path = @"C:\Tools\Foo;C:\WINDOWS\system32";
        Assert.IsTrue(AboutEditorViewModel.ContainsDirectory(path, @"c:\tools\foo"));
        Assert.IsTrue(AboutEditorViewModel.ContainsDirectory(path, @"C:\Windows\System32"));
    }

    [TestMethod]
    public void ContainsDirectory_NoMatch_ReturnsFalse()
    {
        char sep = Path.PathSeparator;
        string path = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $@"C:\Windows{sep}C:\Windows\System32"
            : $"/usr/bin{sep}/usr/local/bin";
        string absent = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? @"C:\Users\u\AppData\Roaming\npm"
            : "/opt/claude";

        Assert.IsFalse(AboutEditorViewModel.ContainsDirectory(path, absent));
    }

    [TestMethod]
    public void ContainsDirectory_WhitespacePaddedEntryStillMatches()
    {
        // Real-world User PATH values often contain stray spaces after `;`
        // separators (bad edits from GUI tools). Our matcher trims entries
        // before comparing so a padded entry still resolves correctly.
        string dir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\tools" : "/usr/local/bin";
        string path = $" {dir} {Path.PathSeparator} /other";

        Assert.IsTrue(AboutEditorViewModel.ContainsDirectory(path, dir));
    }

    [TestMethod]
    public void ContainsDirectory_EmptyEntriesAreSkipped()
    {
        // Consecutive separators ("C:\a;;C:\b") produce empty split tokens.
        // Make sure we don't accidentally match the needle against one.
        char sep = Path.PathSeparator;
        string dir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\tools" : "/usr/local/bin";
        string path = $"{sep}{sep}{dir}{sep}{sep}";

        Assert.IsTrue(AboutEditorViewModel.ContainsDirectory(path, dir));
        Assert.IsFalse(AboutEditorViewModel.ContainsDirectory(path, ""));
    }
}