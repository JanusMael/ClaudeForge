namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// regression tests for the data-loss bug in
/// <see cref="HookEntry.FromJson"/> / <see cref="HookEntry.ToJson"/>.
/// </summary>
/// <remarks>
/// <para>
/// Before this fix, hooks of types the editor didn't natively render
/// (notably <c>agent</c> and <c>http</c>, both valid in the Claude Code
/// schema) were silently downcast to <c>HookCommandType.Command</c> with
/// an empty <c>CommandValue</c>. The next save through the editor either
/// dropped them entirely (filtered by <c>HookEventGroup.ToJson</c>) or
/// emitted a schema-invalid <c>{"type":"command"}</c> entry — destroying
/// the user's data either way.
/// </para>
/// <para>
/// Fix: when FromJson encounters an unrecognised type, the original
/// JsonObject is stashed verbatim and re-emitted by ToJson. The user
/// loses no data even if the editor doesn't yet know how to render
/// the hook type.
/// </para>
/// </remarks>
[TestClass]
public sealed class HookEntryOpaqueRoundtripTests
{
    [TestMethod]
    public void FromJson_AgentType_PreservesOpaque()
    {
        // An "agent" hook with arbitrary fields the editor doesn't know about.
        JsonObject input = (JsonObject)JsonNode.Parse("""
                                                      {
                                                          "matcher": "Bash",
                                                          "type": "agent",
                                                          "agent": "code-reviewer",
                                                          "config": { "depth": 3 }
                                                      }
                                                      """)!;

        HookEntry entry = HookEntry.FromJson(input);

        Assert.IsTrue(entry.IsOpaque, "Agent type must be preserved as opaque.");
    }

    [TestMethod]
    public void FromJson_HttpType_PreservesOpaque()
    {
        JsonObject input = (JsonObject)JsonNode.Parse("""
                                                      {
                                                          "matcher": "Bash",
                                                          "type": "http",
                                                          "url": "https://example.com/hook",
                                                          "method": "POST"
                                                      }
                                                      """)!;

        HookEntry entry = HookEntry.FromJson(input);

        Assert.IsTrue(entry.IsOpaque);
    }

    [TestMethod]
    public void RoundTrip_AgentType_EmitsVerbatim()
    {
        JsonObject input = (JsonObject)JsonNode.Parse("""
                                                      {
                                                          "type": "agent",
                                                          "agent": "code-reviewer",
                                                          "config": { "depth": 3, "extra": ["a", "b"] }
                                                      }
                                                      """)!;

        HookEntry entry = HookEntry.FromJson(input);
        JsonObject output = entry.ToJson();

        // Every field of the original must survive the round-trip.
        Assert.AreEqual("agent", output["type"]!.GetValue<string>());
        Assert.AreEqual("code-reviewer", output["agent"]!.GetValue<string>());
        JsonObject cfg = output["config"]!.AsObject();
        Assert.AreEqual(3, cfg["depth"]!.GetValue<int>());
        Assert.AreEqual(2, cfg["extra"]!.AsArray().Count);
        Assert.AreEqual("a", cfg["extra"]![0]!.GetValue<string>());
    }

    [TestMethod]
    public void RoundTrip_HttpType_EmitsVerbatim()
    {
        JsonObject input = (JsonObject)JsonNode.Parse("""
                                                      { "type": "http", "url": "https://example.com/hook", "method": "POST" }
                                                      """)!;

        HookEntry entry = HookEntry.FromJson(input);
        JsonObject output = entry.ToJson();

        Assert.AreEqual("http", output["type"]!.GetValue<string>());
        Assert.AreEqual("https://example.com/hook", output["url"]!.GetValue<string>());
        Assert.AreEqual("POST", output["method"]!.GetValue<string>());
    }

    [TestMethod]
    public void RoundTrip_CommandType_NotOpaque()
    {
        // The native types must continue to use the synthesized form, NOT the
        // opaque path — otherwise inline edits to CommandValue / Matcher /
        // CommandType would not flow through to the saved JSON.
        JsonObject input = (JsonObject)JsonNode.Parse("""
                                                      { "type": "command", "command": "echo hello" }
                                                      """)!;

        HookEntry entry = HookEntry.FromJson(input);

        Assert.IsFalse(entry.IsOpaque,
            "Native command type must not take the opaque preservation path.");
        Assert.AreEqual(HookCommandType.Command, entry.CommandType);
        Assert.AreEqual("echo hello", entry.CommandValue);
    }

    [TestMethod]
    public void RoundTrip_CommandType_EditsApplyOnSave()
    {
        // Lock the contract: edits through the editor's CommandValue setter
        // MUST be reflected in ToJson() output (proves the synthesized path
        // is reached for native types, not accidentally the opaque one).
        JsonObject input = (JsonObject)JsonNode.Parse("""
                                                      { "type": "command", "command": "old" }
                                                      """)!;

        HookEntry entry = HookEntry.FromJson(input);
        entry.CommandValue = "new";

        JsonObject output = entry.ToJson();
        Assert.AreEqual("new", output["command"]!.GetValue<string>());
    }

    [TestMethod]
    public void RoundTrip_PromptType_NotOpaque()
    {
        JsonObject input = (JsonObject)JsonNode.Parse("""
                                                      { "type": "prompt", "prompt": "Be careful with rm -rf" }
                                                      """)!;

        HookEntry entry = HookEntry.FromJson(input);

        Assert.IsFalse(entry.IsOpaque);
        Assert.AreEqual(HookCommandType.Prompt, entry.CommandType);
    }

    [TestMethod]
    public void RoundTrip_UrlType_NotOpaque()
    {
        JsonObject input = (JsonObject)JsonNode.Parse("""
                                                      { "type": "url", "url": "https://docs.example.com" }
                                                      """)!;

        HookEntry entry = HookEntry.FromJson(input);

        Assert.IsFalse(entry.IsOpaque);
        Assert.AreEqual(HookCommandType.Url, entry.CommandType);
    }

    [TestMethod]
    public void OpaqueEntry_NotFilteredByGroupToJson()
    {
        // Lock the HookEventGroup.ToJson contract: opaque entries must NOT be
        // dropped by the empty-CommandValue filter, even though the editor
        // hasn't given them a "real" CommandValue. Otherwise the data-loss
        // bug returns at the group level.
        HookEventGroup group = HookEventGroup.FromJson("Stop", JsonNode.Parse("""
                                                                              [
                                                                                  {
                                                                                      "matcher": "Bash",
                                                                                      "hooks": [
                                                                                          { "type": "agent", "agent": "code-reviewer" }
                                                                                      ]
                                                                                  }
                                                                              ]
                                                                              """));

        Assert.AreEqual(1, group.Hooks.Count);
        Assert.IsTrue(group.Hooks[0].IsOpaque);

        JsonArray output = group.ToJson();
        Assert.AreEqual(1, output.Count);
        JsonArray inner = output[0]!.AsObject()["hooks"]!.AsArray();
        Assert.AreEqual(1, inner.Count);
        Assert.AreEqual("agent", inner[0]!.AsObject()["type"]!.GetValue<string>());
    }
}