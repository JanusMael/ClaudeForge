using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.OpenCode.Sdk.Mcp;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// The <c>mcp</c> union: three declared arms, one sub-union, and the requirement that nothing the
/// codec fails to understand is lost.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>The schema has THREE arms, not the two the plan describes.</b> Alongside
/// <c>McpLocalConfig</c> and <c>McpRemoteConfig</c> there is an inline
/// <c>{ "enabled": boolean }</c> with <c>required: ["enabled"]</c> and
/// <c>additionalProperties: false</c> — a toggle for a server another scope declares, without
/// restating it. An editor built to the plan's description would have classified every one of
/// those as unparseable.
/// </para>
/// <para>
/// Most tests here are round trips through the value currency rather than assertions about model
/// fields, because the property that matters to a user is that their file survives being opened.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeMcpCodecTests
{
    private static object? RoundTrip(string json) =>
        OpenCodeMcpCodec.WriteMap(OpenCodeMcpCodec.ReadMap(CurrencyText.Parse(json)));

    private static void AssertRoundTrips(string json, string because)
    {
        Assert.AreEqual(
            CurrencyText.Render(CurrencyText.Parse(json)),
            CurrencyText.Render(RoundTrip(json)),
            because);
    }

    // ── Round trips ──────────────────────────────────────────────────────────

    [TestMethod]
    public void ALocalServer_RoundTripsEveryField()
    {
        AssertRoundTrips(
            """
            {"fs":{"type":"local","command":["npx","-y","@mcp/fs","--root","/srv"],
            "cwd":"/srv","environment":{"TOKEN":"abc","LOG":"debug"},
            "enabled":true,"timeout":15000}}
            """,
            "Every local field is surfaced, so nothing should need the opaque path.");
    }

    [TestMethod]
    public void ARemoteServer_RoundTripsEveryField_IncludingOAuth()
    {
        AssertRoundTrips(
            """
            {"api":{"type":"remote","url":"https://mcp.example.com/sse","enabled":false,
            "headers":{"X-Tenant":"acme","Accept":"text/event-stream"},
            "oauth":{"clientId":"cid","clientSecret":"shh","scope":"read write",
            "callbackPort":20000,"redirectUri":"http://127.0.0.1:20000/cb"},
            "timeout":30000}}
            """,
            "The OAuth sub-object is surfaced field-for-field.");
    }

    [TestMethod]
    public void AnEnabledOnlyEntry_IsItsOwnArm_AndRoundTrips()
    {
        object? value = CurrencyText.Parse("""{"shared":{"enabled":false}}""");
        var servers = OpenCodeMcpCodec.ReadMap(value);

        Assert.AreEqual(
            OpenCodeMcpKind.EnabledOverride,
            servers.Single().Value.Kind,
            "The schema's third arm toggles a server declared in another scope. Reading it as "
            + "unrecognised would make the commonest project-level override uneditable.");
        Assert.AreEqual(
            CurrencyText.Render(value),
            CurrencyText.Render(OpenCodeMcpCodec.WriteMap(servers)));
    }

    /// <remarks>
    /// Three states, not two: an object means OAuth on with those settings, literal <c>false</c>
    /// means auto-detection off, and an absent key means auto-detect. The schema permits only
    /// <c>false</c> — never <c>true</c> — so a nullable bool cannot carry this.
    /// </remarks>
    [TestMethod]
    public void OAuthFalse_IsDistinctFromOAuthAbsentAndFromAnEmptyObject()
    {
        var disabled = OpenCodeMcpCodec.ReadServer(CurrencyText.Parse(
            """{"type":"remote","url":"https://x","oauth":false}""".Replace("\"mcp\":", "")));
        Assert.IsTrue(disabled.OAuthDisabled);
        Assert.IsNull(disabled.OAuth);

        var absent = OpenCodeMcpCodec.ReadServer(
            CurrencyText.Parse("""{"type":"remote","url":"https://x"}"""));
        Assert.IsFalse(absent.OAuthDisabled);
        Assert.IsNull(absent.OAuth);

        var empty = OpenCodeMcpCodec.ReadServer(
            CurrencyText.Parse("""{"type":"remote","url":"https://x","oauth":{}}"""));
        Assert.IsFalse(empty.OAuthDisabled);
        Assert.IsNotNull(empty.OAuth, "`oauth: {}` means on-with-defaults, which is not absent.");

        AssertRoundTrips(
            """{"a":{"type":"remote","url":"https://x","oauth":false}}""",
            "Disabled must not come back as absent.");
        AssertRoundTrips(
            """{"a":{"type":"remote","url":"https://x","oauth":{}}}""",
            "On-with-defaults must not come back as absent either.");
        AssertRoundTrips(
            """{"a":{"type":"remote","url":"https://x"}}""",
            "And absent must stay absent rather than acquiring an oauth key.");
    }

    // ── Preservation: the behaviour the plan asked for ───────────────────────

    /// <remarks>
    /// ⭐ The forward-compatibility case. A <c>type</c> this build has never heard of is held
    /// verbatim rather than guessed at or dropped — which is what the plan wanted and what the
    /// template it recommended does <b>not</b> do.
    /// </remarks>
    [TestMethod]
    public void AnUnknownTypeIsHeldVerbatimAndWrittenBackUnchanged()
    {
        const string json =
            """{"future":{"type":"websocket","endpoint":"wss://x","retries":3,"nested":{"a":[1,2]}}}""";

        var servers = OpenCodeMcpCodec.ReadMap(CurrencyText.Parse(json));

        Assert.AreEqual(OpenCodeMcpKind.Unrecognised, servers.Single().Value.Kind);
        AssertRoundTrips(
            json,
            "A server OpenCode understands and this build does not must survive a save. Dropping "
            + "it deletes a working server; guessing at it rewrites one.");
    }

    /// <remarks>
    /// ⭐ Per-entry granularity, which is the improvement over echoing the whole value. One entry
    /// from a newer OpenCode must not make the other twelve read-only.
    /// </remarks>
    [TestMethod]
    public void AnUnknownServerDoesNotStopItsNeighboursFromBeingEdited()
    {
        var servers = OpenCodeMcpCodec.ReadMap(CurrencyText.Parse(
            """
            {"future":{"type":"websocket","endpoint":"wss://x"},
             "fs":{"type":"local","command":["npx","fs"]},
             "api":{"type":"remote","url":"https://y"}}
            """));

        Assert.AreEqual(3, servers.Count);
        Assert.AreEqual(OpenCodeMcpKind.Unrecognised, servers[0].Value.Kind);
        Assert.AreEqual(OpenCodeMcpKind.Local, servers[1].Value.Kind);
        Assert.AreEqual(OpenCodeMcpKind.Remote, servers[2].Value.Kind);

        // Editing a neighbour must not disturb the opaque one.
        var edited = servers
            .Select(s => s.Key == "fs"
                ? new KeyValuePair<string, OpenCodeMcpServer>(
                    s.Key,
                    s.Value with { Command = ["npx", "fs", "--root", "/tmp"] })
                : s)
            .ToList();

        string written = CurrencyText.Render(OpenCodeMcpCodec.WriteMap(edited));
        StringAssert.Contains(written, "wss://x", "The opaque entry must still be there.");
        StringAssert.Contains(written, "--root");
    }

    [TestMethod]
    public void FieldsTheModelDoesNotSurface_SurviveOnARecognisedServer()
    {
        AssertRoundTrips(
            """{"fs":{"type":"local","command":["x"],"experimentalFlag":true,"note":"keep me"}}""",
            "The schema says additionalProperties:false, but a config written by a newer OpenCode "
            + "is exactly when an editor must not strip what it has not heard of.");
    }

    [TestMethod]
    public void AnEntryThatIsNotEvenAnObject_IsHeldVerbatim()
    {
        AssertRoundTrips(
            """{"weird":"just a string","alsoWeird":[1,2,3],"nope":null}""",
            "Nothing about these is editable, and nothing about them should be destroyed.");
    }

    // ── Order ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ServerOrderAndEnvironmentOrderArePreserved()
    {
        const string json =
            """
            {"z":{"type":"local","command":["a"],"environment":{"ZZ":"1","AA":"2","MM":"3"}},
             "a":{"type":"local","command":["b"]}}
            """;

        object? written = RoundTrip(json);

        CollectionAssert.AreEqual(
            new[] { "z", "a" },
            ((IReadOnlyDictionary<string, object?>)written!).Keys.ToArray(),
            "Servers keep file order. Alphabetising a user's config turns a one-line change into "
            + "a whole-file diff.");

        var z = (IReadOnlyDictionary<string, object?>)
            ((IReadOnlyDictionary<string, object?>)written)["z"]!;
        CollectionAssert.AreEqual(
            new[] { "ZZ", "AA", "MM" },
            ((IReadOnlyDictionary<string, object?>)z["environment"]!).Keys.ToArray(),
            "Environment order too — this is the map big enough for a reshuffle to hurt.");
    }

    /// <remarks>
    /// ⚠ Asserts the TYPE at every level. Every order assertion above stays green against a plain
    /// <see cref="Dictionary{TKey,TValue}"/>, which enumerates in insertion order in practice and
    /// promises nothing. A canary on the permission grid proved that the hard way: swapping only
    /// the inner map left all 24 of its tests passing.
    /// </remarks>
    [TestMethod]
    public void EveryEmittedMapIsAnOrderedMap_AtEveryLevel()
    {
        object? written = RoundTrip(
            """
            {"a":{"type":"remote","url":"https://x","headers":{"H":"1"},
            "oauth":{"clientId":"c"}}}
            """);

        Assert.IsInstanceOfType<OrderedPropertyMap>(written, "the server map");

        var server = (IReadOnlyDictionary<string, object?>)
            ((IReadOnlyDictionary<string, object?>)written!)["a"]!;
        Assert.IsInstanceOfType<OrderedPropertyMap>(server, "one server's fields");
        Assert.IsInstanceOfType<OrderedPropertyMap>(server["headers"], "headers");
        Assert.IsInstanceOfType<OrderedPropertyMap>(server["oauth"], "oauth");
    }

    // ── Permissive reads, and removal ────────────────────────────────────────

    /// <remarks>
    /// <c>command</c> is required, but an entry can legitimately be mid-edit or hand-broken.
    /// Reading it as unrecognised would make the one field the user needs to repair the one field
    /// they cannot reach.
    /// </remarks>
    [TestMethod]
    public void ALocalServerMissingItsCommand_IsStillReadAsLocal()
    {
        var server = OpenCodeMcpCodec.ReadServer(
            CurrencyText.Parse("""{"type":"local","cwd":"/srv"}"""));

        Assert.AreEqual(OpenCodeMcpKind.Local, server.Kind);
        Assert.AreEqual(0, server.Command.Count);
        Assert.AreEqual("/srv", server.WorkingDirectory);
    }

    [TestMethod]
    public void ATimeoutThatArrivedAsAWholeDouble_IsStillRead()
    {
        // JSON numbers do not always survive as integers through a currency conversion.
        var server = OpenCodeMcpCodec.ReadServer(CurrencyText.Map([
            new KeyValuePair<string, object?>("type", "local"),
            new KeyValuePair<string, object?>("command", new List<object?> { "x" }),
            new KeyValuePair<string, object?>("timeout", 5000.0),
        ]));

        Assert.AreEqual(5000L, server.TimeoutMs, "Refusing this silently blanks the user's timeout.");
    }

    [TestMethod]
    public void WriteMap_IsNullWhenThereAreNoServers()
    {
        Assert.IsNull(
            OpenCodeMcpCodec.WriteMap([]),
            "An empty object would persist an mcp key that configures nothing; null tells the "
            + "workspace to remove it.");
    }

    [TestMethod]
    public void WriteMap_SkipsABlankServerName()
    {
        object? written = OpenCodeMcpCodec.WriteMap([
            new KeyValuePair<string, OpenCodeMcpServer>("  ", new OpenCodeMcpServer
            {
                Kind = OpenCodeMcpKind.Local,
                Command = ["x"],
            }),
        ]);

        Assert.IsNull(written, "A half-added row is not yet a server.");
    }

    [TestMethod]
    public void ReadMap_TreatsANonObjectValueAsNothingToEdit()
    {
        Assert.AreEqual(0, OpenCodeMcpCodec.ReadMap(null).Count);
        Assert.AreEqual(0, OpenCodeMcpCodec.ReadMap("nonsense").Count);
    }

    /// <remarks>
    /// An empty <c>cwd</c> is not the same as no <c>cwd</c>: OpenCode resolves the empty string to
    /// the workspace root, so persisting one turns a cleared textbox into an actual setting.
    /// </remarks>
    [TestMethod]
    public void AClearedTextBoxIsNotWrittenAsAnEmptyString()
    {
        object? written = OpenCodeMcpCodec.WriteServer(new OpenCodeMcpServer
        {
            Kind = OpenCodeMcpKind.Local,
            Command = ["x"],
            WorkingDirectory = "   ",
        });

        Assert.IsFalse(
            ((IReadOnlyDictionary<string, object?>)written!).ContainsKey("cwd"),
            "Whitespace means the user cleared the box, not that they chose the workspace root.");
    }
}
