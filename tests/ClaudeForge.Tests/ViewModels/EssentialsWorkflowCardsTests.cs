using System.Collections;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Essentials;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk.Env;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.AppServices.Abstractions;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;
using Bennewitz.Ninja.ClaudeForge.Services;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.ScopedEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// The three Essentials cards for the settings tied to Ultracode (plans/00010 step W, decision 4):
/// <c>disableWorkflows</c>, <c>workflowKeywordTriggerEnabled</c> and <c>workflowSizeGuideline</c>.
/// </summary>
/// <remarks>
/// Each card is driven through a real <see cref="ClaudeCodeClient"/> over an in-memory User
/// document, so a write is observed where <c>settings.json</c> would get it, and an unset value is
/// observed as inherit rather than as the schema's default.
/// </remarks>
public sealed class EssentialsWorkflowCardsTests
{
    private static ClaudeConfigClientBase MakeClient(string userJson = "{}")
    {
        JsonObject root = (JsonObject)JsonNode.Parse(userJson)!;
        SettingsDocument doc = new(ConfigScope.User, "user.json", root, isReadOnly: false);
        SettingsWorkspace ws = new([doc], ClaudeMergePolicy.Instance);
        return ClaudeCodeClient.FromExistingWorkspace(ClaudeEnvironment.Empty,
            ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
    }

    private static async Task<(ClaudeConfigClientBase Client, EssentialsCardViewModel Card)> Open(
        string cardId, string userJson = "{}")
    {
        ClaudeConfigClientBase client = MakeClient(userJson);
        EssentialsViewModel vm = new(client, new FakeEnvironmentProvider());
        await vm.RefreshAsync();
        EssentialsCardViewModel card = vm.GetCardById(cardId)
            ?? throw new InvalidOperationException("No Essentials card with id " + cardId);
        return (client, card);
    }

    [Theory]
    [InlineData(EssentialsViewModel.CardIdDisableWorkflows)]
    [InlineData(EssentialsViewModel.CardIdWorkflowKeywordTrigger)]
    public async Task BoolCard_RoundTripsThroughSettingsJson(string key)
    {
        (ClaudeConfigClientBase client, EssentialsCardViewModel card) = await Open(key);
        MessageAssert.Equal(EssentialsCardKind.Bool, card.Kind, key + " is a boolean setting.");

        card.BoolValue = false;
        MessageAssert.Equal(false, client.GetEffective<bool?>(key), "false is written, not dropped as a default.");

        card.BoolValue = true;
        MessageAssert.Equal(true, client.GetEffective<bool?>(key), "true is written.");

        card.BoolValue = null;
        MessageAssert.Null(client.GetEffective<bool?>(key), "Clearing the card removes the key.");
    }

    [Theory]
    [InlineData(EssentialsViewModel.CardIdDisableWorkflows, """{"disableWorkflows": true}""", true)]
    [InlineData(EssentialsViewModel.CardIdWorkflowKeywordTrigger, """{"workflowKeywordTriggerEnabled": false}""", false)]
    public async Task BoolCard_ReadsWhatSettingsJsonHolds(string key, string json, bool expected)
    {
        (_, EssentialsCardViewModel card) = await Open(key, json);
        MessageAssert.Equal(expected, card.BoolValue, "The card shows the value on disk.");
    }

    [Theory]
    [InlineData(EssentialsViewModel.CardIdDisableWorkflows)]
    [InlineData(EssentialsViewModel.CardIdWorkflowKeywordTrigger)]
    public async Task BoolCard_Unset_ShowsAsInherit_NotAsTheSchemaDefault(string key)
    {
        // workflowKeywordTriggerEnabled defaults to TRUE in the schema; showing that as a set value
        // would hide that nothing is written, and writing it back would pin it.
        (ClaudeConfigClientBase client, EssentialsCardViewModel card) = await Open(key);
        MessageAssert.Null(card.BoolValue, "An unset setting reads as inherit.");
        MessageAssert.Null(client.GetEffective<bool?>(key), "Opening the card wrote nothing.");
    }

    [Fact]
    public async Task SizeCard_RoundTripsThroughSettingsJson()
    {
        const string key = EssentialsViewModel.CardIdWorkflowSizeGuideline;
        (ClaudeConfigClientBase client, EssentialsCardViewModel card) = await Open(key);
        MessageAssert.Equal(EssentialsCardKind.EnumString, card.Kind, "The size guideline is an enum.");
        MessageAssert.Null(card.EnumValue, "An unset guideline reads as inherit.");

        card.EnumValue = "small";
        MessageAssert.Equal("small", client.GetEffective<string>(key), "The chosen value is written.");

        card.EnumValue = null;
        MessageAssert.Null(client.GetEffective<string>(key), "Clearing the card removes the key.");
    }

    [Fact]
    public async Task SizeCard_OffersExactlyTheSchemasValues()
    {
        string schemaPath = Path.Combine(FindRepoRoot(), "src", "AgentForge.Core", "Assets", "Schemas",
            "claude-code-settings.json");
        JsonNode schema = JsonNode.Parse(await File.ReadAllTextAsync(schemaPath, TestContext.Current.CancellationToken))!;
        string[] fromSchema =
        [
            .. schema["properties"]!["workflowSizeGuideline"]!["enum"]!.AsArray()
                .Select(v => v!.GetValue<string>()),
        ];
        Assert.True(fromSchema.Length > 0, "The bundled schema lists no values; the comparison would be vacuous.");

        (_, EssentialsCardViewModel card) = await Open(EssentialsViewModel.CardIdWorkflowSizeGuideline);
        MessageAssert.SequenceEqual(fromSchema, card.EnumOptions,
            "The card offers exactly the schema's values, in its order. A new upstream value must be offered too.");
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClaudeForge.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate ClaudeForge.slnx.");
    }
}
