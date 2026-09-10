using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Essentials;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Navigation;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.OpenCode.Avalonia.Essentials;
using Bennewitz.Ninja.OpenCode.Sdk;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tests;

/// <summary>
/// The Essentials page can show a host app's own boolean preference alongside the schema-backed
/// cards.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>Every other card on this page reads and writes a JSON path through the config client.</b>
/// An app preference has none — it lives in the host's state file, which this project sits below
/// and cannot reach. So the host supplies the accessors and the text, and these assert that the
/// card actually routes to them rather than quietly doing nothing.
/// </para>
/// <para>
/// ⚠ <b>"Quietly doing nothing" is the specific risk.</b> A card whose <c>WriteAsync</c> never
/// fires looks completely normal: the checkbox moves, the page renders, and the preference simply
/// never changes. Nothing throws and nothing is logged.
/// </para>
/// </remarks>
[TestClass]
public sealed class AppPreferenceCardTests
{
    private const string CardId = "app.testPreference";

    private string _configDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _configDir = Path.Combine(Path.GetTempPath(), "ocav-pref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_configDir))
            {
                Directory.Delete(_configDir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A handle still open is not a test failure.
        }
    }

    /// <remarks>
    /// <c>ProjectConfigDisabled</c> so the config walk cannot climb out of the sandbox and pick
    /// up a real <c>opencode.json</c> from an ancestor of the runner's working directory.
    /// </remarks>
    private OpenCodeEnvironment Env() => new(_configDir, null, null, ProjectConfigDisabled: true);

    /// <summary>A minimal layout — these tests never follow a "View in …" link.</summary>
    private static SchemaPageLayout Layout { get; } = new()
    {
        PropertyToPage = new Dictionary<string, string>(StringComparer.Ordinal),
        PageOrder = ["Advanced"],
        FallbackPage = "Advanced",
    };

    private OpenCodeEssentialsViewModel Build(
        Func<bool> get, Action<bool> set)
        => new(
            client: null,
            environment: Env(),
            layout: Layout,
            danger: null,
            appPreferences:
            [
                new EssentialsAppPreference(
                    Id: CardId,
                    Title: "Check for updates on launch",
                    Body: "Body text supplied by the host.",
                    Get: get,
                    Set: set),
            ]);

    [TestMethod]
    public void TheCardIsAddedAndShowsTheHostsCurrentValue()
    {
        bool stored = false;
        OpenCodeEssentialsViewModel vm = Build(() => stored, v => stored = v);

        EssentialsCardViewModel? card = vm.GetCardById(CardId);

        Assert.IsNotNull(card, $"No card '{CardId}' was added for the supplied app preference.");
        Assert.AreEqual("Check for updates on launch", card.Title, "The host's title was not used.");
        Assert.AreEqual(
            false,
            card.BoolValue,
            "The card did not read the host's current value on construction, so the page would "
            + "open showing something other than the real preference.");
    }

    [TestMethod]
    public async Task TogglingTheCardWritesThroughToTheHost()
    {
        bool stored = false;
        OpenCodeEssentialsViewModel vm = Build(() => stored, v => stored = v);
        EssentialsCardViewModel card = vm.GetCardById(CardId)!;

        card.BoolValue = true;
        await card.WriteAsync();

        Assert.IsTrue(
            stored,
            "Toggling the card did not reach the host's setter. The checkbox would move and the "
            + "preference would never change — with nothing thrown and nothing logged.");
    }

    /// <summary>
    /// A refresh re-reads the host rather than showing a cached copy.
    /// </summary>
    /// <remarks>
    /// The same preference is reachable from elsewhere in the app, so a value cached at
    /// construction would leave this page contradicting the setting it is displaying.
    /// </remarks>
    [TestMethod]
    public async Task RefreshRereadsTheHostRatherThanCaching()
    {
        bool stored = false;
        OpenCodeEssentialsViewModel vm = Build(() => stored, v => stored = v);
        EssentialsCardViewModel card = vm.GetCardById(CardId)!;
        Assert.AreEqual(false, card.BoolValue, "Premise: the card must start at the host's value.");

        // Changed behind the page's back, as another surface would.
        stored = true;
        await card.ReadAsync();

        Assert.AreEqual(
            true,
            card.BoolValue,
            "The card served a cached value after the preference changed elsewhere.");
    }

    /// <summary>
    /// An app-preference card claims no JSON path, no danger and no deep link.
    /// </summary>
    /// <remarks>
    /// ⚠ Each omission is load-bearing. A <c>JsonPathFilter</c> would make the card appear when
    /// the user filters to a key it has nothing to do with; a non-neutral severity would dilute
    /// what a danger dot means next to keys that can auto-approve tool execution; and a
    /// "View in …" link would navigate to a settings page that does not contain this value.
    /// </remarks>
    [TestMethod]
    public void TheCardClaimsNoDocumentIdentity()
    {
        OpenCodeEssentialsViewModel vm = Build(() => false, _ => { });
        EssentialsCardViewModel card = vm.GetCardById(CardId)!;

        Assert.IsTrue(
            string.IsNullOrEmpty(card.JsonPathFilter),
            "An app preference has no JSON path; claiming one makes the card surface under a "
            + "filter for a key it is unrelated to.");
        Assert.AreEqual(
            AppSeverity.Neutral,
            card.Severity,
            "An app preference carries no risk to the configuration being edited.");
    }
}
