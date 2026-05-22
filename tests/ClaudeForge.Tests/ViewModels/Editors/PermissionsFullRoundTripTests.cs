// (Permissions): comprehensive disk →
// SDK → editor → user-mutation → SDK → disk round-trip suite for the
// Permissions editor.  Mirrors HooksFullRoundTripTests + McpFullRoundTripTests'
// fixture pattern.

using Bennewitz.Ninja.ClaudeForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

[TestClass]
public class PermissionsFullRoundTripTests
{
    // ─── Fixture ─────────────────────────────────────────────────────────────

    private sealed class PermsFixture : IDisposable
    {
        public SettingsDocument Doc { get; }
        public SettingsWorkspace Workspace { get; }
        public ClaudeCodeClient Client { get; }
        public PermissionsEditorViewModel Editor { get; private set; } = null!;

        private PermsFixture(SettingsDocument doc, SettingsWorkspace ws, ClaudeCodeClient client)
        {
            Doc = doc;
            Workspace = ws;
            Client = client;
        }

        public static PermsFixture From(JsonObject? permissions)
        {
            JsonObject rootObj = new();
            if (permissions is not null)
            {
                rootObj["permissions"] = permissions.DeepClone();
            }

            SettingsDocument doc = new(ConfigScope.User, "settings.json", rootObj, isReadOnly: false);
            SettingsWorkspace ws = new([doc]);
            ClaudeCodeClient client = ClaudeCodeClient.FromExistingWorkspace(
                ws, ConfigScope.User,
                new SchemaRegistry(new HttpClient()));
            PermsFixture fx = new(doc, ws, client);
            fx.RebuildEditor();
            return fx;
        }

        private void RebuildEditor()
        {
            Editor = new PermissionsEditorViewModel(PermissionsSchema(), ConfigScope.User, Client);
            Editor.LoadFromLayered(BuildLayered(Doc.Root["permissions"]), ConfigScope.User);
        }

        public void SaveAndReload()
        {
            JsonNode? emitted = Editor.ToJsonValue();
            if (emitted is not null)
            {
                Client.SetValue("permissions", emitted, ConfigScope.User);
            }
            else
            {
                Client.RemoveValue("permissions", ConfigScope.User);
            }

            RebuildEditor();
        }

        public void Dispose()
        {
            Client.Dispose();
        }
    }

    private static SchemaNode PermissionsSchema()
    {
        return new SchemaNode("permissions", "permissions") { ValueType = SchemaValueType.Complex };
    }

    private static LayeredValue BuildLayered(JsonNode? permsNode)
    {
        if (permsNode is null)
        {
            return new LayeredValue("permissions", []);
        }

        ScopeEntry entry = new(ConfigScope.User, permsNode, "/fake");
        return new LayeredValue("permissions", [entry])
        {
            EffectiveValue = permsNode,
            EffectiveScope = ConfigScope.User,
        };
    }

    // ─── JSON builders ──────────────────────────────────────────────────────

    private static JsonObject Perms(
        IReadOnlyList<string>? allow = null,
        IReadOnlyList<string>? deny = null,
        IReadOnlyList<string>? ask = null,
        string? defaultMode = null,
        IReadOnlyList<string>? additionalDirectories = null,
        bool? disableBypassPermissionsMode = null,
        IReadOnlyDictionary<string, JsonNode?>? extras = null)
    {
        JsonObject obj = new();
        if (allow is { Count: > 0 } a)
        {
            JsonArray arr = new();
            foreach (string v in a)
            {
                arr.Add(v);
            }

            obj["allow"] = arr;
        }

        if (deny is { Count: > 0 } d)
        {
            JsonArray arr = new();
            foreach (string v in d)
            {
                arr.Add(v);
            }

            obj["deny"] = arr;
        }

        if (ask is { Count: > 0 } k)
        {
            JsonArray arr = new();
            foreach (string v in k)
            {
                arr.Add(v);
            }

            obj["ask"] = arr;
        }

        if (defaultMode is not null)
        {
            obj["defaultMode"] = defaultMode;
        }

        if (additionalDirectories is { Count: > 0 } ad)
        {
            JsonArray arr = new();
            foreach (string v in ad)
            {
                arr.Add(v);
            }

            obj["additionalDirectories"] = arr;
        }

        if (disableBypassPermissionsMode.HasValue)
        {
            obj["disableBypassPermissionsMode"] = disableBypassPermissionsMode.Value;
        }

        if (extras is not null)
        {
            foreach ((string xk, JsonNode? xv) in extras)
            {
                obj[xk] = xv?.DeepClone();
            }
        }

        return obj;
    }

    private static JsonObject PermsOnDisk(SettingsDocument doc)
    {
        return doc.Root["permissions"]!.AsObject();
    }

    private static List<string> RuleStrings(IEnumerable<PermissionRuleViewModel> rules)
    {
        return rules.Select(r => r.Rule).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Variant: Allow / Deny / Ask  (× mutation)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void AllowList_AddRule_RoundTrips()
    {
        using PermsFixture fx = PermsFixture.From(Perms(allow: ["Read"]));
        fx.Editor.NewAllowText = "Bash(git status)";
        fx.Editor.AddAllowCommand.Execute(null);

        fx.SaveAndReload();

        Assert.AreEqual(2, fx.Editor.AllowList.Count);
        CollectionAssert.AreEquivalent(
            new[] { "Read", "Bash(git status)" },
            RuleStrings(fx.Editor.AllowList));
    }

    [TestMethod]
    public void AllowList_RemoveRule_RoundTrips()
    {
        using PermsFixture fx = PermsFixture.From(Perms(allow: ["Read", "Glob", "Grep"]));
        PermissionRuleViewModel first = fx.Editor.AllowList[0];
        fx.Editor.RemoveAllowCommand.Execute(first);

        fx.SaveAndReload();

        Assert.AreEqual(2, fx.Editor.AllowList.Count);
        Assert.IsFalse(RuleStrings(fx.Editor.AllowList).Contains(first.Rule));
    }

    [TestMethod]
    public void DenyList_AddRule_RoundTrips()
    {
        using PermsFixture fx = PermsFixture.From(Perms(deny: ["Bash(rm -rf *)"]));
        fx.Editor.NewDenyText = "Bash(sudo *)";
        fx.Editor.AddDenyCommand.Execute(null);

        fx.SaveAndReload();

        Assert.AreEqual(2, fx.Editor.DenyList.Count);
    }

    [TestMethod]
    public void DenyList_RemoveRule_RoundTrips()
    {
        using PermsFixture fx = PermsFixture.From(Perms(deny: ["Bash(rm *)", "Write"]));
        PermissionRuleViewModel first = fx.Editor.DenyList[0];
        fx.Editor.RemoveDenyCommand.Execute(first);

        fx.SaveAndReload();

        Assert.AreEqual(1, fx.Editor.DenyList.Count);
    }

    [TestMethod]
    public void AskList_AddRule_RoundTrips()
    {
        using PermsFixture fx = PermsFixture.From(Perms());
        fx.Editor.NewAskText = "Bash(git push *)";
        fx.Editor.AddAskCommand.Execute(null);

        fx.SaveAndReload();

        Assert.AreEqual(1, fx.Editor.AskList.Count);
        Assert.AreEqual("Bash(git push *)", fx.Editor.AskList[0].Rule);
    }

    [TestMethod]
    public void AskList_RemoveRule_RoundTrips()
    {
        using PermsFixture fx = PermsFixture.From(Perms(ask: ["Bash(git commit *)", "Bash(git push *)"]));
        PermissionRuleViewModel first = fx.Editor.AskList[0];
        fx.Editor.RemoveAskCommand.Execute(first);

        fx.SaveAndReload();

        Assert.AreEqual(1, fx.Editor.AskList.Count);
    }

    [TestMethod]
    public void AllThreeLists_RoundTrip_Independently()
    {
        // Locks the contract that mutating one list does not affect the
        // others on round-trip.  Catches schema-key ordering / overwrite
        // regressions in ToJsonValue.
        using PermsFixture fx = PermsFixture.From(Perms(
            allow: ["Read"],
            deny: ["Write"],
            ask: ["Edit"]));

        fx.Editor.NewAllowText = "Glob";
        fx.Editor.AddAllowCommand.Execute(null);

        fx.SaveAndReload();

        CollectionAssert.AreEquivalent(new[] { "Read", "Glob" }, RuleStrings(fx.Editor.AllowList));
        CollectionAssert.AreEquivalent(new[] { "Write" }, RuleStrings(fx.Editor.DenyList));
        CollectionAssert.AreEquivalent(new[] { "Edit" }, RuleStrings(fx.Editor.AskList));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DefaultMode (tri-ish state including null)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void DefaultMode_SetFromNull_RoundTrips()
    {
        using PermsFixture fx = PermsFixture.From(Perms(allow: ["Read"]));
        Assert.IsNull(fx.Editor.DefaultMode, "precondition: defaultMode unset");
        fx.Editor.DefaultMode = "acceptEdits";

        fx.SaveAndReload();

        Assert.AreEqual("acceptEdits", fx.Editor.DefaultMode);
        Assert.AreEqual("acceptEdits", PermsOnDisk(fx.Doc)["defaultMode"]!.GetValue<string>());
    }

    [TestMethod]
    public void DefaultMode_ClearToNull_RemovesKeyFromOnDisk()
    {
        using PermsFixture fx = PermsFixture.From(Perms(
            allow: ["Read"],
            defaultMode: "plan"));
        Assert.AreEqual("plan", fx.Editor.DefaultMode);
        fx.Editor.DefaultMode = null;

        fx.SaveAndReload();

        Assert.IsNull(fx.Editor.DefaultMode);
        Assert.IsFalse(PermsOnDisk(fx.Doc).ContainsKey("defaultMode"),
            "Cleared defaultMode MUST NOT survive on disk as null/empty.");
    }

    [TestMethod]
    public void DefaultMode_Change_RoundTrips()
    {
        using PermsFixture fx = PermsFixture.From(Perms(allow: ["Read"], defaultMode: "default"));
        fx.Editor.DefaultMode = "bypassPermissions";

        fx.SaveAndReload();

        Assert.AreEqual("bypassPermissions", fx.Editor.DefaultMode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  AdditionalDirectories
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void AdditionalDirectories_AddOne_RoundTrips()
    {
        using PermsFixture fx = PermsFixture.From(Perms(allow: ["Read"]));
        fx.Editor.NewAdditionalDirectory = "/srv/projects";
        fx.Editor.AddAdditionalDirectoryCommand.Execute(null);

        fx.SaveAndReload();

        Assert.AreEqual(1, fx.Editor.AdditionalDirectories.Count);
        Assert.AreEqual("/srv/projects", fx.Editor.AdditionalDirectories[0]);
    }

    [TestMethod]
    public void AdditionalDirectories_RemoveOne_RoundTrips()
    {
        using PermsFixture fx = PermsFixture.From(Perms(
            allow: ["Read"],
            additionalDirectories: ["/a", "/b"]));
        fx.Editor.RemoveAdditionalDirectoryCommand.Execute("/a");

        fx.SaveAndReload();

        Assert.AreEqual(1, fx.Editor.AdditionalDirectories.Count);
        Assert.AreEqual("/b", fx.Editor.AdditionalDirectories[0]);
    }

    [TestMethod]
    public void AdditionalDirectories_EmptyList_OmittedFromOnDisk()
    {
        // Removing the last entry must not leave `"additionalDirectories": []`
        // on disk.
        using PermsFixture fx = PermsFixture.From(Perms(
            allow: ["Read"],
            additionalDirectories: ["/a"]));
        fx.Editor.RemoveAdditionalDirectoryCommand.Execute("/a");

        fx.SaveAndReload();

        Assert.IsFalse(PermsOnDisk(fx.Doc).ContainsKey("additionalDirectories"),
            "Empty additionalDirectories array MUST NOT appear on disk.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DisableBypassPermissionsMode (tri-state: null / true / false)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void DisableBypassPermissionsMode_SetTrue_RoundTrips()
    {
        using PermsFixture fx = PermsFixture.From(Perms(allow: ["Read"]));
        Assert.IsNull(fx.Editor.DisableBypassPermissionsMode);
        fx.Editor.DisableBypassPermissionsMode = true;

        fx.SaveAndReload();

        Assert.IsTrue(fx.Editor.DisableBypassPermissionsMode);
        Assert.IsTrue(PermsOnDisk(fx.Doc)["disableBypassPermissionsMode"]!.GetValue<bool>());
    }

    [TestMethod]
    public void DisableBypassPermissionsMode_SetFalse_RoundTrips()
    {
        // Explicit false ≠ null — a deliberate "I want bypass mode
        // ENABLED at this scope" must round-trip distinct from "no
        // override at this scope".
        using PermsFixture fx = PermsFixture.From(Perms(allow: ["Read"]));
        fx.Editor.DisableBypassPermissionsMode = false;

        fx.SaveAndReload();

        Assert.IsFalse(fx.Editor.DisableBypassPermissionsMode);
        Assert.IsTrue(PermsOnDisk(fx.Doc).ContainsKey("disableBypassPermissionsMode"));
        Assert.IsFalse(PermsOnDisk(fx.Doc)["disableBypassPermissionsMode"]!.GetValue<bool>());
    }

    [TestMethod]
    public void DisableBypassPermissionsMode_ClearToNull_RemovesKeyFromOnDisk()
    {
        using PermsFixture fx = PermsFixture.From(Perms(
            allow: ["Read"],
            disableBypassPermissionsMode: true));
        Assert.IsTrue(fx.Editor.DisableBypassPermissionsMode);
        fx.Editor.DisableBypassPermissionsMode = null;

        fx.SaveAndReload();

        Assert.IsNull(fx.Editor.DisableBypassPermissionsMode);
        Assert.IsFalse(PermsOnDisk(fx.Doc).ContainsKey("disableBypassPermissionsMode"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PreservedFields — replay across multiple round-trips
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void PreservedFields_UnknownSubKeys_SurviveSingleRoundTrip()
    {
        // Future schema additions — verify they round-trip via the
        // editor's _preservedFields stash.
        using PermsFixture fx = PermsFixture.From(Perms(
            allow: ["Read"],
            extras: new Dictionary<string, JsonNode?>
            {
                ["futureKey"] = "futureValue",
            }));
        fx.Editor.NewAllowText = "Glob";
        fx.Editor.AddAllowCommand.Execute(null);

        fx.SaveAndReload();

        Assert.IsTrue(PermsOnDisk(fx.Doc).ContainsKey("futureKey"),
            "PreservedFields must replay 'futureKey' on save.");
        Assert.AreEqual("futureValue", PermsOnDisk(fx.Doc)["futureKey"]!.GetValue<string>());
    }

    [TestMethod]
    public void PreservedFields_UnknownSubKeys_SurviveDoubleRoundTrip()
    {
        // bug class — fields survive ONE round-trip but
        // vanish on the SECOND.
        using PermsFixture fx = PermsFixture.From(Perms(
            allow: ["Read"],
            extras: new Dictionary<string, JsonNode?>
            {
                ["futureKey"] = "futureValue",
            }));
        fx.Editor.NewAllowText = "Glob";
        fx.Editor.AddAllowCommand.Execute(null);
        fx.SaveAndReload();

        // Second mutation + reload.
        fx.Editor.NewDenyText = "Bash(rm *)";
        fx.Editor.AddDenyCommand.Execute(null);
        fx.SaveAndReload();

        Assert.IsTrue(PermsOnDisk(fx.Doc).ContainsKey("futureKey"),
            "PreservedFields must SURVIVE a second round-trip.");
        Assert.AreEqual("futureValue", PermsOnDisk(fx.Doc)["futureKey"]!.GetValue<string>());
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Workspace-removal contract
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void EmptyAllLists_NoDefaultMode_DropsPermissionsKeyFromOnDisk()
    {
        using PermsFixture fx = PermsFixture.From(Perms(allow: ["Read"]));
        fx.Editor.RemoveAllowCommand.Execute(fx.Editor.AllowList[0]);

        fx.SaveAndReload();

        Assert.IsFalse(fx.Doc.Root.ContainsKey("permissions"),
            "When all lists are empty AND no other keys are set, the entire 'permissions' object MUST be removed.");
    }

    [TestMethod]
    public void EmptyLists_DefaultModeSet_KeepsPermissionsKeyOnDisk()
    {
        using PermsFixture fx = PermsFixture.From(Perms(
            allow: ["Read"],
            defaultMode: "acceptEdits"));
        fx.Editor.RemoveAllowCommand.Execute(fx.Editor.AllowList[0]);

        fx.SaveAndReload();

        Assert.IsTrue(fx.Doc.Root.ContainsKey("permissions"),
            "DefaultMode alone must keep 'permissions' on disk.");
        Assert.IsFalse(PermsOnDisk(fx.Doc).ContainsKey("allow"),
            "Empty allow list MUST NOT survive on disk.");
        Assert.IsTrue(PermsOnDisk(fx.Doc).ContainsKey("defaultMode"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Empty-array contract on disk
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void RemoveAllAllowRules_OmitsAllowKeyFromOnDisk_WhenOtherListsHaveContent()
    {
        using PermsFixture fx = PermsFixture.From(Perms(
            allow: ["Read"],
            deny: ["Write"]));
        fx.Editor.RemoveAllowCommand.Execute(fx.Editor.AllowList[0]);

        fx.SaveAndReload();

        JsonObject disk = PermsOnDisk(fx.Doc);
        Assert.IsFalse(disk.ContainsKey("allow"),
            "Empty allow list MUST NOT survive on disk.");
        Assert.IsTrue(disk.ContainsKey("deny"),
            "Other lists with content must persist.");
    }
}