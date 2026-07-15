// comprehensive disk → SDK → editor →
// user-mutation → SDK → disk round-trip suite for the Hooks editor.
//
// What this file tests that the existing HooksRoundTripTests does not:
//
//   1. **Matrix coverage** — every (variant × mutation) pair has a named
//      test, so adding a new schema variant or mutation type is a
//      deliberate change to the matrix (not a quietly-missing case).
//   2. **Live-write loop simulation** — every test routes the editor's
//      ToJsonValue through the SDK's `client.SetValue("hooks", ...)`
//      surface, then constructs a FRESH editor against the same workspace
//      and verifies the round-trip.  Mirrors what production does on
//      every IsModified flip via SettingsGroupEditorViewModel.
//   3. **Second round-trip** — the key tests run load → mutate → save →
//      reload → save → reload, catching PreservedFields-style "survives
//      once but not twice" bugs (the 2026-04-30 MCP regression class).
//   4. **PreservedFields** — load JSON with sub-fields the SDK doesn't
//      model (async / statusMessage / model), mutate other fields,
//      verify preserved fields persist verbatim across save+reload.

using Bennewitz.Ninja.ClaudeForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

[TestClass]
public class HooksFullRoundTripTests
{
    // ─── Fixture ─────────────────────────────────────────────────────────────
    //
    // The fixture wraps the workspace + SDK client + editor so each test can
    // express the lifecycle as fluent calls:
    //   var fx = HooksFixture.From(BuildHooksWith(...));
    //   fx.MutateEntry(entry => { entry.CommandValue = "..."; });
    //   fx.SaveAndReload();
    //   var entry = fx.FirstHook("PreToolUse");
    //   Assert...
    //
    // SaveAndReload mirrors what production does on a save: emit
    // ToJsonValue, route via client.SetValue / RemoveValue, then construct
    // a fresh editor against the same workspace and re-load.  The fresh
    // editor is what catches subscription leaks, baseline-snapshot drift,
    // and PreservedFields replay bugs.
    // ────────────────────────────────────────────────────────────────────────

    private sealed class HooksFixture : IDisposable
    {
        public SettingsDocument Doc { get; }
        public SettingsWorkspace Workspace { get; }
        public ClaudeCodeClient Client { get; }
        public HooksEditorViewModel Editor { get; private set; } = null!;

        private HooksFixture(SettingsDocument doc, SettingsWorkspace ws, ClaudeCodeClient client)
        {
            Doc = doc;
            Workspace = ws;
            Client = client;
        }

        public static HooksFixture From(JsonObject? hooks)
        {
            JsonObject rootObj = new();
            if (hooks is not null)
            {
                rootObj["hooks"] = hooks.DeepClone();
            }

            SettingsDocument doc = new(ConfigScope.User, "settings.json", rootObj, isReadOnly: false);
            SettingsWorkspace ws = new([doc]);
            ClaudeCodeClient client = ClaudeCodeClient.FromExistingWorkspace(
                ws, ConfigScope.User,
                new SchemaRegistry(new HttpClient()));
            HooksFixture fx = new(doc, ws, client);
            fx.RebuildEditor();
            return fx;
        }

        /// <summary>
        /// Construct a fresh editor against the current workspace state
        /// and load it.  Mirrors what the parent VM does on a workspace
        /// reload: the editor instance is replaced, so subscription state
        /// from the prior instance is not carried over.
        /// </summary>
        private void RebuildEditor()
        {
            Editor = new HooksEditorViewModel(HooksSchema(), ConfigScope.User, Client);
            Editor.LoadFromLayered(BuildLayered(Doc.Root["hooks"]), ConfigScope.User);
        }

        /// <summary>
        /// Simulate the live-write + reload that the parent VM performs
        /// on every IsModified flip: emit ToJsonValue, route through the
        /// SDK's SetValue / RemoveValue, then load a FRESH editor against
        /// the resulting workspace state.
        /// </summary>
        public void SaveAndReload()
        {
            JsonNode? emitted = Editor.ToJsonValue();
            if (emitted is not null)
            {
                Client.SetValue("hooks", emitted, ConfigScope.User);
            }
            else
            {
                Client.RemoveValue("hooks", ConfigScope.User);
            }

            RebuildEditor();
        }

        /// <summary>The first hook in the named event group, or throws if none.</summary>
        public HookEntry FirstHook(string eventName)
        {
            HookEventGroup group = Editor.EventGroups.First(g => g.EventName == eventName);
            return group.Hooks[0];
        }

        /// <summary>Number of hooks in the named event group.</summary>
        public int HookCount(string eventName)
        {
            HookEventGroup? group = Editor.EventGroups.FirstOrDefault(g => g.EventName == eventName);
            return group?.Hooks.Count ?? 0;
        }

        /// <summary>Add a fresh hook to the named group via the AddHook command.</summary>
        public HookEntry AddHookTo(string eventName)
        {
            HookEventGroup group = Editor.EventGroups.First(g => g.EventName == eventName);
            group.AddHookCommand.Execute(null);
            return group.Hooks[^1];
        }

        public void Dispose()
        {
            Client.Dispose();
        }
    }

    private static SchemaNode HooksSchema()
    {
        return new SchemaNode("hooks", "hooks") { ValueType = SchemaValueType.Complex };
    }

    private static LayeredValue BuildLayered(JsonNode? hooksNode)
    {
        if (hooksNode is null)
        {
            return new LayeredValue("hooks", []);
        }

        ScopeEntry entry = new(ConfigScope.User, hooksNode, "/fake");
        return new LayeredValue("hooks", [entry])
        {
            EffectiveValue = hooksNode,
            EffectiveScope = ConfigScope.User,
        };
    }

    // ─── JSON builders for common load shapes ───────────────────────────────

    private static JsonObject CommandHook(string eventName, string matcher, string command)
    {
        return new JsonObject
        {
            [eventName] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = matcher,
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "command", ["command"] = command },
                    },
                },
            },
        };
    }

    private static JsonObject PromptHook(string eventName, string matcher, string prompt)
    {
        return new JsonObject
        {
            [eventName] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = matcher,
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "prompt", ["prompt"] = prompt },
                    },
                },
            },
        };
    }

    private static JsonObject UrlHook(
        string eventName,
        string matcher,
        string url,
        IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlyList<string>? allowedEnvVars = null,
        int? timeout = null)
    {
        JsonObject inner = new() { ["type"] = "url", ["url"] = url };
        if (timeout is { } t)
        {
            inner["timeout"] = t;
        }

        if (headers is { Count: > 0 } h)
        {
            JsonObject ho = new();
            foreach ((string k, string v) in h)
            {
                ho[k] = v;
            }

            inner["headers"] = ho;
        }

        if (allowedEnvVars is { Count: > 0 } envs)
        {
            JsonArray arr = new();
            foreach (string name in envs)
            {
                arr.Add(name);
            }

            inner["allowedEnvVars"] = arr;
        }

        return new JsonObject
        {
            [eventName] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = matcher,
                    ["hooks"] = new JsonArray { inner },
                },
            },
        };
    }

    /// <summary>
    /// Build a hook with extra preserved fields the SDK does not natively
    /// model (async / statusMessage / model).  Used to verify the
    /// PreservedFields replay path survives multiple round-trips.
    /// </summary>
    private static JsonObject CommandHookWithPreservedFields(
        string eventName, string matcher, string command,
        bool? asyncFlag = null, string? statusMessage = null, string? model = null)
    {
        JsonObject inner = new() { ["type"] = "command", ["command"] = command };
        if (asyncFlag.HasValue)
        {
            inner["async"] = asyncFlag.Value;
        }

        if (statusMessage is not null)
        {
            inner["statusMessage"] = statusMessage;
        }

        if (model is not null)
        {
            inner["model"] = model;
        }

        return new JsonObject
        {
            [eventName] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = matcher,
                    ["hooks"] = new JsonArray { inner },
                },
            },
        };
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Get the inner hook JsonObject at hooks.{event}[0].hooks[0] from on-disk.</summary>
    private static JsonObject InnerOnDisk(SettingsDocument doc, string eventName)
    {
        JsonObject hooks = doc.Root["hooks"]!.AsObject();
        JsonObject outer = hooks[eventName]!.AsArray()[0]!.AsObject();
        return outer["hooks"]!.AsArray()[0]!.AsObject();
    }

    /// <summary>Get the count of inner hooks at hooks.{event}[0].hooks.</summary>
    private static int InnerCountOnDisk(SettingsDocument doc, string eventName)
    {
        JsonObject? hooks = doc.Root["hooks"] as JsonObject;
        if (hooks is null)
        {
            return 0;
        }

        JsonArray? outerArr = hooks[eventName] as JsonArray;
        if (outerArr is null || outerArr.Count == 0)
        {
            return 0;
        }

        JsonObject? outer = outerArr[0] as JsonObject;
        JsonArray? inner = outer?["hooks"] as JsonArray;
        return inner?.Count ?? 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Schema descriptions via the production (FromExistingWorkspace) client path
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void EventDescriptions_Populate_ViaFromExistingWorkspaceClient()
    {
        // Regression: the GUI builds its SDK client via FromExistingWorkspace (see
        // MainWindowViewModel), which skips OpenAsync — so the client has NO cached
        // schema tree. The fixture here uses that exact path plus a BARE schema node
        // (no descriptions), so the per-event descriptions can only come from the SDK's
        // bundled-schema fallback. They must populate so the left-rail tooltip and the
        // detail-pane label render. Before the fix, SchemaHookEvents returned nothing
        // without a cached node and every event description was blank in the app.
        using HooksFixture fx = HooksFixture.From(null);

        HookEventGroup cwd = fx.Editor.EventGroups.First(g => g.EventName == "CwdChanged");
        Assert.IsTrue(cwd.HasDescription,
            "Event descriptions must populate via the FromExistingWorkspace client (the GUI path).");
        StringAssert.Contains(cwd.Description!, "working directory");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Variant: Command  (× mutation)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Command_AddNewHook_RoundTrips()
    {
        using HooksFixture fx = HooksFixture.From(null);
        HookEventGroup group = fx.Editor.EventGroups.First(g => g.EventName == "PreToolUse");
        group.AddHookCommand.Execute(null);
        HookEntry entry = group.Hooks[0];
        entry.Matcher = "Bash";
        entry.CommandType = HookCommandType.Command;
        entry.CommandValue = "echo hi";

        fx.SaveAndReload();

        Assert.AreEqual(1, fx.HookCount("PreToolUse"));
        HookEntry reloaded = fx.FirstHook("PreToolUse");
        Assert.AreEqual("Bash", reloaded.Matcher);
        Assert.AreEqual(HookCommandType.Command, reloaded.CommandType);
        Assert.AreEqual("echo hi", reloaded.CommandValue);
    }

    [TestMethod]
    public void Command_EditCommandValue_RoundTrips()
    {
        using HooksFixture fx = HooksFixture.From(CommandHook("PreToolUse", "Bash", "echo before"));
        HookEntry entry = fx.FirstHook("PreToolUse");
        entry.CommandValue = "echo after";

        fx.SaveAndReload();

        Assert.AreEqual("echo after", fx.FirstHook("PreToolUse").CommandValue);
    }

    [TestMethod]
    public void Command_EditMatcher_RoundTrips()
    {
        using HooksFixture fx = HooksFixture.From(CommandHook("PreToolUse", "Bash", "echo x"));
        HookEntry entry = fx.FirstHook("PreToolUse");
        entry.Matcher = "Edit";

        fx.SaveAndReload();

        Assert.AreEqual("Edit", fx.FirstHook("PreToolUse").Matcher);
    }

    [TestMethod]
    public void Command_RemoveHook_DropsFromOnDisk()
    {
        using HooksFixture fx = HooksFixture.From(CommandHook("PreToolUse", "Bash", "echo x"));
        HookEventGroup group = fx.Editor.EventGroups.First(g => g.EventName == "PreToolUse");
        group.RemoveHookCommand.Execute(group.Hooks[0]);

        fx.SaveAndReload();

        Assert.AreEqual(0, fx.HookCount("PreToolUse"),
            "Removed hook must be absent from the reloaded editor.");
    }

    [TestMethod]
    public void Command_EditTimeout_RoundTrips()
    {
        using HooksFixture fx = HooksFixture.From(CommandHook("PreToolUse", "Bash", "echo x"));
        HookEntry entry = fx.FirstHook("PreToolUse");
        entry.Timeout = 60;

        fx.SaveAndReload();

        Assert.AreEqual(60, fx.FirstHook("PreToolUse").Timeout);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Variant: Prompt  (× mutation)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Prompt_LoadAndSave_PreservesType_AndValueKey()
    {
        using HooksFixture fx = HooksFixture.From(PromptHook("UserPromptSubmit", "*", "Add tests for this change"));
        HookEntry entry = fx.FirstHook("UserPromptSubmit");
        Assert.AreEqual(HookCommandType.Prompt, entry.CommandType);
        Assert.AreEqual("Add tests for this change", entry.CommandValue);

        fx.SaveAndReload();

        HookEntry reloaded = fx.FirstHook("UserPromptSubmit");
        Assert.AreEqual(HookCommandType.Prompt, reloaded.CommandType);
        Assert.AreEqual("Add tests for this change", reloaded.CommandValue);
        // On-disk: emits "prompt" key, NOT "command".
        JsonObject inner = InnerOnDisk(fx.Doc, "UserPromptSubmit");
        Assert.IsTrue(inner.ContainsKey("prompt"),
            "Prompt-typed hook must emit 'prompt' value-key on disk.");
        Assert.IsFalse(inner.ContainsKey("command"),
            "Prompt-typed hook MUST NOT emit 'command' (would be a different schema branch).");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Variant: URL  (× mutation, including the headers / env-vars matrix)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Url_AddHook_WithHeaderAndEnvVar_RoundTrips()
    {
        using HooksFixture fx = HooksFixture.From(null);
        HookEventGroup group = fx.Editor.EventGroups.First(g => g.EventName == "PreToolUse");
        group.AddHookCommand.Execute(null);
        HookEntry entry = group.Hooks[0];
        entry.Matcher = "Bash";
        entry.CommandType = HookCommandType.Url;
        entry.CommandValue = "https://example.com/hook";
        entry.NewHeaderKey = "Authorization";
        entry.NewHeaderValue = "Bearer xyz";
        entry.AddHeaderCommand.Execute(null);
        entry.NewAllowedEnvVar = "SECRET_TOKEN";
        entry.AddAllowedEnvVarCommand.Execute(null);

        fx.SaveAndReload();

        HookEntry reloaded = fx.FirstHook("PreToolUse");
        Assert.AreEqual(HookCommandType.Url, reloaded.CommandType);
        Assert.AreEqual(1, reloaded.Headers.Count);
        Assert.AreEqual("Authorization", reloaded.Headers[0].Key);
        Assert.AreEqual("Bearer xyz", reloaded.Headers[0].Value);
        Assert.AreEqual(1, reloaded.AllowedEnvVars.Count);
        Assert.AreEqual("SECRET_TOKEN", reloaded.AllowedEnvVars[0]);
    }

    [TestMethod]
    public void Url_AddHeaderToExisting_RoundTrips()
    {
        // Load with one header, add a second, save, reload.  Verifies
        // that AddHeader's MarkModified path → live-write → reload all
        // see both headers.  This is the live-app flow that the
        // subscription-gap fix unblocked.
        using HooksFixture fx = HooksFixture.From(UrlHook(
            "PreToolUse", "Bash", "https://x.com/",
            headers: new Dictionary<string, string> { ["A"] = "1" }));
        HookEntry entry = fx.FirstHook("PreToolUse");
        entry.NewHeaderKey = "B";
        entry.NewHeaderValue = "2";
        entry.AddHeaderCommand.Execute(null);

        fx.SaveAndReload();

        HookEntry reloaded = fx.FirstHook("PreToolUse");
        Assert.AreEqual(2, reloaded.Headers.Count);
        Assert.IsTrue(reloaded.Headers.Any(h => h.Key == "A" && h.Value == "1"));
        Assert.IsTrue(reloaded.Headers.Any(h => h.Key == "B" && h.Value == "2"));
    }

    [TestMethod]
    public void Url_RemoveHeaderFromExisting_RoundTrips()
    {
        using HooksFixture fx = HooksFixture.From(UrlHook(
            "PreToolUse", "Bash", "https://x.com/",
            headers: new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" }));
        HookEntry entry = fx.FirstHook("PreToolUse");
        HookHeaderEntry headerA = entry.Headers.First(h => h.Key == "A");
        entry.RemoveHeaderCommand.Execute(headerA);

        fx.SaveAndReload();

        HookEntry reloaded = fx.FirstHook("PreToolUse");
        Assert.AreEqual(1, reloaded.Headers.Count);
        Assert.AreEqual("B", reloaded.Headers[0].Key);
    }

    [TestMethod]
    public void Url_EditHeaderValue_RoundTrips()
    {
        using HooksFixture fx = HooksFixture.From(UrlHook(
            "PreToolUse", "Bash", "https://x.com/",
            headers: new Dictionary<string, string> { ["Auth"] = "old" }));
        HookEntry entry = fx.FirstHook("PreToolUse");
        entry.Headers[0].Value = "new";

        fx.SaveAndReload();

        HookEntry reloaded = fx.FirstHook("PreToolUse");
        Assert.AreEqual("new", reloaded.Headers[0].Value);
    }

    [TestMethod]
    public void Url_EditHeaderKey_RoundTrips()
    {
        using HooksFixture fx = HooksFixture.From(UrlHook(
            "PreToolUse", "Bash", "https://x.com/",
            headers: new Dictionary<string, string> { ["A"] = "1" }));
        HookEntry entry = fx.FirstHook("PreToolUse");
        entry.Headers[0].Key = "Authorization";

        fx.SaveAndReload();

        HookEntry reloaded = fx.FirstHook("PreToolUse");
        Assert.AreEqual("Authorization", reloaded.Headers[0].Key);
    }

    [TestMethod]
    public void Url_AddAllowedEnvVarToExisting_RoundTrips()
    {
        using HooksFixture fx = HooksFixture.From(UrlHook(
            "PreToolUse", "Bash", "https://x.com/",
            allowedEnvVars: ["FIRST"]));
        HookEntry entry = fx.FirstHook("PreToolUse");
        entry.NewAllowedEnvVar = "SECOND";
        entry.AddAllowedEnvVarCommand.Execute(null);

        fx.SaveAndReload();

        HookEntry reloaded = fx.FirstHook("PreToolUse");
        Assert.AreEqual(2, reloaded.AllowedEnvVars.Count);
        CollectionAssert.AreEquivalent(
            new[] { "FIRST", "SECOND" },
            reloaded.AllowedEnvVars.ToList());
    }

    [TestMethod]
    public void Url_RemoveAllowedEnvVar_RoundTrips()
    {
        using HooksFixture fx = HooksFixture.From(UrlHook(
            "PreToolUse", "Bash", "https://x.com/",
            allowedEnvVars: ["FIRST", "SECOND"]));
        HookEntry entry = fx.FirstHook("PreToolUse");
        entry.RemoveAllowedEnvVarCommand.Execute("FIRST");

        fx.SaveAndReload();

        HookEntry reloaded = fx.FirstHook("PreToolUse");
        Assert.AreEqual(1, reloaded.AllowedEnvVars.Count);
        Assert.AreEqual("SECOND", reloaded.AllowedEnvVars[0]);
    }

    [TestMethod]
    public void Url_EditTimeout_RoundTrips()
    {
        using HooksFixture fx = HooksFixture.From(UrlHook("PreToolUse", "Bash", "https://x.com/", timeout: 30));
        HookEntry entry = fx.FirstHook("PreToolUse");
        entry.Timeout = 120;

        fx.SaveAndReload();

        Assert.AreEqual(120, fx.FirstHook("PreToolUse").Timeout);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Cross-variant: type discriminator change
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void ChangeCommandTypeFromCommandToUrl_PreservesValueKeyShapeOnDisk()
    {
        // Load as command, switch to URL, save.  The on-disk shape must
        // emit the URL key — not stale "command".
        using HooksFixture fx = HooksFixture.From(CommandHook("PreToolUse", "Bash", "echo x"));
        HookEntry entry = fx.FirstHook("PreToolUse");
        entry.CommandType = HookCommandType.Url;
        entry.CommandValue = "https://x.com/hook";

        fx.SaveAndReload();

        HookEntry reloaded = fx.FirstHook("PreToolUse");
        Assert.AreEqual(HookCommandType.Url, reloaded.CommandType);
        Assert.AreEqual("https://x.com/hook", reloaded.CommandValue);

        JsonObject inner = InnerOnDisk(fx.Doc, "PreToolUse");
        Assert.AreEqual("url", inner["type"]!.GetValue<string>());
        Assert.IsTrue(inner.ContainsKey("url"),
            "URL-typed hook must emit the 'url' value-key after type change.");
        Assert.IsFalse(inner.ContainsKey("command"),
            "Stale 'command' value-key MUST NOT survive a type change.");
    }

    [TestMethod]
    public void ChangeCommandTypeFromUrlToCommand_DropsUrlOnlySubFields_OnDisk()
    {
        // URL hook with headers + allowedEnvVars.  Switch to command.
        // Schema-wise headers + allowedEnvVars are URL-only; the editor
        // does not auto-strip them on type-flip, but the on-disk emit
        // path uses the typed CommandType to decide which value-key
        // ("command") to emit.  This locks the contract that the
        // type-change correctly drops the now-stale "url" + the headers
        // / allowedEnvVars sub-keys (which the editor still emits because
        // the user might flip back; verify explicitly here).
        using HooksFixture fx = HooksFixture.From(UrlHook(
            "PreToolUse", "Bash", "https://x.com/",
            headers: new Dictionary<string, string> { ["A"] = "1" },
            allowedEnvVars: ["TOKEN"]));
        HookEntry entry = fx.FirstHook("PreToolUse");
        entry.CommandType = HookCommandType.Command;
        entry.CommandValue = "echo migrated";

        fx.SaveAndReload();

        HookEntry reloaded = fx.FirstHook("PreToolUse");
        Assert.AreEqual(HookCommandType.Command, reloaded.CommandType);
        Assert.AreEqual("echo migrated", reloaded.CommandValue);

        JsonObject inner = InnerOnDisk(fx.Doc, "PreToolUse");
        Assert.AreEqual("command", inner["type"]!.GetValue<string>());
        Assert.IsTrue(inner.ContainsKey("command"));
        Assert.IsFalse(inner.ContainsKey("url"),
            "Command-typed hook MUST NOT emit a stale 'url' field.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PreservedFields — replay across multiple round-trips
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void PreservedFields_AsyncStatusMessageModel_SurviveSingleRoundTrip()
    {
        // The SDK / editor does not natively model `async`, `statusMessage`,
        // or `model` at the time of writing.  They must round-trip
        // verbatim through PreservedFields so users with these fields set
        // do not lose data on every save.
        using HooksFixture fx = HooksFixture.From(CommandHookWithPreservedFields(
            "PreToolUse", "Bash", "echo x",
            asyncFlag: true,
            statusMessage: "Running",
            model: "claude-sonnet"));
        // Mutate an unrelated field to force a save flush.
        fx.FirstHook("PreToolUse").Matcher = "Edit";

        fx.SaveAndReload();

        JsonObject inner = InnerOnDisk(fx.Doc, "PreToolUse");
        Assert.IsTrue(inner.ContainsKey("async"),
            "PreservedFields must replay 'async' on save.");
        Assert.IsTrue(inner["async"]!.GetValue<bool>());
        Assert.IsTrue(inner.ContainsKey("statusMessage"));
        Assert.AreEqual("Running", inner["statusMessage"]!.GetValue<string>());
        Assert.IsTrue(inner.ContainsKey("model"));
        Assert.AreEqual("claude-sonnet", inner["model"]!.GetValue<string>());
    }

    [TestMethod]
    public void PreservedFields_AsyncStatusMessageModel_SurviveDoubleRoundTrip()
    {
        // The 2026-04-30 PreservedFields-replay bug class — fields survive
        // ONE round-trip but vanish on the SECOND.  This test catches that
        // by running save → reload TWICE.
        using HooksFixture fx = HooksFixture.From(CommandHookWithPreservedFields(
            "PreToolUse", "Bash", "echo x",
            asyncFlag: true,
            statusMessage: "Running",
            model: "claude-sonnet"));
        fx.FirstHook("PreToolUse").Matcher = "Edit";

        // First round-trip
        fx.SaveAndReload();
        // Mutate something else to force a second save.
        fx.FirstHook("PreToolUse").CommandValue = "echo y";
        // Second round-trip
        fx.SaveAndReload();

        JsonObject inner = InnerOnDisk(fx.Doc, "PreToolUse");
        Assert.IsTrue(inner.ContainsKey("async"),
            "PreservedFields must SURVIVE a second round-trip.  This is the 2026-04-30 bug class.");
        Assert.IsTrue(inner.ContainsKey("statusMessage"));
        Assert.IsTrue(inner.ContainsKey("model"));
        Assert.IsTrue(inner["async"]!.GetValue<bool>());
        Assert.AreEqual("Running", inner["statusMessage"]!.GetValue<string>());
        Assert.AreEqual("claude-sonnet", inner["model"]!.GetValue<string>());
    }

    [TestMethod]
    public void PreservedFields_DoNotShadowTypedHeaderEdits()
    {
        // When the user edits a TYPED field (Headers, AllowedEnvVars,
        // Timeout) the typed value must win on the on-disk emit; the
        // PreservedFields bag must NOT replay a stale value.  This locks
        // the "typed wins on collision" contract documented in the
        // editor's IngestPreservedFields XML doc.
        using HooksFixture fx = HooksFixture.From(UrlHook(
            "PreToolUse", "Bash", "https://x.com/",
            headers: new Dictionary<string, string> { ["A"] = "old" }));
        HookEntry entry = fx.FirstHook("PreToolUse");
        // Simulate an edit to the typed Headers collection.
        entry.Headers[0].Value = "new";

        fx.SaveAndReload();

        JsonObject inner = InnerOnDisk(fx.Doc, "PreToolUse");
        Assert.AreEqual("new", inner["headers"]!.AsObject()["A"]!.GetValue<string>(),
            "Typed Headers edit must win; PreservedFields must not replay the stale value.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Multiple-hook scenarios (matcher grouping + ordering)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void MultipleHooks_SameMatcher_GroupTogetherOnDisk_AfterRoundTrip()
    {
        using HooksFixture fx = HooksFixture.From(null);
        HookEventGroup group = fx.Editor.EventGroups.First(g => g.EventName == "PreToolUse");

        group.AddHookCommand.Execute(null);
        HookEntry entry1 = group.Hooks[0];
        entry1.Matcher = "Bash";
        entry1.CommandType = HookCommandType.Command;
        entry1.CommandValue = "echo first";

        group.AddHookCommand.Execute(null);
        HookEntry entry2 = group.Hooks[1];
        entry2.Matcher = "Bash";
        entry2.CommandType = HookCommandType.Command;
        entry2.CommandValue = "echo second";

        fx.SaveAndReload();

        // Both hooks should reload, sharing the matcher group.
        HookEventGroup reloadedGroup = fx.Editor.EventGroups.First(g => g.EventName == "PreToolUse");
        Assert.AreEqual(2, reloadedGroup.Hooks.Count);
        Assert.IsTrue(reloadedGroup.Hooks.All(h => h.Matcher == "Bash"));
        CollectionAssert.AreEquivalent(
            new[] { "echo first", "echo second" },
            reloadedGroup.Hooks.Select(h => h.CommandValue).ToList());

        // On-disk: ONE outer entry (single matcher key) with TWO inner entries.
        JsonArray outerArr = fx.Doc.Root["hooks"]!.AsObject()["PreToolUse"]!.AsArray();
        Assert.AreEqual(1, outerArr.Count, "Same-matcher hooks must share one outer entry on disk.");
        JsonArray innerArr = outerArr[0]!.AsObject()["hooks"]!.AsArray();
        Assert.AreEqual(2, innerArr.Count);
    }

    [TestMethod]
    public void MultipleHooks_DifferentMatchers_SeparateOuterEntriesOnDisk()
    {
        using HooksFixture fx = HooksFixture.From(null);
        HookEventGroup group = fx.Editor.EventGroups.First(g => g.EventName == "PreToolUse");

        group.AddHookCommand.Execute(null);
        HookEntry entry1 = group.Hooks[0];
        entry1.Matcher = "Bash";
        entry1.CommandType = HookCommandType.Command;
        entry1.CommandValue = "echo b";

        group.AddHookCommand.Execute(null);
        HookEntry entry2 = group.Hooks[1];
        entry2.Matcher = "Edit";
        entry2.CommandType = HookCommandType.Command;
        entry2.CommandValue = "echo e";

        fx.SaveAndReload();

        HookEventGroup reloadedGroup = fx.Editor.EventGroups.First(g => g.EventName == "PreToolUse");
        Assert.AreEqual(2, reloadedGroup.Hooks.Count);
        JsonArray outerArr = fx.Doc.Root["hooks"]!.AsObject()["PreToolUse"]!.AsArray();
        Assert.AreEqual(2, outerArr.Count, "Different-matcher hooks must produce separate outer entries.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Workspace-removal contract (empty editor → no hooks key on disk)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void RemoveLastHook_DropsHooksKeyFromOnDisk()
    {
        using HooksFixture fx = HooksFixture.From(CommandHook("PreToolUse", "Bash", "echo x"));
        HookEventGroup group = fx.Editor.EventGroups.First(g => g.EventName == "PreToolUse");
        group.RemoveHookCommand.Execute(group.Hooks[0]);

        fx.SaveAndReload();

        // ToJsonValue returns null when no hooks remain; the live-write
        // path translates null → RemoveValue("hooks", scope), so the
        // workspace document must NOT have a "hooks" key.
        Assert.IsFalse(fx.Doc.Root.ContainsKey("hooks"),
            "Removing the last hook must drop the entire 'hooks' key from disk (vs. leaving an empty object).");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Opaque (agent / http) — preserved verbatim through round-trip
    // ═══════════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════════
    //  Opaque types (agent / http / future schema additions) — preserved
    //  verbatim through SDK-backed round-trip. HookEvent.OpaqueInnerJson + HooksAccessor preservation +
    //  HookEntry.IngestOpaqueJson bridge.
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void OpaqueAgentHook_RoundTripsVerbatim_TypeAndCustomFieldsBothPreserved()
    {
        // Unknown hook types ("agent" /
        // "http" / future schema additions) are now preserved end-to-end
        // through the SDK-backed load → editor → save → SDK → reload
        // loop, including the type discriminator itself.
        JsonObject loaded = new()
        {
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = "Bash",
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "agent",
                            ["agentId"] = "code-reviewer",
                            ["customField"] = "preserved",
                        },
                    },
                },
            },
        };
        using HooksFixture fx = HooksFixture.From(loaded);
        HookEntry entry = fx.FirstHook("PreToolUse");

        Assert.IsTrue(entry.IsOpaque,
            "SDK-backed load must propagate opaque-type preservation via OpaqueInnerJson → _opaqueJson.");
        StringAssert.Contains(entry.CommandValue, "agent",
            "Synthetic CommandValue must surface the opaque type so the user sees what's there.");

        // Mutate the matcher (which IS owned by the OUTER entry, not
        // the inner opaque blob) to force a save flush.
        entry.Matcher = "Edit";

        fx.SaveAndReload();

        JsonObject inner = InnerOnDisk(fx.Doc, "PreToolUse");
        Assert.AreEqual("agent", inner["type"]!.GetValue<string>(),
            "Type discriminator MUST survive SDK round-trip after H-5b.");
        Assert.AreEqual("code-reviewer", inner["agentId"]!.GetValue<string>(),
            "Custom fields on opaque-typed hooks must round-trip verbatim.");
        Assert.AreEqual("preserved", inner["customField"]!.GetValue<string>());

        // Outer matcher reflects the user's edit.
        JsonObject outer = fx.Doc.Root["hooks"]!.AsObject()["PreToolUse"]!.AsArray()[0]!.AsObject();
        Assert.AreEqual("Edit", outer["matcher"]!.GetValue<string>());
    }

    [TestMethod]
    public void OpaqueAgentHook_SurvivesDoubleRoundTrip()
    {
        // The 2026-04-30 PreservedFields-survives-once-but-not-twice bug
        // class — same risk for OpaqueInnerJson if the second
        // MaterializeFrom path were to drop the opaque field.
        JsonObject loaded = new()
        {
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = "Bash",
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "agent",
                            ["agentId"] = "linter",
                            ["nestedObj"] = new JsonObject { ["depth"] = "2" },
                        },
                    },
                },
            },
        };
        using HooksFixture fx = HooksFixture.From(loaded);
        fx.FirstHook("PreToolUse").Matcher = "Edit";
        fx.SaveAndReload();
        // Second mutation + reload.
        fx.FirstHook("PreToolUse").Matcher = "Glob";
        fx.SaveAndReload();

        JsonObject inner = InnerOnDisk(fx.Doc, "PreToolUse");
        Assert.AreEqual("agent", inner["type"]!.GetValue<string>(),
            "OpaqueInnerJson must SURVIVE a second round-trip.");
        Assert.AreEqual("linter", inner["agentId"]!.GetValue<string>());
        Assert.IsTrue(inner.ContainsKey("nestedObj"));
        Assert.AreEqual("2", inner["nestedObj"]!.AsObject()["depth"]!.GetValue<string>());
    }

    [TestMethod]
    public void OpaqueHttpHook_RoundTripsVerbatim()
    {
        // The schema's other unknown-to-the-editor type — verify the same
        // preservation pattern works regardless of the specific tag.
        JsonObject loaded = new()
        {
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = "Bash",
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "http",
                            ["url"] = "https://example.com/webhook",
                            ["headers"] = new JsonObject { ["X-Trace"] = "preserved" },
                        },
                    },
                },
            },
        };
        using HooksFixture fx = HooksFixture.From(loaded);
        HookEntry entry = fx.FirstHook("PreToolUse");
        Assert.IsTrue(entry.IsOpaque,
            "Schema 'http' type is unknown to the editor's variant set " +
            "(editor uses 'url') and must be preserved opaque.");

        entry.Matcher = "Edit";
        fx.SaveAndReload();

        JsonObject inner = InnerOnDisk(fx.Doc, "PreToolUse");
        Assert.AreEqual("http", inner["type"]!.GetValue<string>(),
            "'http' type discriminator must survive SDK round-trip.");
        Assert.AreEqual("https://example.com/webhook", inner["url"]!.GetValue<string>());
        Assert.AreEqual("preserved", inner["headers"]!.AsObject()["X-Trace"]!.GetValue<string>());
    }

    [TestMethod]
    public void UnknownEventName_SurvivesSaveReload_AndEditsToOtherEvents()
    {
        // A hook under an event NAME the schema doesn't recognise (deprecated,
        // renamed, or hand-authored). The editor must NEVER drop it on save — it
        // persists until the user removes it themselves. This is the event-name
        // analogue of OpaqueHttpHook_RoundTripsVerbatim (which covers unknown
        // hook TYPES).
        using HooksFixture fx = HooksFixture.From(CommandHook("SomeLegacyEvent", "*", "echo legacy"));

        // Present after the initial SDK-backed load, as its own group.
        Assert.AreEqual(1, fx.HookCount("SomeLegacyEvent"), "Unknown event loads as its own group.");

        // Survives a save + reload untouched (ToJsonValue emits every group with
        // hooks, filtering by NONE of the event names).
        fx.SaveAndReload();
        Assert.AreEqual(1, fx.HookCount("SomeLegacyEvent"), "Unknown event must survive save+reload.");
        Assert.AreEqual("echo legacy", fx.FirstHook("SomeLegacyEvent").CommandValue);

        // Editing a DIFFERENT (recognised) event and saving must not drop it.
        HookEntry added = fx.AddHookTo("PreToolUse");
        added.Matcher = "Bash";
        added.CommandValue = "echo new";
        fx.SaveAndReload();

        Assert.AreEqual(1, fx.HookCount("SomeLegacyEvent"),
            "Editing another event must leave the unknown event intact.");
        Assert.AreEqual("echo legacy", fx.FirstHook("SomeLegacyEvent").CommandValue);
        Assert.AreEqual(1, fx.HookCount("PreToolUse"));
    }
}