using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Adapters;

/// <summary>
/// Round-trip tests for the Claude adapter layer (ClaudeScope, ClaudeValueAdapter,
/// ClaudeWorkspaceAdapter). Each test exercises one documented edge case from the
/// value-currency contract.
/// </summary>
[TestClass]
public class ClaudeValueAdapterTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static LayeredValue LayeredWithJson(string key, ConfigScope scope, JsonNode? node)
    {
        ScopeEntry entry = new(scope, node, "/fake/path");
        return new LayeredValue(key, [entry])
        {
            EffectiveValue = node,
            EffectiveScope = scope,
        };
    }

    // ── Scope priority inversion ───────────────────────────────────────────────

    [TestMethod]
    public void ClaudeScope_Priority_IsInverted_RelativeToConfigScope()
    {
        // ConfigScope (lower enum value = higher priority):
        //   Managed=0 > Local=1 > Project=2 > User=3
        // IEditorScope.Priority is the inverted form (higher numeric = wins),
        // so the order is preserved but the comparison flips:
        //   Managed.Priority > Local.Priority > Project.Priority > User.Priority
        ClaudeScope managed = ClaudeScope.For(ConfigScope.Managed);
        ClaudeScope user = ClaudeScope.For(ConfigScope.User);
        ClaudeScope project = ClaudeScope.For(ConfigScope.Project);
        ClaudeScope local = ClaudeScope.For(ConfigScope.Local);

        Assert.IsTrue(managed.Priority > local.Priority, "Managed beats Local");
        Assert.IsTrue(local.Priority > project.Priority, "Local beats Project");
        Assert.IsTrue(project.Priority > user.Priority, "Project beats User");
    }

    [TestMethod]
    public void ClaudeScope_Managed_IsReadOnly()
    {
        Assert.IsTrue(ClaudeScope.For(ConfigScope.Managed).IsReadOnly);
    }

    [TestMethod]
    public void ClaudeScope_User_IsNotReadOnly()
    {
        Assert.IsFalse(ClaudeScope.For(ConfigScope.User).IsReadOnly);
    }

    [TestMethod]
    public void ClaudeScope_Id_IsLowercaseEnumName()
    {
        Assert.AreEqual("managed", ClaudeScope.For(ConfigScope.Managed).Id);
        Assert.AreEqual("user", ClaudeScope.For(ConfigScope.User).Id);
        Assert.AreEqual("project", ClaudeScope.For(ConfigScope.Project).Id);
        Assert.AreEqual("local", ClaudeScope.For(ConfigScope.Local).Id);
    }

    [TestMethod]
    public void ClaudeScope_For_ReturnsCachedSingleton()
    {
        Assert.AreSame(ClaudeScope.For(ConfigScope.User), ClaudeScope.For(ConfigScope.User));
    }

    // ── ClaudeValueAdapter – basic scalar round-trips ─────────────────────────

    [TestMethod]
    public void ValueAdapter_Bool_RoundTrips()
    {
        LayeredValue layered = LayeredWithJson("myBool", ConfigScope.User, JsonValue.Create(true));
        ClaudeValueAdapter adapter = new(layered);

        ClaudeScope scope = ClaudeScope.For(ConfigScope.User);
        Assert.IsTrue(adapter.IsDefinedAt(scope));
        Assert.IsTrue((bool?)adapter.GetValueAt(scope));
        Assert.IsTrue((bool?)adapter.EffectiveValue);
    }

    [TestMethod]
    public void ValueAdapter_String_RoundTrips()
    {
        LayeredValue layered = LayeredWithJson("myStr", ConfigScope.User, JsonValue.Create("hello"));
        ClaudeValueAdapter adapter = new(layered);

        Assert.AreEqual("hello", adapter.GetValueAt(ClaudeScope.For(ConfigScope.User)));
    }

    [TestMethod]
    public void ValueAdapter_LongInteger_NormalisedToLong()
    {
        // JSON integers in range fit in long
        JsonValue node = JsonValue.Create(9_007_199_254_740_992L); // 2^53 — exact in long, precise in double
        ClaudeValueAdapter adapter = new(LayeredWithJson("n", ConfigScope.User, node));

        object? value = adapter.GetValueAt(ClaudeScope.For(ConfigScope.User));
        Assert.IsInstanceOfType<long>(value);
        Assert.AreEqual(9_007_199_254_740_992L, (long)value!);
    }

    [TestMethod]
    public void ValueAdapter_SmallInteger_NormalisedToLong()
    {
        JsonValue node = JsonValue.Create(42);
        ClaudeValueAdapter adapter = new(LayeredWithJson("n", ConfigScope.User, node));

        object? value = adapter.GetValueAt(ClaudeScope.For(ConfigScope.User));
        // Must be long (or at least integral numeric)
        Assert.IsNotNull(value);
        Assert.AreEqual(42L, Convert.ToInt64(value));
    }

    [TestMethod]
    public void ValueAdapter_Double_NormalisedToDouble()
    {
        JsonValue node = JsonValue.Create(3.14);
        ClaudeValueAdapter adapter = new(LayeredWithJson("n", ConfigScope.User, node));

        object? value = adapter.GetValueAt(ClaudeScope.For(ConfigScope.User));
        Assert.IsInstanceOfType<double>(value);
        Assert.AreEqual(3.14, (double)value!);
    }

    // ── Explicit null vs absent ────────────────────────────────────────────────

    [TestMethod]
    public void ValueAdapter_ExplicitNull_IsDefinedAt_IsTrue()
    {
        // null JsonNode = key present with explicit null value
        ScopeEntry entry = new(ConfigScope.User, null, "/fake");
        LayeredValue layered = new("myKey", [entry])
        {
            EffectiveValue = null,
            EffectiveScope = ConfigScope.User,
        };
        ClaudeValueAdapter adapter = new(layered);

        ClaudeScope scope = ClaudeScope.For(ConfigScope.User);
        Assert.IsTrue(adapter.IsDefinedAt(scope), "explicit null is still 'defined'");
        Assert.IsNull(adapter.GetValueAt(scope), "GetValueAt returns null for explicit null");
    }

    [TestMethod]
    public void ValueAdapter_AbsentScope_IsDefinedAt_IsFalse()
    {
        LayeredValue layered = LayeredWithJson("myKey", ConfigScope.Project, JsonValue.Create("v"));
        ClaudeValueAdapter adapter = new(layered);

        // User scope was never set
        ClaudeScope userScope = ClaudeScope.For(ConfigScope.User);
        Assert.IsFalse(adapter.IsDefinedAt(userScope));
        Assert.IsNull(adapter.GetValueAt(userScope));
    }

    // ── IsOverridden ──────────────────────────────────────────────────────────

    [TestMethod]
    public void ValueAdapter_IsOverridden_True_WhenMultipleScopesDefined()
    {
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, JsonValue.Create("a"), "/u"),
            new ScopeEntry(ConfigScope.Project, JsonValue.Create("b"), "/p"),
        ];
        LayeredValue layered = new("k", entries)
        {
            EffectiveValue = JsonValue.Create("a"),
            EffectiveScope = ConfigScope.User,
        };
        ClaudeValueAdapter adapter = new(layered);

        Assert.IsTrue(adapter.IsOverridden);
    }

    // ── ClaudeWorkspaceAdapter ─────────────────────────────────────────────────

    [TestMethod]
    public void WorkspaceAdapter_SetValue_RaisesValueChanged()
    {
        SettingsDocument[] docs =
        [
            new SettingsDocument(ConfigScope.User, "/fake/settings.json", new JsonObject(), isReadOnly: false),
        ];
        SettingsWorkspace workspace = new(docs);
        ClaudeWorkspaceAdapter adapter = new(workspace);

        ValueChangedEventArgs? received = null;
        adapter.ValueChanged += (_, e) => received = e;

        adapter.SetValue("myKey", "hello", ClaudeScope.For(ConfigScope.User));

        Assert.IsNotNull(received);
        Assert.AreEqual("myKey", received!.Path);
        Assert.AreEqual("user", received.Scope.Id);
    }

    [TestMethod]
    public void WorkspaceAdapter_SetThenGet_RoundTrips()
    {
        SettingsDocument[] docs =
        [
            new SettingsDocument(ConfigScope.User, "/fake/settings.json", new JsonObject(), isReadOnly: false),
        ];
        SettingsWorkspace workspace = new(docs);
        ClaudeWorkspaceAdapter adapter = new(workspace);

        adapter.SetValue("flag", true, ClaudeScope.For(ConfigScope.User));

        IEditorValue val = adapter.GetValue("flag");
        Assert.IsNotNull(val);
        Assert.IsTrue(adapter.GetValue("flag").IsDefinedAt(ClaudeScope.For(ConfigScope.User)));
        Assert.IsTrue((bool?)adapter.GetValue("flag").GetValueAt(ClaudeScope.For(ConfigScope.User)));
    }

    [TestMethod]
    public void WorkspaceAdapter_RemoveValue_RaisesValueChanged()
    {
        SettingsDocument[] docs =
        [
            new SettingsDocument(ConfigScope.User, "/fake/settings.json", new JsonObject(), isReadOnly: false),
        ];
        SettingsWorkspace workspace = new(docs);
        ClaudeWorkspaceAdapter adapter = new(workspace);

        adapter.SetValue("myKey", "hello", ClaudeScope.For(ConfigScope.User));

        ValueChangedEventArgs? removed = null;
        adapter.ValueChanged += (_, e) => removed = e;
        adapter.RemoveValue("myKey", ClaudeScope.For(ConfigScope.User));

        Assert.IsNotNull(removed);
        Assert.AreEqual("myKey", removed!.Path);
    }

    [TestMethod]
    public void WorkspaceAdapter_AvailableScopes_MatchesDocumentScopes()
    {
        SettingsDocument[] docs =
        [
            new SettingsDocument(ConfigScope.Managed, "/m", new JsonObject(), isReadOnly: true),
            new SettingsDocument(ConfigScope.User, "/u", new JsonObject(), isReadOnly: false),
        ];
        SettingsWorkspace workspace = new(docs);
        ClaudeWorkspaceAdapter adapter = new(workspace);

        string[] ids = adapter.AvailableScopes.Select(s => s.Id).ToArray();
        CollectionAssert.Contains(ids, "managed");
        CollectionAssert.Contains(ids, "user");
    }
}