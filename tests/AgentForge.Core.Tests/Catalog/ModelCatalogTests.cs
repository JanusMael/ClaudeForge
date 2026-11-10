using Bennewitz.Ninja.AgentForge.Core.Catalog;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Catalog;

/// <summary>
/// Locks the bundled model catalog: that it embeds + loads, and that the
/// inter-relationship queries + nearest-analog coercion behave per the harvested
/// Claude docs (Opus 4.6 / Sonnet 4.6 lack xhigh; max is session-only; Haiku has
/// no effort; auto is gated on capable models).
/// </summary>
public sealed class ModelCatalogTests
{
    private static ModelCatalog Catalog => ModelCatalogLoader.Load();

    [Fact]
    public void Load_EmbedsBundledCatalog()
    {
        ModelCatalog c = Catalog;
        Assert.True(c.Models.Count >= 6, "Bundled catalog must embed and parse.");
        Assert.Contains("claude-opus-4-8", c.Models.Select(m => m.Id).ToList());
        Assert.Contains("claude-sonnet-4-6", c.Models.Select(m => m.Id).ToList());
    }

    [Fact]
    public void Resolve_StripsContextSuffix_AndUsesAliases()
    {
        Assert.Equal("claude-opus-5-5", Catalog.Resolve("opus[1m]")?.Id);
        Assert.Equal("claude-opus-5-5", Catalog.Resolve("opus")?.Id);
        Assert.Equal("claude-fable-5-2", Catalog.Resolve("fable")?.Id);
        Assert.Equal("claude-sonnet-5-5", Catalog.Resolve("sonnet")?.Id);
        Assert.Equal("claude-haiku-5-5", Catalog.Resolve("haiku")?.Id);
        Assert.Equal("claude-sonnet-4-6", Catalog.Resolve("claude-sonnet-4-6[1m]")?.Id);
        MessageAssert.Null(Catalog.Resolve("some-custom-model"), "Unknown id resolves to null.");
        Assert.Null(Catalog.Resolve(null));
    }

    [Fact]
    public void IsEffortSupported_ReflectsPerModelCapability()
    {
        Assert.False(Catalog.IsEffortSupported("claude-sonnet-4-6", "xhigh"), "Sonnet 4.6 lacks xhigh.");
        Assert.False(Catalog.IsEffortSupported("claude-opus-4-6", "xhigh"), "Opus 4.6 lacks xhigh.");
        Assert.True(Catalog.IsEffortSupported("claude-opus-4-8", "xhigh"), "Opus 4.8 supports xhigh.");
    }

    [Fact]
    public void PersistableEffortLevels_OmitsSessionOnlyMax()
    {
        Assert.False(Catalog.EffortLevels.Single(e => e.Id == "max").Persists, "max is session-only.");
        Assert.DoesNotContain("max", Catalog.PersistableEffortLevels("claude-opus-4-8").ToList());
        // Opus 4.8 supports low/medium/high/xhigh/max; persistable = all but max.
        MessageAssert.SameElements(
            new[] { "low", "medium", "high", "xhigh" },
            Catalog.PersistableEffortLevels("claude-opus-4-8").ToList());
    }

    /// <summary>
    /// The effort axis is exactly Claude Code's: <c>low</c> to <c>max</c>, with <c>max</c> the
    /// top and the only session-only level, and no model may name a level outside it.
    /// </summary>
    /// <remarks>
    /// ⓘ <b>Corrected 2026-11-10 (plans/00010).</b> This was
    /// <c>SessionOnlyEffortLevels_MirrorFullRange_ButStayNonPersistable</c>, which pinned
    /// <c>ultracode</c> as a sixth level "above max on the Faster→Smarter axis", to mirror a
    /// Claude Desktop slider. Ultracode is not an effort level. Claude Code's settings schema has
    /// no enum containing it: it is the prompt keyword that starts a dynamic workflow
    /// (<c>workflowKeywordTriggerEnabled</c>), it "reports as xhigh", and Desktop now shows it as
    /// a toggle beside the effort list. The catalog's own JSON schema no longer accepts it, so
    /// <c>ModelCatalogSchemaParityTests</c> also fails on a catalog that re-adds it.
    /// </remarks>
    [Fact]
    public void MaxIsTheTopEffortLevel_AndTheOnlySessionOnlyOne()
    {
        string[] axis = ["low", "medium", "high", "xhigh", "max"];

        MessageAssert.Equal(
            string.Join(",", axis),
            string.Join(",", Catalog.EffortLevels.Select(e => e.Id)),
            "The effort levels, ordered Faster→Smarter, must be exactly Claude Code's.");

        MessageAssert.SameElements(
            new[] { "max" },
            Catalog.EffortLevels.Where(e => !e.Persists).Select(e => e.Id).ToArray(),
            "max must be the only session-only effort level.");

        // Premise before claim: a catalog with no models would pass the loop below vacuously.
        Assert.NotEmpty(Catalog.Models);
        foreach (ModelInfo model in Catalog.Models)
        {
            string[] outside = Catalog.SupportedEffortLevels(model.Id).Except(axis).ToArray();
            Assert.True(
                outside.Length == 0,
                $"'{model.Id}' names effort levels outside Claude Code's axis: {string.Join(", ", outside)}.");
        }
    }

    [Fact]
    public void NearestAnalogEffort_CoercesToClosestSupported()
    {
        // Sonnet 4.6 persistable = low/medium/high; xhigh and max both fall to high.
        Assert.Equal("high", Catalog.NearestAnalogEffort("claude-sonnet-4-6", "xhigh"));
        Assert.Equal("high", Catalog.NearestAnalogEffort("claude-sonnet-4-6", "max"));
        // Valid value is returned unchanged.
        Assert.Equal("xhigh", Catalog.NearestAnalogEffort("claude-opus-4-8", "xhigh"));
        Assert.Equal("low", Catalog.NearestAnalogEffort("claude-opus-4-8", "low"));
    }

    [Fact]
    public void NearestAnalogEffort_ReturnsNull_WhenModelHasNoEffort()
    {
        MessageAssert.Equal(0, Catalog.SupportedEffortLevels("claude-haiku-4-5").Count, "Haiku exposes no effort.");
        Assert.Null(Catalog.NearestAnalogEffort("claude-haiku-4-5", "high"));
    }

    [Fact]
    public void UnknownModel_IsLenient_AllEffortAllowed_NoAuto()
    {
        // A hand-typed custom id must not blank the effort dropdown, and must not claim auto support.
        Assert.True(Catalog.SupportedEffortLevels("my/custom-model").Count >= 4);
        Assert.False(Catalog.SupportsAutoMode("my/custom-model"));
    }

    [Fact]
    public void SupportsAutoMode_GatedByModel()
    {
        Assert.True(Catalog.SupportsAutoMode("claude-opus-4-8"));
        Assert.False(Catalog.SupportsAutoMode("claude-haiku-4-5"), "Haiku does not support auto.");
    }

    [Fact]
    public void Parse_EmptyObject_YieldsEmptyCatalog()
    {
        ModelCatalog c = ModelCatalogLoader.Parse("{}");
        Assert.Empty(c.Models);
        // Empty catalog stays lenient and never throws.
        Assert.False(c.SupportsAutoMode("opus"));
    }

    [Fact]
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

        MessageAssert.Equal("claude-opus-4-8", c.Resolve("opus-latest")?.Id, "Secondary alias resolves via the alias-map fallback.");
        MessageAssert.Equal("claude-opus-4-8", c.Resolve("OPUS-LATEST")?.Id, "Secondary alias resolves case-insensitively.");
    }

    [Fact]
    public void NearestAnalogEffort_OpusMax_CoercesToXhigh()
    {
        // 'max' (order 4, session-only) on Opus 4.8 (persistable low/medium/high/xhigh =
        // orders 0-3) is nearest to xhigh (|4-3|=1) — the ordinal-distance branch, not
        // the default/highest fallback (distinct from Sonnet, which lacks xhigh).
        Assert.Equal("xhigh", Catalog.NearestAnalogEffort("claude-opus-4-8", "max"));
    }

    [Fact]
    public void NearestAnalogEffort_CustomString_PrefersPersistableModelDefault()
    {
        // An effort id with no known order (custom string) falls back to the model's
        // declared default when that default persists (Opus 4.8 default = 'high').
        Assert.Equal("high", Catalog.NearestAnalogEffort("claude-opus-4-8", "wibble"));
    }

    [Fact]
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

        Assert.Equal("high", c.NearestAnalogEffort("m", "wibble"));
    }

    [Fact]
    public void ModelSuggestions_IncludesOneMVariants_ForSupportingModelsOnly()
    {
        List<string> s = Catalog.ModelSuggestions().ToList();
        MessageAssert.Contains("opus[1m]", s, "Opus supports 1m → its [1m] variant is offered.");
        MessageAssert.DoesNotContain("haiku[1m]", s,
            "Haiku has no [1m] variant: Haiku 4.5 has no 1M window, and Haiku 5.5 runs with it "
            + "natively, 'with no [1m] suffix to select' (Claude Code model-config docs).");
    }

    [Fact]
    public void ModelSuggestions_Include1mFalse_OmitsVariants()
    {
        Assert.DoesNotContain("opus[1m]", Catalog.ModelSuggestions(include1m: false).ToList());
    }

    [Fact]
    public void ModelSuggestions_IncludeLegacyTrue_AddsLegacyIds()
    {
        MessageAssert.DoesNotContain("claude-opus-4-7", Catalog.ModelSuggestions().ToList(), "Legacy id omitted by default.");
        MessageAssert.Contains("claude-opus-4-7", Catalog.ModelSuggestions(includeLegacy: true).ToList(), "Legacy id included when requested.");
    }
}
