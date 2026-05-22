using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Logging;
using AvaloniaLevel = Avalonia.Logging.LogEventLevel;
using SerilogLevel = Serilog.Events.LogEventLevel;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Tests;

[TestClass]
public sealed class SerilogAvaloniaSinkTests
{
    // -----------------------------------------------------------------------
    // Level mapping
    // -----------------------------------------------------------------------

    [TestMethod]
    [DataRow(AvaloniaLevel.Verbose, SerilogLevel.Verbose)]
    [DataRow(AvaloniaLevel.Debug, SerilogLevel.Debug)]
    [DataRow(AvaloniaLevel.Information, SerilogLevel.Information)]
    [DataRow(AvaloniaLevel.Warning, SerilogLevel.Warning)]
    [DataRow(AvaloniaLevel.Error, SerilogLevel.Error)]
    [DataRow(AvaloniaLevel.Fatal, SerilogLevel.Fatal)]
    public void Map_ConvertsEveryLevel_Correctly(AvaloniaLevel avalonia, SerilogLevel expected)
    {
        Assert.AreEqual(expected, SerilogAvaloniaSink.Map(avalonia));
    }

    [TestMethod]
    public void Map_UnknownLevel_ReturnInformation()
    {
        AvaloniaLevel unknown = (AvaloniaLevel)999;
        Assert.AreEqual(SerilogLevel.Information, SerilogAvaloniaSink.Map(unknown));
    }

    // -----------------------------------------------------------------------
    // IsEnabled
    // -----------------------------------------------------------------------

    [TestMethod]
    [DataRow(AvaloniaLevel.Verbose)]
    [DataRow(AvaloniaLevel.Debug)]
    [DataRow(AvaloniaLevel.Information)]
    [DataRow(AvaloniaLevel.Warning)]
    [DataRow(AvaloniaLevel.Error)]
    [DataRow(AvaloniaLevel.Fatal)]
    public void IsEnabled_True_ForUnmutedArea_AtAnyLevel(AvaloniaLevel level)
    {
        SerilogAvaloniaSink sink = new();
        Assert.IsTrue(sink.IsEnabled(level, "Binding"));
    }

    [TestMethod]
    public void IsEnabled_True_ForUnmutedAreas_BelowWarning()
    {
        SerilogAvaloniaSink sink = new();
        Assert.IsTrue(sink.IsEnabled(AvaloniaLevel.Debug, string.Empty));
        Assert.IsTrue(sink.IsEnabled(AvaloniaLevel.Debug, "Binding"));
        Assert.IsTrue(sink.IsEnabled(AvaloniaLevel.Information, "Binding"));
    }

    [TestMethod]
    [DataRow("Layout")]
    [DataRow("Property")]
    [DataRow("Visual")]
    public void IsEnabled_False_ForMutedAreas_BelowWarning(string area)
    {
        SerilogAvaloniaSink sink = new();
        Assert.IsFalse(sink.IsEnabled(AvaloniaLevel.Verbose, area));
        Assert.IsFalse(sink.IsEnabled(AvaloniaLevel.Debug, area));
        Assert.IsFalse(sink.IsEnabled(AvaloniaLevel.Information, area));
    }

    [TestMethod]
    [DataRow("Layout")]
    [DataRow("Property")]
    [DataRow("Visual")]
    public void IsEnabled_True_ForMutedAreas_AtWarningAndAbove(string area)
    {
        SerilogAvaloniaSink sink = new();
        Assert.IsTrue(sink.IsEnabled(AvaloniaLevel.Warning, area));
        Assert.IsTrue(sink.IsEnabled(AvaloniaLevel.Error, area));
        Assert.IsTrue(sink.IsEnabled(AvaloniaLevel.Fatal, area));
    }

    [TestMethod]
    public void IsEnabled_CustomMutedAreas_OverrideDefault()
    {
        // Layout is no longer muted — only "MyNoisyArea" is.
        SerilogAvaloniaSink sink = new(["MyNoisyArea"]);

        Assert.IsTrue(sink.IsEnabled(AvaloniaLevel.Debug, "Layout"));
        Assert.IsFalse(sink.IsEnabled(AvaloniaLevel.Debug, "MyNoisyArea"));
        Assert.IsTrue(sink.IsEnabled(AvaloniaLevel.Warning, "MyNoisyArea"));
    }

    [TestMethod]
    public void IsEnabled_EmptyMutedCollection_DisablesMutingEntirely()
    {
        SerilogAvaloniaSink sink = new([]);

        // Every default-muted area is now un-muted.
        Assert.IsTrue(sink.IsEnabled(AvaloniaLevel.Debug, "Layout"));
        Assert.IsTrue(sink.IsEnabled(AvaloniaLevel.Debug, "Property"));
        Assert.IsTrue(sink.IsEnabled(AvaloniaLevel.Debug, "Visual"));
    }
}