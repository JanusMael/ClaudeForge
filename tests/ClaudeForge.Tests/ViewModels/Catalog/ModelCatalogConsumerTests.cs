using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Models;
using Bennewitz.Ninja.ClaudeForge.Tests.ViewModels; // FakeEnvironmentProvider
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Catalog;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Essentials;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Catalog;

/// <summary>
/// Verifies the app consumers source their value lists from the SDK model
/// catalog (not hardcoded arrays) and that the GUI localization seam covers
/// every catalogued default mode.
/// </summary>
public sealed class ModelCatalogConsumerTests
{
    private static ClaudeConfigClientBase MakeClient(string userJson = "{}")
    {
        JsonObject root = (JsonObject)JsonNode.Parse(userJson)!;
        SettingsDocument doc = new(ConfigScope.User, "user.json", root, isReadOnly: false);
        SettingsWorkspace ws = new([doc], ClaudeMergePolicy.Instance);
        return ClaudeCodeClient.FromExistingWorkspace(ClaudeEnvironment.Empty, ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
    }

    private static EssentialsViewModel MakeEssentials(ClaudeConfigClientBase? client = null)
        => new(client ?? MakeClient(), new FakeEnvironmentProvider());

    private static SchemaNode PermissionsSchema()
        => new("permissions", "permissions") { ValueType = SchemaValueType.Complex };

    [Fact]
    public void CatalogLocalization_MapsEveryDefaultMode()
    {
        foreach (string id in ModelCatalogProvider.Default.AllDefaultModes.Select(d => d.Id))
        {
            string label = CatalogLocalization.DefaultModeLabel(id);
            Assert.False(string.IsNullOrWhiteSpace(label), $"No label for default mode '{id}'.");
            MessageAssert.NotEqual(id, label, $"Default mode '{id}' fell through to the raw-id fallback.");
            Assert.False(string.IsNullOrWhiteSpace(CatalogLocalization.DefaultModeDescription(id)),
                $"No description for default mode '{id}'.");
        }
    }

    [Fact]
    public void PermissionsEditor_DefaultModeInfos_ComeFromCatalog()
    {
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);

        // Alias entries (e.g. "manual" → "default") are deliberately NOT offered —
        // they'd read as a duplicate of the mode they alias — so the offered list
        // mirrors the catalog's real modes, in order.
        MessageAssert.SequenceEqual(
            ModelCatalogProvider.Default.AllDefaultModes.Where(d => !d.IsAlias).Select(d => d.Id).ToList(),
            vm.DefaultModeInfos.Select(i => i.Value).ToList(),
            "DefaultModeInfos must mirror the catalog's non-alias default modes, in order.");

        DefaultModeInfo? delegateInfo = vm.DefaultModeInfos.FirstOrDefault(i => i.Value == "delegate");
        Assert.NotNull(delegateInfo);
        Assert.True(delegateInfo!.IsExperimental, "delegate is experimental in the catalog.");

        // Lock the alias contract in both directions: the catalog carries the alias
        // (so the settings enum stays in parity with the schema and a persisted
        // value round-trips), but the editor never offers it as a choice.
        Assert.True(
            ModelCatalogProvider.Default.AllDefaultModes.Any(d => d.Id == "manual" && d.AliasOf == "default"),
            "The catalog must carry 'manual' as an alias of 'default' — the schema's defaultMode enum includes it.");
        Assert.False(
            vm.DefaultModeInfos.Any(i => i.Value == "manual"),
            "An alias must not be offered as a separate choice; 'manual' is just 'default' relabelled.");
    }

    [Fact]
    public void Essentials_ModelCard_OptionsFromCatalog_AndEditable()
    {
        EssentialsViewModel vm = MakeEssentials();
        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdModel)!;

        MessageAssert.SequenceEqual(
            ModelCatalogProvider.Default.ModelSuggestions().ToList(),
            card.EnumOptions.ToList(),
            "Model card options must come from the catalog suggestions.");
        Assert.True(card.AllowsFreeForm, "Model card must be free-form (editable).");
        Assert.True(card.IsFreeFormEnumString);
        Assert.False(card.IsStrictEnumString);
    }

    [Fact]
    public void Essentials_EffortCard_OptionsFromCatalog_OmitMax()
    {
        EssentialsViewModel vm = MakeEssentials();
        EssentialsCardViewModel card = vm.GetCardById(EssentialsViewModel.CardIdEffortLevel)!;

        // No model set → lenient persistable set (omits session-only "max").
        Assert.Equal(
            ModelCatalogProvider.Default.PersistableEffortLevels(null).ToList(),
            card.EnumOptions.ToList());
        Assert.DoesNotContain("max", card.EnumOptions.ToList());
        Assert.True(card.IsStrictEnumString, "Effort is a strict enum (not editable).");
        Assert.False(card.AllowsFreeForm);
    }
}
