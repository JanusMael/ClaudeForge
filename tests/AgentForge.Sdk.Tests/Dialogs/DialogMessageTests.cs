using Bennewitz.Ninja.AppServices.Abstractions.Dialogs;
using Bennewitz.Ninja.AgentForge.Sdk.Dialogs;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Dialogs;

public sealed class DialogMessageTests
{
    [Fact]
    public void Plain_WrapsStringAsSingleTextSegment()
    {
        DialogMessage msg = DialogMessage.Plain("hello world");

        Assert.Single(msg.Segments);
        Assert.Equal(DialogSegmentKind.Text, msg.Segments[0].Kind);
        Assert.Equal("hello world", msg.Segments[0].Value);
    }

    [Fact]
    public void Plain_NullInput_IsTreatedAsEmpty()
    {
        DialogMessage msg = DialogMessage.Plain(null!);

        Assert.Single(msg.Segments);
        Assert.Equal(string.Empty, msg.Segments[0].Value);
    }

    [Fact]
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

        Assert.Equal(7, msg.Segments.Count);
        Assert.Equal(DialogSegmentKind.Text, msg.Segments[0].Kind);
        Assert.Equal(DialogSegmentKind.Bold, msg.Segments[1].Kind);
        Assert.Equal(DialogSegmentKind.Text, msg.Segments[2].Kind);
        Assert.Equal(DialogSegmentKind.Path, msg.Segments[3].Kind);
        Assert.Equal(DialogSegmentKind.Text, msg.Segments[4].Kind);
        Assert.Equal(DialogSegmentKind.Hyperlink, msg.Segments[5].Kind);
        Assert.Equal(DialogSegmentKind.Text, msg.Segments[6].Kind);
    }

    [Fact]
    public void Builder_HyperlinkSegment_CarriesUrl()
    {
        DialogMessage msg = DialogMessage.Builder()
                                         .Hyperlink("click here", "https://example.com")
                                         .Build();

        Assert.Single(msg.Segments);
        Assert.Equal(DialogSegmentKind.Hyperlink, msg.Segments[0].Kind);
        Assert.Equal("click here", msg.Segments[0].Value);
        Assert.Equal("https://example.com", msg.Segments[0].Url);
    }

    [Fact]
    public void Builder_PathSegment_HasNullUrl()
    {
        // Path segments don't carry a Url — the Value IS the path.
        DialogMessage msg = DialogMessage.Builder()
                                         .Path("/etc/hosts")
                                         .Build();

        Assert.Null(msg.Segments[0].Url);
    }

    [Fact]
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

        Assert.Equal(4, msg.Segments.Count);
        Assert.True(msg.Segments.All(s => s.Value == string.Empty));
    }

    // ── SdkDialogs factory tests ─────────────────────────────────────────

    [Fact]
    public void SaveSucceeded_NoPaths_ReturnsNoChangesMessage()
    {
        DialogMessage msg = SdkDialogs.SaveSucceeded([]);

        Assert.Single(msg.Segments);
        Assert.Equal("No changes to save.", msg.Segments[0].Value);
    }

    [Fact]
    public void SaveSucceeded_SinglePath_RendersPathSegment()
    {
        DialogMessage msg = SdkDialogs.SaveSucceeded(["~/.claude/settings.json"]);

        Assert.Contains(msg.Segments, s => s.Kind == DialogSegmentKind.Path
                                            && s.Value == "~/.claude/settings.json");
    }

    [Fact]
    public void SaveSucceeded_MultiplePaths_RendersOnePathSegmentPerFile()
    {
        DialogMessage msg = SdkDialogs.SaveSucceeded([
            "~/.claude/settings.json",
            "~/.claude/mcp.json",
            "~/.claude/CLAUDE.md",
        ]);

        List<DialogSegment> pathSegments = msg.Segments.Where(s => s.Kind == DialogSegmentKind.Path).ToList();
        Assert.Equal(3, pathSegments.Count);
        Assert.Equal("~/.claude/settings.json", pathSegments[0].Value);
        Assert.Equal("~/.claude/mcp.json", pathSegments[1].Value);
        Assert.Equal("~/.claude/CLAUDE.md", pathSegments[2].Value);
    }

    [Fact]
    public void SaveFailed_RendersTargetAsPath_AndErrorAsText()
    {
        DialogMessage msg = SdkDialogs.SaveFailed("/etc/locked.json", "Access denied");

        Assert.Contains(msg.Segments, s => s.Kind == DialogSegmentKind.Path
                                            && s.Value == "/etc/locked.json");
        Assert.Contains(msg.Segments, s => s.Kind == DialogSegmentKind.Text
                                            && s.Value.Contains("Access denied"));
    }

    [Fact]
    public void SchemaValidationFailed_WithDocsUrl_AppendsHyperlink()
    {
        DialogMessage msg = SdkDialogs.SchemaValidationFailed(
            "model",
            "must be one of: sonnet, opus, haiku",
            docsUrl: "https://docs.claude.com/schema");

        Assert.Contains(msg.Segments, s => s.Kind == DialogSegmentKind.Bold
                                            && s.Value == "model");
        Assert.Contains(msg.Segments, s => s.Kind == DialogSegmentKind.Hyperlink
                                            && s.Url == "https://docs.claude.com/schema");
    }

    [Fact]
    public void SchemaValidationFailed_WithoutDocsUrl_OmitsHyperlink()
    {
        DialogMessage msg = SdkDialogs.SchemaValidationFailed("model", "must be one of: …");

        Assert.DoesNotContain(msg.Segments, s => s.Kind == DialogSegmentKind.Hyperlink);
    }

    [Fact]
    public void NotInstalled_RendersProductBoldAndDocsHyperlink()
    {
        DialogMessage msg = SdkDialogs.NotInstalled("Claude Desktop", "https://example.com/install");

        Assert.Contains(msg.Segments, s => s.Kind == DialogSegmentKind.Bold
                                            && s.Value == "Claude Desktop");
        Assert.Contains(msg.Segments, s => s.Kind == DialogSegmentKind.Hyperlink
                                            && s.Url == "https://example.com/install");
    }
}