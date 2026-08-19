using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.OpenCode.Sdk.Permissions;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// The glob matcher behind every permission rule. Its mistakes are the dangerous kind: an
/// over-broad pattern grants access that was never written down, and an under-broad one makes
/// a deny rule inert.
/// </summary>
[TestClass]
public class OpenCodeGlobTests
{
    private string _sandbox = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "glob-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    [TestMethod]
    [DataRow("*", "anything at all")]
    [DataRow("git *", "git status")]
    [DataRow("git *", "git push --force")]
    [DataRow("*.md", "README.md")]
    [DataRow("npm ?", "npm i")]
    [DataRow("exact", "exact")]
    public void Matches(string pattern, string value)
        => Assert.IsTrue(OpenCodeGlob.IsMatch(pattern, value), $"'{pattern}' should match '{value}'.");

    [TestMethod]
    [DataRow("git *", "gitk")]
    [DataRow("git *", "npm install")]
    [DataRow("npm ?", "npm install")]
    [DataRow("*.md", "README.txt")]
    [DataRow("exact", "exactly")]
    public void DoesNotMatch(string pattern, string value)
        => Assert.IsFalse(OpenCodeGlob.IsMatch(pattern, value), $"'{pattern}' should not match '{value}'.");

    /// <summary>
    /// <c>git *</c> requires the space, so it does not match the bare command. That is what
    /// makes <c>git *</c> and <c>git commit *</c> different rules rather than one being a
    /// prefix of the other.
    /// </summary>
    [TestMethod]
    public void SpecificityComesFromTheLiteralPrefix()
    {
        Assert.IsTrue(OpenCodeGlob.IsMatch("git *", "git commit -m x"));
        Assert.IsTrue(OpenCodeGlob.IsMatch("git commit *", "git commit -m x"));
        Assert.IsFalse(OpenCodeGlob.IsMatch("git commit *", "git push"));
    }

    /// <summary>
    /// Everything that is not <c>*</c> or <c>?</c> is literal. Real commands are full of
    /// regex metacharacters, and a matcher that let them through would change what the user's
    /// rule means — <c>.</c> matching any character is the mild case.
    /// </summary>
    [TestMethod]
    [DataRow("rm -rf /tmp/x.y", "rm -rf /tmp/xAy")]
    [DataRow("a+b", "aab")]
    [DataRow("x(1)", "x1")]
    public void RegexMetacharactersAreLiteral(string pattern, string shouldNotMatch)
    {
        Assert.IsFalse(OpenCodeGlob.IsMatch(pattern, shouldNotMatch),
            $"'{pattern}' must be matched literally, not as a regular expression.");
    }

    [TestMethod]
    public void RegexMetacharactersStillMatchThemselves()
    {
        Assert.IsTrue(OpenCodeGlob.IsMatch("rm -rf /tmp/x.y", "rm -rf /tmp/x.y"));
        Assert.IsTrue(OpenCodeGlob.IsMatch("a+b", "a+b"));
    }

    /// <summary>
    /// An empty pattern matches only the empty string. Promoting it to a wildcard would let a
    /// blank row in an editor silently apply to everything.
    /// </summary>
    [TestMethod]
    public void EmptyPatternIsNotAWildcard()
    {
        Assert.IsFalse(OpenCodeGlob.IsMatch("", "anything"));
        Assert.IsTrue(OpenCodeGlob.IsMatch("", ""));
    }

    /// <summary>
    /// Case-sensitive on every platform. Case-folding would make <c>"Rm -rf *": "deny"</c>
    /// block <c>rm -rf</c> on Windows and not on Linux — the same config behaving differently
    /// per machine.
    /// </summary>
    [TestMethod]
    public void MatchingIsCaseSensitive()
        => Assert.IsFalse(OpenCodeGlob.IsMatch("git *", "GIT status"));

    // ── home expansion ───────────────────────────────────────────────────────

    [TestMethod]
    public void TildeExpandsToHome()
    {
        string key = Path.Combine(_sandbox, ".ssh", "id_rsa");

        Assert.IsTrue(OpenCodeGlob.IsMatch("~/.ssh/*", key.Replace('\\', '/')) ||
                      OpenCodeGlob.IsMatch("~\\.ssh\\*", key),
            "A rule written against ~ must match a real path under the home directory.");
    }

    [TestMethod]
    public void DollarHomeExpandsToo()
        => Assert.AreEqual(PlatformPaths.UserProfile, OpenCodeGlob.ExpandHome("$HOME"));

    [TestMethod]
    public void BareTildeExpands()
        => Assert.AreEqual(PlatformPaths.UserProfile, OpenCodeGlob.ExpandHome("~"));

    /// <summary>
    /// Leading only. A <c>~</c> mid-pattern is a legal filename character and common in shell
    /// text; expanding it there would rewrite patterns the user never meant as paths.
    /// </summary>
    [TestMethod]
    [DataRow("rm -rf ~backup")]
    [DataRow("cp a~b c")]
    [DataRow("echo $HOMEBREW_PREFIX")]
    public void HomeExpansionIsLeadingOnly(string pattern)
        => Assert.AreEqual(pattern, OpenCodeGlob.ExpandHome(pattern));

    /// <summary>
    /// Expansion is sandbox-aware because it goes through <c>PlatformPaths.UserProfile</c>. If
    /// it read the real home directory these tests would pass on a developer machine and mean
    /// nothing.
    /// </summary>
    [TestMethod]
    public void ExpansionHonoursTheTestSandbox()
        => StringAssert.StartsWith(OpenCodeGlob.ExpandHome("~/.config"), _sandbox);
}
