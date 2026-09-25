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

    [Fact]
    public void SessionOnlyEffortLevels_MirrorFullRange_ButStayNonPersistable()
    {
        // "Mirror the UI": the catalog carries the full effort range Claude Desktop shows —
        // including session-only `max` AND `ultracode` (the dynamic-workflow tier that
        // reports as xhigh) — so ClaudeForge's model matches reality rather than pretending
        // they don't exist. Both are persists:false, so neither reaches the settings-file
        // effortLevel dropdown (which can only hold persistable values).
        EffortLevelInfo ultra = Catalog.EffortLevels.Single(e => e.Id == "ultracode");
        Assert.False(ultra.Persists, "ultracode is session-only (not accepted in settings.json).");
        Assert.True(
            ultra.Order > Catalog.EffortLevels.Single(e => e.Id == "max").Order,
            "ultracode sits above max on the Faster→Smarter axis.");

        // Full range (incl. ultracode) is exposed for models that carry xhigh+max…
        Assert.Contains("ultracode", Catalog.SupportedEffortLevels("claude-opus-4-8").ToList());
        // …but the persistable set stays low/medium/high/xhigh (no max, no ultracode).
        Assert.DoesNotContain("ultracode", Catalog.PersistableEffortLevels("claude-opus-4-8").ToList());

        // Models without xhigh don't gain the ultracode tier (it reports as xhigh).
        Assert.DoesNotContain("ultracode", Catalog.SupportedEffortLevels("claude-sonnet-4-6").ToList());
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
        MessageAssert.DoesNotContain("haiku[1m]", s, "Haiku does not support 1m → no [1m] variant.");
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
