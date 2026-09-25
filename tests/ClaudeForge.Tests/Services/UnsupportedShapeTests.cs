using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.ClaudeForge.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Services;

/// <summary>
/// Phase 2 of the schema-coverage guarantee: a property the editor factory
/// cannot classify is surfaced via the validated raw-JSON editor AND reported
/// so the host can raise one aggregated, non-fatal load-time notice. These tests
/// lock the collector's dedupe/order contract and the factory's report+notice
/// wiring (and that a CLASSIFIED shape is neither flagged nor reported).
/// </summary>
public class UnsupportedShapeTests
{
    // ── UnsupportedShapeCollector ───────────────────────────────────────────────

    [Fact]
    public void Collector_DedupesByPath_FirstDisplayNameWins_AndSnapshotIsPathOrdered()
    {
        UnsupportedShapeCollector collector = new();
        collector.Report("b.path", "B");
        collector.Report("a.path", "A");
        collector.Report("a.path", "A-again"); // duplicate path → ignored

        IReadOnlyList<UnsupportedShape> snap = collector.Snapshot();
        MessageAssert.Equal(2, snap.Count, "Duplicate paths must be collapsed.");
        MessageAssert.Equal("a.path", snap[0].JsonPath, "Snapshot must be path-ordered.");
        MessageAssert.Equal("A", snap[0].DisplayName, "First display name wins on a duplicate path.");
        Assert.Equal("b.path", snap[1].JsonPath);
    }

    [Fact]
    public void Collector_HasAny_TracksReports()
    {
        UnsupportedShapeCollector collector = new();
        Assert.False(collector.HasAny, "Empty collector must report HasAny == false.");
        collector.Report("x", null);
        Assert.True(collector.HasAny);
    }

    // ── DefaultEditorFactory report + per-field notice ──────────────────────────

    [Fact]
    public void Factory_UnclassifiableShape_ReportsToSink_AndTagsEditorWithNotice()
    {
        UnsupportedShapeCollector collector = new();
        DefaultEditorFactory factory = ClaudeEditorFactoryConfig.CreateDefault();
        factory.UnsupportedShapeSink = collector;

        // A Complex node with no properties + an unrecognised name → the factory
        // has no structured editor for it → raw-JSON fallback.
        SchemaNode weird = new SchemaNode("foo.weird", "weird") { ValueType = SchemaValueType.Complex };
        var editor = factory.Create(weird, ConfigScope.User);

        MessageAssert.Equal("JsonRawPropertyEditorViewModel", editor.GetType().Name,
            "An unclassifiable shape must fall back to the validated raw-JSON editor.");
        Assert.False(string.IsNullOrEmpty(editor.UnsupportedShapeNotice),
            "The raw-fallback editor must carry the per-field unsupported-shape notice (drives the warning badge).");
        Assert.True(collector.HasAny, "The factory must report the unsupported shape to the sink.");
        Assert.Equal("foo.weird", collector.Snapshot().Single().JsonPath);
    }

    [Fact]
    public void Factory_ClassifiableShape_DoesNotReport_AndHasNoNotice()
    {
        UnsupportedShapeCollector collector = new();
        DefaultEditorFactory factory = ClaudeEditorFactoryConfig.CreateDefault();
        factory.UnsupportedShapeSink = collector;

        SchemaNode enumNode = new SchemaNode("foo.mode", "mode")
        {
            ValueType = SchemaValueType.Enum,
            EnumValues = ["a", "b"],
        };
        var editor = factory.Create(enumNode, ConfigScope.User);

        Assert.Equal("EnumPropertyEditorViewModel", editor.GetType().Name);
        MessageAssert.Null(editor.UnsupportedShapeNotice, "A classified shape must not carry the unsupported-shape notice.");
        Assert.False(collector.HasAny, "A classified shape must not be reported as unsupported.");
    }

    [Fact]
    public void Factory_NoSink_StillTagsNotice_DoesNotThrow()
    {
        // The notice is intrinsic to the raw fallback; the sink is optional.
        DefaultEditorFactory factory = ClaudeEditorFactoryConfig.CreateDefault();
        SchemaNode weird = new SchemaNode("foo.weird", "weird") { ValueType = SchemaValueType.Complex };

        var editor = factory.Create(weird, ConfigScope.User);

        Assert.Equal("JsonRawPropertyEditorViewModel", editor.GetType().Name);
        Assert.False(string.IsNullOrEmpty(editor.UnsupportedShapeNotice));
    }
}
