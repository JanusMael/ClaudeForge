using Bennewitz.Ninja.ClaudeForge.Core.Catalog;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Catalog;

/// <summary>
/// Locks the bundled model catalog: that it embeds + loads, and that the
/// inter-relationship queries + nearest-analog coercion behave per the harvested
/// Claude docs (Opus 4.6 / Sonnet 4.6 lack xhigh; max is session-only; Haiku has
/// no effort; auto is gated on capable models).
/// </summary>
[TestClass]
public sealed class ModelCatalogTests
{
    private static ModelCatalog Catalog => ModelCatalogLoader.Load();

    [TestMethod]
    public void Load_EmbedsBundledCatalog()
    {
        ModelCatalog c = Catalog;
        Assert.IsTrue(c.Models.Count >= 6, "Bundled catalog must embed and parse.");
        CollectionAssert.Contains(c.Models.Select(m => m.Id).ToList(), "claude-opus-4-8");
        CollectionAssert.Contains(c.Models.Select(m => m.Id).ToList(), "claude-sonnet-4-6");
    }

    [TestMethod]
    public void Resolve_StripsContextSuffix_AndUsesAliases()
    {
        Assert.AreEqual("claude-opus-4-8", Catalog.Resolve("opus[1m]")?.Id);
        Assert.AreEqual("claude-opus-4-8", Catalog.Resolve("opus")?.Id);
        Assert.AreEqual("claude-sonnet-4-6", Catalog.Resolve("claude-sonnet-4-6[1m]")?.Id);
        Assert.IsNull(Catalog.Resolve("some-custom-model"), "Unknown id resolves to null.");
        Assert.IsNull(Catalog.Resolve(null));
    }

    [TestMethod]
    public void IsEffortSupported_ReflectsPerModelCapability()
    {
        Assert.IsFalse(Catalog.IsEffortSupported("claude-sonnet-4-6", "xhigh"), "Sonnet 4.6 lacks xhigh.");
        Assert.IsFalse(Catalog.IsEffortSupported("claude-opus-4-6", "xhigh"), "Opus 4.6 lacks xhigh.");
        Assert.IsTrue(Catalog.IsEffortSupported("claude-opus-4-8", "xhigh"), "Opus 4.8 supports xhigh.");
    }

    [TestMethod]
    public void PersistableEffortLevels_OmitsSessionOnlyMax()
    {
        Assert.IsFalse(Catalog.EffortLevels.Single(e => e.Id == "max").Persists, "max is session-only.");
        CollectionAssert.DoesNotContain(Catalog.PersistableEffortLevels("claude-opus-4-8").ToList(), "max");
        // Opus 4.8 supports low/medium/high/xhigh/max; persistable = all but max.
        CollectionAssert.AreEquivalent(
            new[] { "low", "medium", "high", "xhigh" },
            Catalog.PersistableEffortLevels("claude-opus-4-8").ToList());
    }

    [TestMethod]
    public void NearestAnalogEffort_CoercesToClosestSupported()
    {
        // Sonnet 4.6 persistable = low/medium/high; xhigh and max both fall to high.
        Assert.AreEqual("high", Catalog.NearestAnalogEffort("claude-sonnet-4-6", "xhigh"));
        Assert.AreEqual("high", Catalog.NearestAnalogEffort("claude-sonnet-4-6", "max"));
        // Valid value is returned unchanged.
        Assert.AreEqual("xhigh", Catalog.NearestAnalogEffort("claude-opus-4-8", "xhigh"));
        Assert.AreEqual("low", Catalog.NearestAnalogEffort("claude-opus-4-8", "low"));
    }

    [TestMethod]
    public void NearestAnalogEffort_ReturnsNull_WhenModelHasNoEffort()
    {
        Assert.AreEqual(0, Catalog.SupportedEffortLevels("claude-haiku-4-5").Count, "Haiku exposes no effort.");
        Assert.IsNull(Catalog.NearestAnalogEffort("claude-haiku-4-5", "high"));
    }

    [TestMethod]
    public void UnknownModel_IsLenient_AllEffortAllowed_NoAuto()
    {
        // A hand-typed custom id must not blank the effort dropdown, and must not claim auto support.
        Assert.IsTrue(Catalog.SupportedEffortLevels("my/custom-model").Count >= 4);
        Assert.IsFalse(Catalog.SupportsAutoMode("my/custom-model"));
    }

    [TestMethod]
    public void SupportsAutoMode_GatedByModel()
    {
        Assert.IsTrue(Catalog.SupportsAutoMode("claude-opus-4-8"));
        Assert.IsFalse(Catalog.SupportsAutoMode("claude-haiku-4-5"), "Haiku does not support auto.");
    }

    [TestMethod]
    public void Parse_EmptyObject_YieldsEmptyCatalog()
    {
        ModelCatalog c = ModelCatalogLoader.Parse("{}");
        Assert.AreEqual(0, c.Models.Count);
        // Empty catalog stays lenient and never throws.
        Assert.IsFalse(c.SupportsAutoMode("opus"));
    }

    [TestMethod]
    public void Resolve_SecondaryAliasOnlyInAliasMap_IsCaseInsensitive()
    {
        // A key present ONLY in the alias map (not any model's primary Alias field)
        // exercises the Aliases.TryGetValue fallback — the bundled catalog can't,
        // since its alias-map keys duplicate the models' primary aliases and are
        // caught first by the direct match. The fallback must be case-insensitive
        // like the [1m] strip and the direct id/alias match.
        ModelCatalog c = ModelCatalogLoader.Parse(
            """
            {
              "models": [ { "id": "claude-opus-4-8", "alias": "opus", "label": "Opus 4.8" } ],
              "aliases": { "opus-latest": "claude-opus-4-8" }
            }
            """);

        Assert.AreEqual("claude-opus-4-8", c.Resolve("opus-latest")?.Id, "Secondary alias resolves via the alias-map fallback.");
        Assert.AreEqual("claude-opus-4-8", c.Resolve("OPUS-LATEST")?.Id, "Secondary alias resolves case-insensitively.");
    }

    [TestMethod]
    public void NearestAnalogEffort_OpusMax_CoercesToXhigh()
    {
        // 'max' (order 4, session-only) on Opus 4.8 (persistable low/medium/high/xhigh =
        // orders 0-3) is nearest to xhigh (|4-3|=1) — the ordinal-distance branch, not
        // the default/highest fallback (distinct from Sonnet, which lacks xhigh).
        Assert.AreEqual("xhigh", Catalog.NearestAnalogEffort("claude-opus-4-8", "max"));
    }

    [TestMethod]
    public void NearestAnalogEffort_CustomString_PrefersPersistableModelDefault()
    {
        // An effort id with no known order (custom string) falls back to the model's
        // declared default when that default persists (Opus 4.8 default = 'high').
        Assert.AreEqual("high", Catalog.NearestAnalogEffort("claude-opus-4-8", "wibble"));
    }

    [TestMethod]
    public void NearestAnalogEffort_CustomString_FallsToHighest_WhenDefaultNotPersistable()
    {
        // When the model's declared default is session-only (non-persistable), an
        // unknown custom effort falls through to the highest persistable level.
        ModelCatalog c = ModelCatalogLoader.Parse(
            """
            {
              "models": [ { "id": "m", "alias": "m", "label": "M",
                "supportedEffortLevels": ["low","medium","high","max"], "defaultEffortLevel": "max" } ],
              "effortLevels": [
                { "id": "low", "order": 0, "persists": true },
                { "id": "medium", "order": 1, "persists": true },
                { "id": "high", "order": 2, "persists": true },
                { "id": "max", "order": 4, "persists": false }
              ]
            }
            """);

        Assert.AreEqual("high", c.NearestAnalogEffort("m", "wibble"));
    }

    [TestMethod]
    public void ModelSuggestions_IncludesOneMVariants_ForSupportingModelsOnly()
    {
        List<string> s = Catalog.ModelSuggestions().ToList();
        CollectionAssert.Contains(s, "opus[1m]", "Opus supports 1m → its [1m] variant is offered.");
        CollectionAssert.DoesNotContain(s, "haiku[1m]", "Haiku does not support 1m → no [1m] variant.");
    }

    [TestMethod]
    public void ModelSuggestions_Include1mFalse_OmitsVariants()
    {
        CollectionAssert.DoesNotContain(Catalog.ModelSuggestions(include1m: false).ToList(), "opus[1m]");
    }

    [TestMethod]
    public void ModelSuggestions_IncludeLegacyTrue_AddsLegacyIds()
    {
        CollectionAssert.DoesNotContain(Catalog.ModelSuggestions().ToList(), "claude-opus-4-7", "Legacy id omitted by default.");
        CollectionAssert.Contains(Catalog.ModelSuggestions(includeLegacy: true).ToList(), "claude-opus-4-7", "Legacy id included when requested.");
    }
}
