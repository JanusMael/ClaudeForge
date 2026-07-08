using Bennewitz.Ninja.ClaudeForge.Core.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

/// <summary>
/// Locks <see cref="HookEventCatalog"/>: the schema (fresh) drives the event
/// list, the curated overlay only orders it + seeds an offline fallback, and
/// unrecognized-event detection stays forgiving when the schema set is unknown.
/// </summary>
[TestClass]
public sealed class HookEventCatalogTests
{
    [TestMethod]
    public void ResolveOrder_NullOrEmpty_FallsBackToCuratedOrder()
    {
        IReadOnlyCollection<string>? nullNames = null;
        CollectionAssert.AreEqual(HookEventCatalog.CuratedOrder.ToList(),
            HookEventCatalog.ResolveOrder(nullNames).ToList());
        CollectionAssert.AreEqual(HookEventCatalog.CuratedOrder.ToList(),
            HookEventCatalog.ResolveOrder(Array.Empty<string>()).ToList());
    }

    [TestMethod]
    public void ResolveOrder_OrdersCuratedFirst_ThenSchemaExtras()
    {
        // Schema (arbitrary order) with two curated events + one we don't curate.
        string[] schema = ["Stop", "ZebraEvent", "PreToolUse"];
        List<string> ordered = HookEventCatalog.ResolveOrder(schema).ToList();

        // Curated ones come in curated order (PreToolUse before Stop); extra last.
        Assert.AreEqual("PreToolUse", ordered[0]);
        Assert.AreEqual("Stop", ordered[1]);
        Assert.AreEqual("ZebraEvent", ordered[2]);
    }

    [TestMethod]
    public void ResolveOrder_DropsCuratedEventsAbsentFromSchema()
    {
        // Schema omits everything except PreToolUse → only PreToolUse is offered
        // (a curated entry the schema dropped must NOT be shown proactively).
        List<string> ordered = HookEventCatalog.ResolveOrder(["PreToolUse"]).ToList();
        CollectionAssert.AreEqual(new[] { "PreToolUse" }, ordered);
    }

    [TestMethod]
    public void UnrecognizedEvents_UnknownSchema_ReturnsEmpty()
    {
        Assert.AreEqual(0, HookEventCatalog.UnrecognizedEvents(["Whatever"], null).Count);
        Assert.AreEqual(0, HookEventCatalog.UnrecognizedEvents(["Whatever"], []).Count);
    }

    [TestMethod]
    public void UnrecognizedEvents_ReturnsCandidatesNotInSchema_Distinct()
    {
        string[] schema = ["PreToolUse", "Stop"];
        string[] candidates = ["PreToolUse", "Deprecated1", "Deprecated1", "Deprecated2"];
        List<string> unknown = HookEventCatalog.UnrecognizedEvents(candidates, schema).ToList();
        CollectionAssert.AreEqual(new[] { "Deprecated1", "Deprecated2" }, unknown);
    }

    [TestMethod]
    public void CuratedOrder_HasNoDuplicates()
    {
        Assert.AreEqual(HookEventCatalog.CuratedOrder.Count,
            HookEventCatalog.CuratedOrder.Distinct().Count());
    }
}
