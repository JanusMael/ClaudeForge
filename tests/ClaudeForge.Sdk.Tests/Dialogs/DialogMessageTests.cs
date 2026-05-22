using Bennewitz.Ninja.ClaudeForge.Sdk.Dialogs;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Dialogs;

[TestClass]
public sealed class DialogMessageTests
{
    [TestMethod]
    public void Plain_WrapsStringAsSingleTextSegment()
    {
        DialogMessage msg = DialogMessage.Plain("hello world");

        Assert.AreEqual(1, msg.Segments.Count);
        Assert.AreEqual(DialogSegmentKind.Text, msg.Segments[0].Kind);
        Assert.AreEqual("hello world", msg.Segments[0].Value);
    }

    [TestMethod]
    public void Plain_NullInput_IsTreatedAsEmpty()
    {
        DialogMessage msg = DialogMessage.Plain(null!);

        Assert.AreEqual(1, msg.Segments.Count);
        Assert.AreEqual(string.Empty, msg.Segments[0].Value);
    }

    [TestMethod]
    public void Builder_AppendsSegmentsInOrder()
    {
        DialogMessage msg = DialogMessage.Builder()
                                         .Text("Apply '")
                                         .Bold("MyProfile")
                                         .Text("' to ")
                                         .Path("~/.claude/settings.json")
                                         .Text("? See ")
                                         .Hyperlink("docs", "https://example.com/docs")
                                         .Text(".")
                                         .Build();

        Assert.AreEqual(7, msg.Segments.Count);
        Assert.AreEqual(DialogSegmentKind.Text, msg.Segments[0].Kind);
        Assert.AreEqual(DialogSegmentKind.Bold, msg.Segments[1].Kind);
        Assert.AreEqual(DialogSegmentKind.Text, msg.Segments[2].Kind);
        Assert.AreEqual(DialogSegmentKind.Path, msg.Segments[3].Kind);
        Assert.AreEqual(DialogSegmentKind.Text, msg.Segments[4].Kind);
        Assert.AreEqual(DialogSegmentKind.Hyperlink, msg.Segments[5].Kind);
        Assert.AreEqual(DialogSegmentKind.Text, msg.Segments[6].Kind);
    }

    [TestMethod]
    public void Builder_HyperlinkSegment_CarriesUrl()
    {
        DialogMessage msg = DialogMessage.Builder()
                                         .Hyperlink("click here", "https://example.com")
                                         .Build();

        Assert.AreEqual(1, msg.Segments.Count);
        Assert.AreEqual(DialogSegmentKind.Hyperlink, msg.Segments[0].Kind);
        Assert.AreEqual("click here", msg.Segments[0].Value);
        Assert.AreEqual("https://example.com", msg.Segments[0].Url);
    }

    [TestMethod]
    public void Builder_PathSegment_HasNullUrl()
    {
        // Path segments don't carry a Url — the Value IS the path.
        DialogMessage msg = DialogMessage.Builder()
                                         .Path("/etc/hosts")
                                         .Build();

        Assert.IsNull(msg.Segments[0].Url);
    }

    [TestMethod]
    public void Builder_NullSegmentValue_IsTreatedAsEmpty()
    {
        // Null inputs to any builder method must not throw — they shouldn't
        // happen in practice but the call site is often a string.Format()
        // result that could legitimately be null.
        DialogMessage msg = DialogMessage.Builder()
                                         .Text(null!)
                                         .Bold(null!)
                                         .Path(null!)
                                         .Hyperlink(null!, null!)
                                         .Build();

        Assert.AreEqual(4, msg.Segments.Count);
        Assert.IsTrue(msg.Segments.All(s => s.Value == string.Empty));
    }

    // ── SdkDialogs factory tests ─────────────────────────────────────────

    [TestMethod]
    public void SaveSucceeded_NoPaths_ReturnsNoChangesMessage()
    {
        DialogMessage msg = SdkDialogs.SaveSucceeded([]);

        Assert.AreEqual(1, msg.Segments.Count);
        Assert.AreEqual("No changes to save.", msg.Segments[0].Value);
    }

    [TestMethod]
    public void SaveSucceeded_SinglePath_RendersPathSegment()
    {
        DialogMessage msg = SdkDialogs.SaveSucceeded(["~/.claude/settings.json"]);

        Assert.IsTrue(msg.Segments.Any(s => s.Kind == DialogSegmentKind.Path
                                            && s.Value == "~/.claude/settings.json"));
    }

    [TestMethod]
    public void SaveSucceeded_MultiplePaths_RendersOnePathSegmentPerFile()
    {
        DialogMessage msg = SdkDialogs.SaveSucceeded([
            "~/.claude/settings.json",
            "~/.claude/mcp.json",
            "~/.claude/CLAUDE.md",
        ]);

        List<DialogSegment> pathSegments = msg.Segments.Where(s => s.Kind == DialogSegmentKind.Path).ToList();
        Assert.AreEqual(3, pathSegments.Count);
        Assert.AreEqual("~/.claude/settings.json", pathSegments[0].Value);
        Assert.AreEqual("~/.claude/mcp.json", pathSegments[1].Value);
        Assert.AreEqual("~/.claude/CLAUDE.md", pathSegments[2].Value);
    }

    [TestMethod]
    public void SaveFailed_RendersTargetAsPath_AndErrorAsText()
    {
        DialogMessage msg = SdkDialogs.SaveFailed("/etc/locked.json", "Access denied");

        Assert.IsTrue(msg.Segments.Any(s => s.Kind == DialogSegmentKind.Path
                                            && s.Value == "/etc/locked.json"));
        Assert.IsTrue(msg.Segments.Any(s => s.Kind == DialogSegmentKind.Text
                                            && s.Value.Contains("Access denied")));
    }

    [TestMethod]
    public void SchemaValidationFailed_WithDocsUrl_AppendsHyperlink()
    {
        DialogMessage msg = SdkDialogs.SchemaValidationFailed(
            "model",
            "must be one of: sonnet, opus, haiku",
            docsUrl: "https://docs.claude.com/schema");

        Assert.IsTrue(msg.Segments.Any(s => s.Kind == DialogSegmentKind.Bold
                                            && s.Value == "model"));
        Assert.IsTrue(msg.Segments.Any(s => s.Kind == DialogSegmentKind.Hyperlink
                                            && s.Url == "https://docs.claude.com/schema"));
    }

    [TestMethod]
    public void SchemaValidationFailed_WithoutDocsUrl_OmitsHyperlink()
    {
        DialogMessage msg = SdkDialogs.SchemaValidationFailed("model", "must be one of: …");

        Assert.IsFalse(msg.Segments.Any(s => s.Kind == DialogSegmentKind.Hyperlink));
    }

    [TestMethod]
    public void NotInstalled_RendersProductBoldAndDocsHyperlink()
    {
        DialogMessage msg = SdkDialogs.NotInstalled("Claude Desktop", "https://example.com/install");

        Assert.IsTrue(msg.Segments.Any(s => s.Kind == DialogSegmentKind.Bold
                                            && s.Value == "Claude Desktop"));
        Assert.IsTrue(msg.Segments.Any(s => s.Kind == DialogSegmentKind.Hyperlink
                                            && s.Url == "https://example.com/install"));
    }
}