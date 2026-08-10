using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Models;
using Bennewitz.Ninja.ClaudeForge.Tests.ViewModels; // FakeEnvironmentProvider
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Catalog;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Catalog;

/// <summary>
/// Verifies the app consumers source their value lists from the SDK model
/// catalog (not hardcoded arrays) and that the GUI localization seam covers
/// every catalogued default mode.
/// </summary>
[TestClass]
public sealed class ModelCatalogConsumerTests
{
    private static ClaudeConfigClientCore MakeClient(string userJson = "{}")
    {
        JsonObject root = (JsonObject)JsonNode.Parse(userJson)!;
        SettingsDocument doc = new(ConfigScope.User, "user.json", root, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        return ClaudeCodeClient.FromExistingWorkspace(ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
    }

    private static EssentialsViewModel MakeEssentials(ClaudeConfigClientCore? client = null)
        => new(client ?? MakeClient(), new FakeEnvironmentProvider());

    private static SchemaNode PermissionsSchema()
        => new("permissions", "permissions") { ValueType = SchemaValueType.Complex };

    [TestMethod]
    public void CatalogLocalization_MapsEveryDefaultMode()
    {
        foreach (string id in ModelCatalogProvider.Default.AllDefaultModes.Select(d => d.Id))
        {
            string label = CatalogLocalization.DefaultModeLabel(id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(label), $"No label for default mode '{id}'.");
            Assert.AreNotEqual(id, label, $"Default mode '{id}' fell through to the raw-id fallback.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(CatalogLocalization.DefaultModeDescription(id)),
                $"No description for default mode '{id}'.");
        }
    }

    [TestMethod]
    public void PermissionsEditor_DefaultModeInfos_ComeFromCatalog()
    {
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);

        // Alias entries (e.g. "manual" → "default") are deliberately NOT offered —
        // they'd read as a duplicate of the mode they alias — so the offered list
        // mirrors the catalog's real modes, in order.
        CollectionAssert.AreEqual(
            ModelCatalogProvider.Default.AllDefaultModes.Where(d => !d.IsAlias).Select(d => d.Id).ToList(),
            vm.DefaultModeInfos.Select(i => i.Value).ToList(),
            "DefaultModeInfos must mirror the catalog's non-alias default modes, in order.");

        DefaultModeInfo? delegateInfo = vm.DefaultModeInfos.FirstOrDefault(i => i.Value == "delegate");
        Assert.IsNotNull(delegateInfo);
        Assert.IsTrue(delegateInfo!.IsExperimental, "delegate is experimental in the catalog.");

        // Lock the alias contract in both directions: the catalog carries the alias
        // (so the settings enum stays in parity with the schema and a persisted
        // value round-trips), but the editor never offers it as a choice.
        Assert.IsTrue(
            ModelCatalogProvider.Default.AllDefaultModes.Any(d => d.Id == "manual" && d.AliasOf == "default"),
            "The catalog must carry 'manual' as an alias of 'default' — the schema's defaultMode enum includes it.");
        Assert.IsFalse(
            vm.DefaultModeInfos.Any(i => i.Value == "manual"),
            "An alias must not be offered as a separate choice; 'manual' is just 'default' relabelled.");
    }

    [TestMethod]
    public void Essentials_ModelCard_OptionsFromCatalog_AndEditable()
    {
        EssentialsViewModel vm = MakeEssentials();
        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdModel)!;

        CollectionAssert.AreEqual(
            ModelCatalogProvider.Default.ModelSuggestions().ToList(),
            card.EnumOptions.ToList(),
            "Model card options must come from the catalog suggestions.");
        Assert.IsTrue(card.AllowsFreeForm, "Model card must be free-form (editable).");
        Assert.IsTrue(card.IsFreeFormEnumString);
        Assert.IsFalse(card.IsStrictEnumString);
    }

    [TestMethod]
    public void Essentials_EffortCard_OptionsFromCatalog_OmitMax()
    {
        EssentialsViewModel vm = MakeEssentials();
        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdEffortLevel)!;

        // No model set → lenient persistable set (omits session-only "max").
        CollectionAssert.AreEqual(
            ModelCatalogProvider.Default.PersistableEffortLevels(null).ToList(),
            card.EnumOptions.ToList());
        CollectionAssert.DoesNotContain(card.EnumOptions.ToList(), "max");
        Assert.IsTrue(card.IsStrictEnumString, "Effort is a strict enum (not editable).");
        Assert.IsFalse(card.AllowsFreeForm);
    }
}
