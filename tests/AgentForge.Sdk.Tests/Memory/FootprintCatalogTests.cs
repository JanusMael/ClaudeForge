using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Memory;

/// <summary>
/// Guards for the enum→product-data conversion of <see cref="FootprintCategory"/>.
/// </summary>
/// <remarks>
/// ⛔ <b>These pin a compatibility surface, not an implementation.</b> The conversion is only safe
/// because <c>default(FootprintCategory)</c> still equals <see cref="FootprintCategory.SessionTranscripts"/>,
/// the statics still equal the categories a service hands out, the order is still the render
/// order, and <c>ToString()</c> still returns the former enum member names. Each of those failing
/// is silent — a wrong label, a row in the wrong place, a mismatched comparison — so each gets a
/// test that names what breaks.
/// </remarks>
[TestClass]
public sealed class FootprintCatalogTests
{
    // ── The null-encoding invariant, the one ConfigScope measured ───────────

    [TestMethod]
    public void DefaultCategory_IsStillTheFirstCategory()
    {
        // ⛔ The whole reason the catalog is stored as a NULLABLE field. A struct whose fields are
        // reference types has an all-zero default; if the catalog were non-null, default(...)
        // would carry a null catalog and throw on first use instead of being SessionTranscripts.
        // ConfigScope measured the cost of getting this wrong: 2,791 of 2,792 tests still passed
        // while default(ConfigScope) quietly stopped being Managed.
        Assert.AreEqual(FootprintCategory.SessionTranscripts, default(FootprintCategory));
    }

    [TestMethod]
    public void CategoriesFromTheDefaultCatalog_EqualTheStatics()
    {
        // Without the null normalisation in CategoryAt, a category handed out by a service holding
        // an explicit FootprintCatalog.Default would compare UNEQUAL to the static of the same
        // name — and every call site naming a static would look independently broken.
        Assert.AreEqual(FootprintCategory.Todos, FootprintCatalog.Default.CategoryAt(5));
        Assert.AreEqual(FootprintCategory.SessionMetadata, FootprintCatalog.Default.CategoryAt(1));
    }

    [TestMethod]
    public void ACategoryFromAnotherCatalog_IsNotEqualToADefaultOne()
    {
        // The other half of the same invariant: two catalogs' ordinal-0 categories are different
        // things, and equality must say so. Otherwise a product's own category would silently
        // match Claude's SessionTranscripts and delete the wrong files.
        FootprintCatalog other = new(
        [
            new FootprintCategoryDefinition(
                Id: "session-transcripts",
                Sources: [FootprintSource.Directory("data", "sessions")],
                Anchor: FootprintSource.Directory("data", "sessions"),
                IsInStandardBackup: false),
        ]);

        Assert.AreNotEqual(FootprintCategory.SessionTranscripts, other.CategoryAt(0),
            "Same ordinal and same id, different catalog — these are not the same category.");
    }

    // ── The order and the names the UI depends on ──────────────────────────

    [TestMethod]
    public void DefaultCatalog_KeepsTheFormerEnumsOrderAndNames()
    {
        // ⛔ Declaration order was load-bearing for the enum: the footprint table renders in it,
        // because the old code iterated Enum.GetValues which returns declaration order.
        string[] expected =
        [
            "SessionTranscripts",
            "SessionMetadata",
            "PromptHistory",
            "BashCommandLog",
            "CostTrackerLog",
            "Todos",
            "FileEditHistory",
        ];

        CollectionAssert.AreEqual(
            expected,
            FootprintCategory.All.Select(c => c.ToString()).ToArray(),
            "ToString() is consumed as data — these are the former enum member names, in order.");
    }

    [TestMethod]
    public void DefaultCatalog_IdsAreStable()
    {
        // The ids key the app's resx label lookup. Renaming one silently drops a category's label
        // to its ToString() fallback, which looks like a translation gap rather than a rename.
        string[] expected =
        [
            "session-transcripts",
            "session-metadata",
            "prompt-history",
            "bash-command-log",
            "cost-tracker-log",
            "todos",
            "file-edit-history",
        ];

        CollectionAssert.AreEqual(expected, FootprintCategory.All.Select(c => c.Id).ToArray());
    }

    // ── What the categories actually cover ─────────────────────────────────

    [TestMethod]
    public void SessionMetadata_CoversThreeDirectories_ButRevealsTheirParent()
    {
        // ⚠ The anchor is deliberately NOT Sources[0]: a reveal must show all three siblings at
        // once, so it points at ~/.claude rather than at ~/.claude/sessions.
        FootprintCategoryDefinition definition = FootprintCategory.SessionMetadata.Definition;
        FootprintRoots roots = FootprintRoots.Single(FootprintRoots.Home, Path.Combine("/profile", ".claude"));

        Assert.AreEqual(3, definition.Sources.Count);
        CollectionAssert.AreEquivalent(
            new[] { "sessions", "session-data", "session-env" },
            definition.Sources.Select(s => s.RelativePath).ToArray());

        Assert.AreEqual(
            Path.Combine("/profile", ".claude"),
            definition.Anchor.Resolve(roots),
            "SessionMetadata reveals the parent so all three siblings are visible.");
    }

    [TestMethod]
    public void OnlySessionTranscripts_IsExcludedFromTheStandardBackup()
    {
        // Mirrors BackupEngine.ShouldSkipHomeSubdir: ~/.claude/projects is the one subdirectory
        // Standard mode skips. If a skip rule is added there, this test is the tripwire.
        FootprintCategory[] excluded =
            [.. FootprintCategory.All.Where(c => !c.IsInStandardBackup)];

        CollectionAssert.AreEqual(new[] { FootprintCategory.SessionTranscripts }, excluded);
    }

    // ── Multi-root resolution, the reason the indirection exists ───────────

    [TestMethod]
    public void ACategoryCanSpanRootsAProductSupplies()
    {
        // The shape OpenCode needs: its footprint spans four unrelated XDG roots, and the largest
        // item on disk sits under a different root from the only irreplaceable one.
        FootprintRoots roots = new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["config"] = Path.Combine("/home", "u", ".config", "opencode"),
            ["cache"] = Path.Combine("/home", "u", ".cache", "opencode"),
        });

        Assert.AreEqual(
            Path.Combine("/home", "u", ".config", "opencode", "node_modules"),
            FootprintSource.Directory("config", "node_modules").Resolve(roots));

        Assert.AreEqual(
            Path.Combine("/home", "u", ".cache", "opencode", "models.json"),
            FootprintSource.File("cache", "models.json").Resolve(roots));
    }

    [TestMethod]
    public void ASourceNamingAnUnsuppliedRoot_ResolvesToNull_RatherThanThrowing()
    {
        // ⚠ Null, not throw. GetStatsAsync walks every category in one pass, so a throw here would
        // take out the whole page over one mis-keyed catalog entry.
        FootprintRoots roots = FootprintRoots.Single(FootprintRoots.Home, "/profile/.claude");

        Assert.IsNull(FootprintSource.Directory("state", "locks").Resolve(roots));
    }

    [TestMethod]
    public void RelativePathsUsesForwardSlashes_AndLandOnThePlatformSeparator()
    {
        // Catalog entries read the same on every platform; Path.Combine puts them on the
        // platform's own separator. Never concatenate with a literal separator.
        FootprintRoots roots = FootprintRoots.Single(FootprintRoots.Home, Path.Combine("/profile", ".claude"));

        Assert.AreEqual(
            Path.Combine("/profile", ".claude", "a", "b", "c"),
            FootprintSource.Directory(FootprintRoots.Home, "a/b/c").Resolve(roots));
    }

    [TestMethod]
    public void AnEmptyCatalog_IsRejectedAtConstruction()
    {
        Assert.ThrowsExactly<ArgumentException>(() => _ = new FootprintCatalog([]));
    }

    [TestMethod]
    public void TryGetById_FindsACategory_AndReturnsNullForAnUnknownId()
    {
        Assert.AreEqual(FootprintCategory.Todos, FootprintCatalog.Default.TryGetById("todos"));
        Assert.IsNull(FootprintCatalog.Default.TryGetById("Todos"), "Ids are ordinal machine keys.");
        Assert.IsNull(FootprintCatalog.Default.TryGetById("no-such-category"));
    }
}
