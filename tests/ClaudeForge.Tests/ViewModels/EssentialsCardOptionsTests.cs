using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Essentials;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;
using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Sdk.Env;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// The deep-link label — the one piece of card behaviour that changed when the card view-model
/// moved to the shell.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>This was uncovered before the move, and the move is exactly what made it fragile.</b>
/// <c>EssentialsCardViewModel</c> used to read <c>Strings.LabelEssentialsViewInGroupFmt</c>
/// directly; the shell cannot see a product's resources, so the format now arrives through
/// <see cref="EssentialsCardOptions.ViewInGroupLabelFormat"/>. Nothing asserted the resulting
/// label, so a host that forgot to supply the format would have rendered "Permissions" — or,
/// with the record's <c>"{0}"</c> default, silently the bare group name — on every card.
/// </para>
/// <para>
/// ⭐ <b>The last test asserts against the PRODUCTION supplier, not a local copy of the format.</b>
/// A test that passes its own format string and checks the result cannot fail when the host stops
/// supplying one — the tautological-fixture shape that hid an inert scope escalation behind 85
/// green tests earlier in this project.
/// </para>
/// </remarks>
[TestClass]
public sealed class EssentialsCardOptionsTests
{
    private static EssentialsCardOptions Minimal(string viewInGroupTitle, string format) => new()
    {
        Id = "probe",
        Title = "t",
        Body = "b",
        Severity = AppSeverity.Neutral,
        Kind = EssentialsCardKind.Bool,
        ReadAsync = _ => Task.CompletedTask,
        WriteAsync = _ => Task.CompletedTask,
        ViewInGroupTitle = viewInGroupTitle,
        ViewInGroupLabelFormat = format,
    };

    /// <summary>The real card set, over an empty in-memory User workspace.</summary>
    private static EssentialsViewModel BuildVm()
    {
        JsonObject root = (JsonObject)JsonNode.Parse("{}")!;
        SettingsDocument doc = new(ConfigScope.User, "user.json", root, isReadOnly: false);
        SettingsWorkspace ws = new([doc], ClaudeMergePolicy.Instance);
        return new EssentialsViewModel(
            ClaudeCodeClient.FromExistingWorkspace(ws, ConfigScope.User, schemaRegistry: new SchemaRegistry()),
            new FakeEnvironmentProvider());
    }

    [TestMethod]
    public void TheDeepLinkLabelIsFormattedFromTheHostsFormatAndTheGroupTitle()
    {
        EssentialsCardViewModel card = new(Minimal("Permissions", "Go to {0} now"));

        Assert.AreEqual("Go to Permissions now", card.ViewInGroupLabel);
    }

    /// <summary>A card with no group home renders no button, so it gets no label.</summary>
    /// <remarks>
    /// ⚠ Empty rather than the format applied to an empty string: formatting would yield
    /// "View in " — a button labelled with a dangling preposition.
    /// </remarks>
    [TestMethod]
    public void ACardWithNoGroupHomeGetsAnEmptyLabel()
    {
        EssentialsCardViewModel card = new(Minimal(string.Empty, "View in {0}"));

        Assert.AreEqual(string.Empty, card.ViewInGroupLabel);
        Assert.AreEqual(string.Empty, card.ViewInGroupTitle);
    }

    /// <summary>
    /// Every card that targets a schema-driven page carries the JSON path its deep link filters to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔⛔ <b>This exists because a canary with a GUESSED test name measured the gap.</b> Dropping
    /// <see cref="EssentialsCardOptions.JsonPathFilter"/> from the card's wiring entirely reddened
    /// <b>nothing</b> — the property had no coverage at all. It is the difference between "View in
    /// Permissions" landing on the page and landing on the specific property, so losing it degrades
    /// silently: the button still works, just less well, and no test noticed.
    /// </para>
    /// <para>
    /// ⚠ Asserts the exact path per card rather than merely non-empty. A wrong path is the failure
    /// that actually happens — these were object-initializer values moved into a record, and a
    /// transposition between two cards would leave every one of them non-empty.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EveryCardKeepsTheDeepLinkPathItTargets()
    {
        EssentialsViewModel vm = BuildVm();

        // ⭐ Keyed by the production id CONSTANTS, not string literals: the ids are not the
        // property paths (CardIdMaxOutputTokens is "CLAUDE_CODE_MAX_OUTPUT_TOKENS"), and a
        // hand-copied literal is how this table silently stops matching any card.
        Dictionary<string, string> expected = new(StringComparer.Ordinal)
        {
            [EssentialsViewModel.CardIdAutoMemoryEnabled] = "autoMemoryEnabled",
            [EssentialsViewModel.CardIdMaxOutputTokens] = EnvVarKey.MaxOutputTokens,
            [EssentialsViewModel.CardIdMaxThinkingTokens] = EnvVarKey.MaxThinkingTokens,
            [EssentialsViewModel.CardIdEffortLevel] = "effortLevel",
            [EssentialsViewModel.CardIdFastMode] = "fastMode",
            [EssentialsViewModel.CardIdModel] = "model",
            [EssentialsViewModel.CardIdDisableBypass] = "permissions.disableBypassPermissionsMode",
            [EssentialsViewModel.CardIdEnableAllProjectMcp] = "enableAllProjectMcpServers",
            [EssentialsViewModel.CardIdSandboxEnabled] = "sandbox.enabled",
            [EssentialsViewModel.CardIdSandboxDomains] = "sandbox.network.allowedDomains",
            [EssentialsViewModel.CardIdAutoUpdatesChannel] = "autoUpdatesChannel",
        };

        List<string> wrong = [];
        int checkedCards = 0;

        foreach (EssentialsCardViewModel card in vm.Cards)
        {
            if (!expected.TryGetValue(card.Id, out string? want))
            {
                // A card with no schema home (e.g. the WindowState-backed update check) has none.
                continue;
            }

            checkedCards++;
            if (!string.Equals(card.JsonPathFilter, want, StringComparison.Ordinal))
            {
                wrong.Add($"  {card.Id}: got \"{card.JsonPathFilter}\", expected \"{want}\"");
            }
        }

        Assert.AreEqual(expected.Count, checkedCards,
            $"only {checkedCards} of {expected.Count} expected card ids were found — a card was "
            + "renamed or removed, and the missing ones are silently unchecked. Update this table "
            + "deliberately rather than letting the scan shrink.");

        Assert.IsTrue(wrong.Count == 0,
            $"{wrong.Count} card(s) lost or mismatched their deep-link path:\n"
            + string.Join('\n', wrong)
            + "\n\nWithout it the \"View in <group>\" button lands on the page instead of the "
            + "property, which is a silent degradation rather than a visible break.");
    }

    /// <summary>
    /// The real Claude cards carry the real resource format — not the record's bare default.
    /// </summary>
    /// <remarks>
    /// ⛔⛔ <b>This is the assertion that actually guards the seam.</b>
    /// <see cref="EssentialsCardOptions.ViewInGroupLabelFormat"/> defaults to <c>"{0}"</c>, which
    /// is a legitimate no-op for a host with no wording — and therefore indistinguishable from a
    /// host that FORGOT to supply one. Only comparing against
    /// <c>Strings.LabelEssentialsViewInGroupFmt</c> as the app resolves it can tell those apart.
    /// </remarks>
    [TestMethod]
    public void EveryClaudeCardWithAGroupHomeUsesTheAppsOwnFormat()
    {
        EssentialsViewModel vm = BuildVm();

        List<EssentialsCardViewModel> linked =
            [.. vm.Cards.Where(c => !string.IsNullOrEmpty(c.ViewInGroupTitle))];

        Assert.IsTrue(linked.Count > 0,
            "no card declares a group home — the scan has lost its subjects and would pass "
            + "without checking anything");

        List<string> wrong = [];
        foreach (EssentialsCardViewModel card in linked)
        {
            string expected = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                Strings.LabelEssentialsViewInGroupFmt,
                card.ViewInGroupTitle);

            if (!string.Equals(card.ViewInGroupLabel, expected, StringComparison.Ordinal))
            {
                wrong.Add($"  {card.Id}: got \"{card.ViewInGroupLabel}\", expected \"{expected}\"");
            }
        }

        Assert.IsTrue(wrong.Count == 0,
            $"{wrong.Count} of {linked.Count} card(s) are not using the app's deep-link format:\n"
            + string.Join('\n', wrong)
            + "\n\nThe shell cannot read a product's resources, so BuildCards must supply the "
            + "format (see the Card() helper). Without it the record's \"{0}\" default renders "
            + "the bare group name and the button loses its verb.");
    }
}
