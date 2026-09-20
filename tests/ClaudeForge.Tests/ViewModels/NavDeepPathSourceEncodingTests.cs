using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// A plugin artifact's source is itself a path
/// (<c>claude-plugins-official/plugins/math-olympiad</c>), and an item key carries
/// that source after an <c>@</c>. Emitted raw, those separators split into extra
/// segments and the whole path is rejected against
/// <see cref="NavDeepPath.MaxSegments"/> — which is what made place-keeping and
/// "Copy deep link" silently useless for every plugin artifact: the restore fell
/// back to the page's default tab.
/// </summary>
[TestClass]
public sealed class NavDeepPathSourceEncodingTests
{
    private const string PluginSource = "claude-plugins-official/plugins/math-olympiad";

    [TestMethod]
    public void FormatItemKey_PluginSource_ProducesOneSegment()
    {
        string key = NavDeepPath.FormatItemKey("math-olympiad", PluginSource);

        Assert.IsFalse(
            key.Contains(NavDeepPath.Separator),
            "An item key containing the segment separator cannot survive a round trip.");
    }

    /// <summary>The regression this fixes, stated as the whole path the app captures.</summary>
    [TestMethod]
    public void CapturedPluginPath_Parses()
    {
        string path = NavDeepPath.Format(
        [
            "agents-skills",
            "skills",
            NavDeepPath.FormatItemKey("math-olympiad", PluginSource),
        ]);

        Assert.IsTrue(
            NavDeepPath.TryParse(path, out IReadOnlyList<string> segments, out string? error),
            "The captured path must parse; it was rejected with: " + (error ?? "(none)"));
        Assert.AreEqual(3, segments.Count);
        Assert.AreEqual("agents-skills", segments[0]);
        Assert.AreEqual("skills", segments[1]);
    }

    [TestMethod]
    public void SplitItemKey_RoundTripsAnEncodedPluginSource()
    {
        string key = NavDeepPath.FormatItemKey("math-olympiad", PluginSource);
        (string name, string? source) = NavDeepPath.SplitItemKey(key);

        Assert.AreEqual("math-olympiad", name);
        Assert.AreEqual(NavDeepPath.EncodeSource(PluginSource), source);
    }

    [TestMethod]
    public void EncodeSource_IsIdempotent()
    {
        string once = NavDeepPath.EncodeSource(PluginSource);

        Assert.AreEqual(
            once,
            NavDeepPath.EncodeSource(once),
            "Encoding twice must not change the value, or comparisons that normalise both sides break.");
    }

    [TestMethod]
    public void EncodeSource_HandlesWindowsSeparators()
    {
        Assert.AreEqual(
            NavDeepPath.EncodeSource("a/b/c"),
            NavDeepPath.EncodeSource(@"a\b\c"),
            "A source read off a Windows path must encode the same as its forward-slash spelling.");
    }

    [TestMethod]
    public void EncodeSource_LeavesAPlainScopeAlone()
    {
        Assert.AreEqual("user", NavDeepPath.EncodeSource("user"));
    }

    [TestMethod]
    public void FormatItemKey_NoSource_IsJustTheName()
    {
        Assert.AreEqual("pdf", NavDeepPath.FormatItemKey("pdf", source: null));
        Assert.AreEqual("pdf", NavDeepPath.FormatItemKey("pdf", source: "   "));
    }
}
