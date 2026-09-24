using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Sdk.Diagnostics;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Diagnostics;

/// <summary>
/// Unit tests for <see cref="JsonDiff.Compute"/>.  Exercises the full
/// recursion + array set-diff contract that the App's save-confirmation
/// dialog and audit log depend on.  Migrated from the App test project
/// when the diff core moved into <see cref="Bennewitz.Ninja.AgentForge.Sdk"/>.
/// </summary>
public sealed class JsonDiffTests
{
    [Fact]
    public void DiffJsonObjects_DetectsAddedKey()
    {
        JsonObject baseline = new() { ["model"] = "sonnet" };
        JsonObject current = new() { ["model"] = "sonnet", ["verbose"] = true };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        Assert.Single(diffs);
        Assert.Equal("verbose", diffs[0].Key);
        Assert.Equal(ChangeKind.Added, diffs[0].Kind);
        Assert.Null(diffs[0].OldValue);
        Assert.Equal("true", diffs[0].NewValue);
    }

    [Fact]
    public void DiffJsonObjects_DetectsRemovedKey()
    {
        JsonObject baseline = new() { ["model"] = "sonnet", ["verbose"] = true };
        JsonObject current = new() { ["model"] = "sonnet" };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        Assert.Single(diffs);
        Assert.Equal("verbose", diffs[0].Key);
        Assert.Equal(ChangeKind.Removed, diffs[0].Kind);
        Assert.Equal("true", diffs[0].OldValue);
        Assert.Null(diffs[0].NewValue);
    }

    [Fact]
    public void DiffJsonObjects_DetectsModifiedKey()
    {
        JsonObject baseline = new() { ["model"] = "sonnet" };
        JsonObject current = new() { ["model"] = "opus" };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        Assert.Single(diffs);
        Assert.Equal("model", diffs[0].Key);
        Assert.Equal(ChangeKind.Modified, diffs[0].Kind);
        Assert.Equal("\"sonnet\"", diffs[0].OldValue);
        Assert.Equal("\"opus\"", diffs[0].NewValue);
    }

    [Fact]
    public void DiffJsonObjects_NullBaseline_TreatsAllKeysAsAdded()
    {
        JsonObject current = new() { ["model"] = "sonnet", ["verbose"] = true };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(null, current);

        Assert.Equal(2, diffs.Count);
        Assert.True(diffs.All(d => d.Kind == ChangeKind.Added));
    }

    [Fact]
    public void DiffJsonObjects_DropsToolMetadataKey()
    {
        // The "//" key is the tool-written metadata marker; it always
        // changes (timestamps) but carries no semantic content. The diff
        // helper must drop it so the rolling log isn't spammed.
        JsonObject baseline = new() { ["//"] = "old-stamp", ["model"] = "sonnet" };
        JsonObject current = new() { ["//"] = "new-stamp", ["model"] = "opus" };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        Assert.Single(diffs);
        Assert.Equal("model", diffs[0].Key);
    }

    [Fact]
    public void DiffJsonObjects_NoDifferences_ReturnsEmpty()
    {
        JsonObject baseline = new() { ["model"] = "sonnet" };
        JsonObject current = new() { ["model"] = "sonnet" };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        Assert.Empty(diffs);
    }

    [Fact]
    public void DiffJsonObjects_WithinSameKind_IsAlphabeticalByKey()
    {
        // All three keys are Added (baseline is empty), so the kind-rank tier
        // is constant — the within-kind tiebreaker (alphabetical by key) is
        // what determines the visible order.
        JsonObject baseline = new();
        JsonObject current = new()
        {
            ["zeta"] = 1,
            ["alpha"] = 2,
            ["mid"] = 3,
        };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        Assert.Equal(
            new[] { "alpha", "mid", "zeta" },
            diffs.Select(d => d.Key).ToList());
    }

    [Fact]
    public void DiffJsonObjects_AcrossKinds_OrdersModifiedThenAddedThenRemoved()
    {
        // Save-confirmation dialog ordering contract:
        //   1. Modified rows surface first (highest fat-finger risk)
        //   2. Added next (intentional growth)
        //   3. Removed last (deliberate deletions)
        // Within each kind, keys are ordered alphabetically.
        JsonObject baseline = new()
        {
            ["zRemoved"] = "gone",
            ["aModified"] = "before",
            ["mModified"] = "before2",
            ["bRemoved"] = "gone2",
        };
        JsonObject current = new()
        {
            ["aModified"] = "after",
            ["mModified"] = "after2",
            ["nAdded"] = "new",
            ["cAdded"] = "new2",
        };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        // Expected:
        //   Modified: aModified, mModified  (alphabetical within kind)
        //   Added:    cAdded,    nAdded     (alphabetical within kind)
        //   Removed:  bRemoved,  zRemoved   (alphabetical within kind)
        (string, ChangeKind)[] expected =
        [
            ("aModified", ChangeKind.Modified),
            ("mModified", ChangeKind.Modified),
            ("cAdded", ChangeKind.Added),
            ("nAdded", ChangeKind.Added),
            ("bRemoved", ChangeKind.Removed),
            ("zRemoved", ChangeKind.Removed),
        ];
        (string Key, ChangeKind Kind)[] actual = diffs.Select(d => (d.Key, d.Kind)).ToArray();
        MessageAssert.SequenceEqual(expected, actual,
            "Save-dialog row order must be Modified → Added → Removed, alphabetical " +
            "within each kind. See JsonDiff.Compute remarks.");
    }

    // ── Recursion into nested objects ─────────────────────────────────

    [Fact]
    public void DiffJsonObjects_NewlyAddedObjectKey_RecursesInsteadOfBlobbing()
    {
        // Root cause of the MAX_OUTPUT_TOKENS disappearance bug (2026-05-13):
        // when the baseline has no "env" section and one env-var is written,
        // the old code emitted one "Added env" row carrying the whole object.
        // The fix recurses so the dialog shows "Added env.MAX_OUTPUT_TOKENS".
        JsonObject baseline = new();
        JsonObject current = new()
        {
            ["env"] = new JsonObject { ["MAX_OUTPUT_TOKENS"] = "60000" },
        };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        MessageAssert.Equal(1, diffs.Count,
            "One env-var add must produce exactly one diff row.");
        MessageAssert.Equal("env.MAX_OUTPUT_TOKENS", diffs[0].Key,
            "Key must be the dotted leaf path, not the bare object key.");
        Assert.Equal(ChangeKind.Added, diffs[0].Kind);
        Assert.Equal("\"60000\"", diffs[0].NewValue);
        Assert.Null(diffs[0].OldValue);
    }

    [Fact]
    public void DiffJsonObjects_RemovedObjectKey_RecursesInsteadOfBlobbing()
    {
        // Symmetric to the Added case — removing the env section entirely
        // should surface the individual key, not the whole blob.
        JsonObject baseline = new()
        {
            ["env"] = new JsonObject { ["MAX_OUTPUT_TOKENS"] = "60000" },
        };
        JsonObject current = new();

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        Assert.Single(diffs);
        Assert.Equal("env.MAX_OUTPUT_TOKENS", diffs[0].Key);
        Assert.Equal(ChangeKind.Removed, diffs[0].Kind);
        Assert.Equal("\"60000\"", diffs[0].OldValue);
        Assert.Null(diffs[0].NewValue);
    }

    [Fact]
    public void DiffJsonObjects_NewlyAddedObjectWithMultipleKeys_EmitsOneRowPerLeaf()
    {
        // Multiple env-vars written at once → one row per var, all Added.
        JsonObject baseline = new();
        JsonObject current = new()
        {
            ["env"] = new JsonObject
            {
                ["MAX_OUTPUT_TOKENS"] = "60000",
                ["MAX_THINKING_TOKENS"] = "10000",
            },
        };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        Assert.Equal(2, diffs.Count);
        Assert.True(diffs.All(d => d.Kind == ChangeKind.Added));
        Assert.Contains(diffs, d => d.Key == "env.MAX_OUTPUT_TOKENS");
        Assert.Contains(diffs, d => d.Key == "env.MAX_THINKING_TOKENS");
    }

    [Fact]
    public void DiffJsonObjects_NestedObjectChange_ReportsLeafPathOnly()
    {
        // The user's bug: changing one element inside `hooks.Stop` used to emit
        // the entire `hooks` object as one Modified row. Recursive diff must
        // surface the change at the nested key path, not the parent.
        JsonObject baseline = new()
        {
            ["hooks"] = new JsonObject
            {
                ["Stop"] = new JsonArray("entry1", "entry2"),
                ["PreToolUse"] = new JsonArray("untouched"),
            },
        };
        JsonObject current = new()
        {
            ["hooks"] = new JsonObject
            {
                ["Stop"] = new JsonArray("entry1"), // entry2 removed
                ["PreToolUse"] = new JsonArray("untouched"),
            },
        };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        MessageAssert.Equal(1, diffs.Count,
            "Removing one entry from hooks.Stop must produce exactly one diff row.");
        Assert.Equal("hooks.Stop", diffs[0].Key);
        Assert.Equal(ChangeKind.Removed, diffs[0].Kind);
        Assert.Equal("\"entry2\"", diffs[0].OldValue);
        Assert.False(diffs.Any(d => d.Key == "hooks"),
            "Top-level 'hooks' must NOT appear — recursion drilled in.");
    }

    [Fact]
    public void DiffJsonObjects_DeeplyNestedObject_PathReflectsFullJsonPath()
    {
        JsonObject baseline = new()
        {
            ["a"] = new JsonObject
            {
                ["b"] = new JsonObject
                {
                    ["c"] = "old",
                },
            },
        };
        JsonObject current = new()
        {
            ["a"] = new JsonObject
            {
                ["b"] = new JsonObject
                {
                    ["c"] = "new",
                },
            },
        };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        Assert.Single(diffs);
        Assert.Equal("a.b.c", diffs[0].Key);
        Assert.Equal(ChangeKind.Modified, diffs[0].Kind);
    }

    // ── Array set-diff ────────────────────────────────────────────────

    [Fact]
    public void DiffJsonObjects_ArrayElementRemoved_ReportsOneRemovedRowAtArrayPath()
    {
        JsonObject baseline = new() { ["allow"] = new JsonArray("Bash", "Edit", "Read") };
        JsonObject current = new() { ["allow"] = new JsonArray("Bash", "Read") };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        Assert.Single(diffs);
        Assert.Equal("allow", diffs[0].Key);
        Assert.Equal(ChangeKind.Removed, diffs[0].Kind);
        Assert.Equal("\"Edit\"", diffs[0].OldValue);
        Assert.Null(diffs[0].NewValue);
    }

    [Fact]
    public void DiffJsonObjects_ArrayElementAdded_ReportsOneAddedRowAtArrayPath()
    {
        JsonObject baseline = new() { ["allow"] = new JsonArray("Bash") };
        JsonObject current = new() { ["allow"] = new JsonArray("Bash", "Edit") };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        Assert.Single(diffs);
        Assert.Equal(ChangeKind.Added, diffs[0].Kind);
        Assert.Equal("\"Edit\"", diffs[0].NewValue);
    }

    [Fact]
    public void DiffJsonObjects_ArrayMixedAddRemove_ReportsBothRowsSeparately()
    {
        JsonObject baseline = new() { ["allow"] = new JsonArray("Bash", "Edit") };
        JsonObject current = new() { ["allow"] = new JsonArray("Bash", "Read") };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        Assert.Equal(2, diffs.Count);
        Assert.Contains(diffs, d => d.Kind == ChangeKind.Added && d.NewValue == "\"Read\"");
        Assert.Contains(diffs, d => d.Kind == ChangeKind.Removed && d.OldValue == "\"Edit\"");
    }

    [Fact]
    public void DiffJsonObjects_ArrayReorderOnly_FallsThroughToModifiedRow()
    {
        // Same multiset, different sequence — element-set diff is empty but
        // the array isn't structurally identical, so we emit a single
        // Modified row at the array path so the reorder is still surfaced.
        JsonObject baseline = new() { ["allow"] = new JsonArray("a", "b", "c") };
        JsonObject current = new() { ["allow"] = new JsonArray("c", "b", "a") };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        Assert.Single(diffs);
        Assert.Equal(ChangeKind.Modified, diffs[0].Kind);
        Assert.Equal("allow", diffs[0].Key);
    }

    [Fact]
    public void DiffJsonObjects_ArrayOfObjects_DiffsByJsonStringIdentity()
    {
        // Realistic case: Permissions array of rule objects.  Removing
        // one rule must yield exactly one Removed row carrying just that
        // rule's JSON, not the entire rule list.
        JsonObject baseline = new()
        {
            ["permissions"] = new JsonObject
            {
                ["allow"] = new JsonArray(
                    new JsonObject { ["tool"] = "Bash", ["pattern"] = "*" },
                    new JsonObject { ["tool"] = "Edit", ["pattern"] = "*.cs" }),
            },
        };
        JsonObject current = new()
        {
            ["permissions"] = new JsonObject
            {
                ["allow"] = new JsonArray(
                    new JsonObject { ["tool"] = "Bash", ["pattern"] = "*" }),
            },
        };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        Assert.Single(diffs);
        Assert.Equal("permissions.allow", diffs[0].Key);
        Assert.Equal(ChangeKind.Removed, diffs[0].Kind);
        OrdinalAssert.Contains("\"Edit\"", diffs[0].OldValue);
        OrdinalAssert.Contains("\"*.cs\"", diffs[0].OldValue);
    }

    // ── Type mismatch and metadata key ────────────────────────────────

    [Fact]
    public void DiffJsonObjects_TypeMismatch_FallsThroughToModified()
    {
        // String → array at the same key: can't recurse, can't set-diff.
        // Falls through to the legacy single-Modified row.
        JsonObject baseline = new() { ["x"] = "hello" };
        JsonObject current = new() { ["x"] = new JsonArray(1, 2) };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        Assert.Single(diffs);
        Assert.Equal(ChangeKind.Modified, diffs[0].Kind);
    }

    [Fact]
    public void DiffJsonObjects_NestedMetadataKey_IsAlsoStripped()
    {
        // The "//" tool-metadata stripper must apply at every recursion
        // level, not just the top — nested objects can carry their own
        // commentary that we don't want flooding the dialog.
        JsonObject baseline = new()
        {
            ["hooks"] = new JsonObject
            {
                ["//"] = "stamp-old",
                ["Stop"] = new JsonArray("a"),
            },
        };
        JsonObject current = new()
        {
            ["hooks"] = new JsonObject
            {
                ["//"] = "stamp-new",
                ["Stop"] = new JsonArray("a"),
            },
        };

        IReadOnlyList<PropertyDiff> diffs = JsonDiff.Compute(baseline, current);

        MessageAssert.Equal(0, diffs.Count,
            "A nested '//' metadata-only change must not produce any diffs.");
    }
}