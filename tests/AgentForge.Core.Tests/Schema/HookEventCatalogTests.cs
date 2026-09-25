using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// Locks <see cref="HookEventCatalog"/>: the schema (fresh) drives the event
/// list, the curated overlay only orders it + seeds an offline fallback, and
/// unrecognized-event detection stays forgiving when the schema set is unknown.
/// </summary>
public sealed class HookEventCatalogTests
{
    [Fact]
    public void ResolveOrder_NullOrEmpty_FallsBackToCuratedOrder()
    {
        IReadOnlyCollection<string>? nullNames = null;
        Assert.Equal(HookEventCatalog.CuratedOrder.ToList(),
            HookEventCatalog.ResolveOrder(nullNames).ToList());
        Assert.Equal(HookEventCatalog.CuratedOrder.ToList(),
            HookEventCatalog.ResolveOrder(Array.Empty<string>()).ToList());
    }

    [Fact]
    public void ResolveOrder_OrdersCuratedFirst_ThenSchemaExtras()
    {
        // Schema (arbitrary order) with two curated events + one we don't curate.
        string[] schema = ["Stop", "ZebraEvent", "PreToolUse"];
        List<string> ordered = HookEventCatalog.ResolveOrder(schema).ToList();

        // Curated ones come in curated order (PreToolUse before Stop); extra last.
        Assert.Equal("PreToolUse", ordered[0]);
        Assert.Equal("Stop", ordered[1]);
        Assert.Equal("ZebraEvent", ordered[2]);
    }

    [Fact]
    public void ResolveOrder_DropsCuratedEventsAbsentFromSchema()
    {
        // Schema omits everything except PreToolUse → only PreToolUse is offered
        // (a curated entry the schema dropped must NOT be shown proactively).
        List<string> ordered = HookEventCatalog.ResolveOrder(["PreToolUse"]).ToList();
        Assert.Equal(new[] { "PreToolUse" }, ordered);
    }

    [Fact]
    public void UnrecognizedEvents_UnknownSchema_ReturnsEmpty()
    {
        Assert.Empty(HookEventCatalog.UnrecognizedEvents(["Whatever"], null));
        Assert.Empty(HookEventCatalog.UnrecognizedEvents(["Whatever"], []));
    }

    [Fact]
    public void UnrecognizedEvents_ReturnsCandidatesNotInSchema_Distinct()
    {
        string[] schema = ["PreToolUse", "Stop"];
        string[] candidates = ["PreToolUse", "Deprecated1", "Deprecated1", "Deprecated2"];
        List<string> unknown = HookEventCatalog.UnrecognizedEvents(candidates, schema).ToList();
        Assert.Equal(new[] { "Deprecated1", "Deprecated2" }, unknown);
    }

    [Fact]
    public void CuratedOrder_HasNoDuplicates()
    {
        Assert.Equal(HookEventCatalog.CuratedOrder.Count,
            HookEventCatalog.CuratedOrder.Distinct().Count());
    }
}
