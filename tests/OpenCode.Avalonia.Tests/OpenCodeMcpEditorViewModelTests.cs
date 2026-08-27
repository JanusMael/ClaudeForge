using System.ComponentModel;
using Bennewitz.Ninja.OpenCode.Avalonia.Mcp;
using Bennewitz.Ninja.OpenCode.Sdk.Mcp;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tests;

/// <summary>
/// The MCP editor: the two behaviours the plan asked for, and the plumbing that decides whether an
/// edit reaches disk at all.
/// </summary>
/// <remarks>
/// The two headline behaviours — holding both union arms across a switch, and preserving an entry
/// this build cannot classify — are marked in the tests below. The second is the one the plan
/// claimed its recommended template already had; it does not, so it is built and tested here from
/// scratch rather than copied.
/// </remarks>
[TestClass]
public sealed class OpenCodeMcpEditorViewModelTests
{
    private static OpenCodeMcpEditorViewModel LoadedWith(string json)
    {
        OpenCodeMcpEditorViewModel vm = new(new TestSchema("mcp"), TestScope.Project);
        vm.LoadFromValue(
            new TestValue("mcp").With(TestScope.Project, CurrencyText.Parse(json)),
            TestScope.Project);
        return vm;
    }

    private static void AssertRoundTrips(string json, string because)
    {
        Assert.AreEqual(
            CurrencyText.Render(CurrencyText.Parse(json)),
            CurrencyText.Render(LoadedWith(json).ToValue()),
            because);
    }

    // ── Round trips through the editor ───────────────────────────────────────

    [TestMethod]
    public void ALocalServer_RoundTripsThroughTheEditor()
    {
        AssertRoundTrips(
            """
            {"fs":{"type":"local","command":["npx","-y","@mcp/fs"],"cwd":"/srv",
            "environment":{"TOKEN":"abc"},"enabled":true,"timeout":15000}}
            """,
            "Opening a config and saving it without touching anything must not change it.");
    }

    [TestMethod]
    public void ARemoteServerWithOAuth_RoundTripsThroughTheEditor()
    {
        AssertRoundTrips(
            """
            {"api":{"type":"remote","url":"https://mcp.example.com/sse","enabled":false,
            "headers":{"X-Tenant":"acme"},
            "oauth":{"clientId":"cid","clientSecret":"shh","scope":"read",
            "callbackPort":20000,"redirectUri":"http://127.0.0.1:20000/cb"},"timeout":30000}}
            """,
            "Including the OAuth sub-object and the secret.");
    }

    [TestMethod]
    public void AnEnabledOnlyOverride_RoundTripsThroughTheEditor()
    {
        AssertRoundTrips(
            """{"shared":{"enabled":false}}""",
            "The schema's third arm. An editor built to the plan's two-arm description would have "
            + "treated this as unparseable.");
    }

    // ── ⭐ Behaviour (a): both arms held across a switch ─────────────────────

    /// <remarks>
    /// ⭐ The behaviour the recommended template genuinely does have, and it matters more here:
    /// retyping an argv, a working directory and a dozen environment variables is not a small loss.
    /// </remarks>
    [TestMethod]
    public void SwitchingLocalToRemoteAndBack_KeepsEveryLocalField()
    {
        OpenCodeMcpEditorViewModel vm = LoadedWith(
            """
            {"fs":{"type":"local","command":["npx","-y","@mcp/fs"],"cwd":"/srv",
            "environment":{"TOKEN":"abc","LOG":"debug"}}}
            """);

        OpenCodeMcpServerViewModel server = vm.Servers.Single();

        server.Kind = OpenCodeMcpKind.Remote;
        server.Url = "https://example.com";

        Assert.AreEqual(
            3,
            server.Command.Rows.Count,
            "The local arm stays in memory while the remote arm is showing.");

        server.Kind = OpenCodeMcpKind.Local;

        CollectionAssert.AreEqual(
            new[] { "npx", "-y", "@mcp/fs" },
            server.Command.Rows.Select(r => r.Value).ToArray(),
            "Switching back must restore argv in order.");
        Assert.AreEqual("/srv", server.WorkingDirectory);
        CollectionAssert.AreEqual(
            new[] { "TOKEN", "LOG" },
            server.Environment.Rows.Select(r => r.Key).ToArray());
    }

    /// <remarks>
    /// Holding both arms is only safe if exactly one is written: both variants declare
    /// <c>additionalProperties: false</c>, so emitting the idle arm produces a config OpenCode
    /// rejects.
    /// </remarks>
    [TestMethod]
    public void OnlyTheSelectedArmIsWritten()
    {
        OpenCodeMcpEditorViewModel vm = LoadedWith(
            """{"fs":{"type":"local","command":["npx"],"cwd":"/srv"}}""");

        OpenCodeMcpServerViewModel server = vm.Servers.Single();
        server.Kind = OpenCodeMcpKind.Remote;
        server.Url = "https://example.com";

        string written = CurrencyText.Render(vm.ToValue());

        StringAssert.Contains(written, "remote");
        StringAssert.Contains(written, "https://example.com");
        Assert.IsFalse(written.Contains("command", StringComparison.Ordinal),
            "A remote entry carrying `command` is rejected by the schema.");
        Assert.IsFalse(written.Contains("/srv", StringComparison.Ordinal),
            "And so is one carrying `cwd`.");
    }

    // ── ⭐ Behaviour (b): an entry this build cannot classify ────────────────

    /// <remarks>
    /// ⭐ The forward-compatibility case, and the reason this editor does not follow the template
    /// the plan named: that one drops an unknown variant on load and again on save.
    /// </remarks>
    [TestMethod]
    public void AnUnrecognisedServerIsNotEditableAndSurvivesASave()
    {
        const string json =
            """{"future":{"type":"websocket","endpoint":"wss://x","retries":3}}""";

        OpenCodeMcpEditorViewModel vm = LoadedWith(json);
        OpenCodeMcpServerViewModel server = vm.Servers.Single();

        Assert.AreEqual(OpenCodeMcpKind.Unrecognised, server.Kind);
        Assert.IsTrue(server.IsOpaque);
        Assert.IsFalse(server.IsEditable, "Offering fields would mean guessing at an unknown shape.");
        Assert.AreNotEqual(string.Empty, server.OpaqueNotice, "And the user has to be told why.");
        Assert.AreEqual(1, vm.OpaqueServerCount);
        Assert.IsTrue(vm.HasOpaqueServers);

        AssertRoundTrips(json, "Dropping it deletes a working server; guessing rewrites one.");
    }

    /// <remarks>
    /// ⭐ Per-entry granularity, the improvement over echoing the whole value: one server from a
    /// newer OpenCode must not make the rest of the file read-only.
    /// </remarks>
    [TestMethod]
    public void EditingOneServerDoesNotDisturbAnUnrecognisedNeighbour()
    {
        OpenCodeMcpEditorViewModel vm = LoadedWith(
            """
            {"future":{"type":"websocket","endpoint":"wss://x"},
             "fs":{"type":"local","command":["npx"]}}
            """);

        Assert.AreEqual(2, vm.Servers.Count);

        OpenCodeMcpServerViewModel fs = vm.Servers.Single(s => s.Name == "fs");
        fs.Command.NewValue = "--root";
        fs.Command.AddCommand.Execute(null);

        string written = CurrencyText.Render(vm.ToValue());

        StringAssert.Contains(written, "wss://x", "The opaque entry is untouched.");
        StringAssert.Contains(written, "--root", "And the edit next door landed.");
        CollectionAssert.AreEqual(
            new[] { "future", "fs" },
            ((IReadOnlyDictionary<string, object?>)vm.ToValue()!).Keys.ToArray(),
            "Order is preserved across both.");
    }

    // ── Order ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ReorderingAnArgument_ChangesTheWrittenArgv()
    {
        OpenCodeMcpEditorViewModel vm = LoadedWith(
            """{"fs":{"type":"local","command":["npx","-y","pkg"]}}""");

        OpenCodeMcpServerViewModel server = vm.Servers.Single();
        server.Command.Rows[2].MoveUpCommand.Execute(null);

        StringAssert.Contains(
            CurrencyText.Render(vm.ToValue()),
            "[npx,pkg,-y]",
            "argv order is meaning — moving an argument must reach the written value.");
    }

    // ── Removal vs an empty object ───────────────────────────────────────────

    [TestMethod]
    public void ToValue_IsNullWhenThereAreNoServers()
    {
        OpenCodeMcpEditorViewModel vm = new(new TestSchema("mcp"), TestScope.Project);
        vm.LoadFromValue(new TestValue("mcp"), TestScope.Project);

        Assert.IsNull(
            vm.ToValue(),
            "An empty object persists an mcp key that configures nothing; null removes it.");
    }

    [TestMethod]
    public void AnUnsetEnabledStateOmitsTheKeyRatherThanWritingFalse()
    {
        OpenCodeMcpEditorViewModel vm = LoadedWith("""{"fs":{"type":"local","command":["npx"]}}""");

        Assert.AreEqual(OpenCodeMcpEnabledState.Unset, vm.Servers.Single().EnabledState);
        Assert.IsFalse(
            CurrencyText.Render(vm.ToValue()).Contains("enabled", StringComparison.Ordinal),
            "Absent means OpenCode's own default applies, which is not the same claim as false.");
    }

    [TestMethod]
    public void AddServer_DefaultsToLocal()
    {
        OpenCodeMcpEditorViewModel vm = new(new TestSchema("mcp"), TestScope.Project);
        vm.LoadFromValue(new TestValue("mcp"), TestScope.Project);

        vm.NewServerName = "fs";
        vm.AddServerCommand.Execute(null);

        Assert.AreEqual(OpenCodeMcpKind.Local, vm.Servers.Single().Kind);
        Assert.IsFalse(
            OpenCodeMcpEditorViewModel.SelectableKinds.Contains(OpenCodeMcpKind.Unrecognised),
            "Unrecognised is a state an entry arrives in, never one a user picks — offering it "
            + "would invite turning an editable server into an opaque blob.");
    }

    // ── The modified signal ──────────────────────────────────────────────────

    private static int CountIsModifiedFires(OpenCodeMcpEditorViewModel vm, Action mutate)
    {
        int fired = 0;
        void Handler(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OpenCodeMcpEditorViewModel.IsModified))
            {
                fired++;
            }
        }

        vm.PropertyChanged += Handler;
        try
        {
            mutate();
        }
        finally
        {
            vm.PropertyChanged -= Handler;
        }

        return fired;
    }

    /// <remarks>
    /// ⚠ The documented three-level subscription trap. The load path fills a server's nested
    /// collections <b>before</b> adding the server, so hooking only future additions leaves every
    /// loaded row silent — the edit never reaches the live-write or save-enable subscriptions, both
    /// of which watch <c>PropertyChanged(IsModified)</c> rather than the value.
    /// </remarks>
    [TestMethod]
    public void EditingALoadedArgument_FiresIsModifiedPropertyChanged()
    {
        OpenCodeMcpEditorViewModel vm = LoadedWith(
            """{"fs":{"type":"local","command":["npx","-y"]}}""");
        Assert.IsTrue(vm.IsModified, "Loading a scope that has the key sets the flag.");

        int fired = CountIsModifiedFires(vm, () => vm.Servers[0].Command.Rows[0].Value = "pnpx");

        Assert.IsTrue(fired >= 1, "An inline edit on a loaded row must fire even though the flag "
                                  + "was already true.");
    }

    [TestMethod]
    public void EditingALoadedEnvironmentValue_FiresIsModifiedPropertyChanged()
    {
        OpenCodeMcpEditorViewModel vm = LoadedWith(
            """{"fs":{"type":"local","command":["npx"],"environment":{"TOKEN":"abc"}}}""");

        int fired = CountIsModifiedFires(vm, () => vm.Servers[0].Environment.Rows[0].Value = "xyz");

        Assert.IsTrue(fired >= 1);
    }

    [TestMethod]
    public void EditingALoadedHeader_FiresIsModifiedPropertyChanged()
    {
        OpenCodeMcpEditorViewModel vm = LoadedWith(
            """{"api":{"type":"remote","url":"https://x","headers":{"H":"1"}}}""");

        int fired = CountIsModifiedFires(vm, () => vm.Servers[0].Headers.Rows[0].Value = "2");

        Assert.IsTrue(fired >= 1);
    }

    [TestMethod]
    public void RemovingALoadedArgument_FiresIsModifiedPropertyChanged()
    {
        OpenCodeMcpEditorViewModel vm = LoadedWith(
            """{"fs":{"type":"local","command":["npx","-y"]}}""");

        int fired = CountIsModifiedFires(
            vm,
            () => vm.Servers[0].Command.Rows[0].RemoveCommand.Execute(null));

        Assert.IsTrue(fired >= 1);
    }

    [TestMethod]
    public void TypingInTheAddBoxes_DoesNotMarkTheEditorModified()
    {
        OpenCodeMcpEditorViewModel vm = LoadedWith(
            """{"fs":{"type":"local","command":["npx"],"environment":{"A":"1"}}}""");

        int fired = CountIsModifiedFires(vm, () =>
        {
            vm.NewServerName = "ap";
            vm.Servers[0].Command.NewValue = "--ro";
            vm.Servers[0].Environment.NewKey = "TOK";
        });

        Assert.AreEqual(
            0,
            fired,
            "These back the add boxes. Marking the file dirty per keystroke makes Save flicker and, "
            + "on a live-write host, writes half-typed values to disk.");
    }

    // ── Reset ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ResetAfterEdit_RestoresTheLoadedStateRatherThanClearing()
    {
        OpenCodeMcpEditorViewModel vm = LoadedWith(
            """{"fs":{"type":"local","command":["npx"]}}""");

        vm.NewServerName = "extra";
        vm.AddServerCommand.Execute(null);
        Assert.AreEqual(2, vm.Servers.Count);

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(1, vm.Servers.Count, "Reset means 'what is on disk', not 'empty'.");
        Assert.AreEqual("fs", vm.Servers[0].Name);
        Assert.IsFalse(vm.IsModified);
    }

    // ── Preservation of what the editor does not surface ─────────────────────

    [TestMethod]
    public void FieldsTheEditorDoesNotSurface_SurviveAnEditElsewhereOnTheSameServer()
    {
        OpenCodeMcpEditorViewModel vm = LoadedWith(
            """{"fs":{"type":"local","command":["npx"],"experimentalFlag":true}}""");

        vm.Servers[0].WorkingDirectory = "/srv";

        StringAssert.Contains(
            CurrencyText.Render(vm.ToValue()),
            "experimentalFlag:true",
            "An unsurfaced field must survive an edit to a neighbouring field on the same entry — "
            + "otherwise the first save silently strips it.");
    }
}
