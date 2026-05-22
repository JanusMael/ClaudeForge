namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

[TestClass]
public class McpServerEntryTests
{
    [TestMethod]
    public void AddArg_AddsArgItemToCollection()
    {
        McpServerEntry entry = new("srv");
        entry.NewArg = "--verbose";
        entry.AddArgCommand.Execute(null);

        Assert.AreEqual(1, entry.Args.Count);
        Assert.AreEqual("--verbose", entry.Args[0].Value);
        Assert.AreEqual(string.Empty, entry.NewArg);
    }

    [TestMethod]
    public void RemoveArg_RemovesItem()
    {
        McpServerEntry entry = new("srv");
        entry.NewArg = "-v";
        entry.AddArgCommand.Execute(null);
        entry.RemoveArgCommand.Execute(entry.Args[0]);

        Assert.AreEqual(0, entry.Args.Count);
    }

    [TestMethod]
    public void AddEnv_AddsEnvVar()
    {
        McpServerEntry entry = new("srv");
        entry.NewEnvKey = "DEBUG";
        entry.NewEnvValue = "1";
        entry.AddEnvCommand.Execute(null);

        Assert.AreEqual(1, entry.Env.Count);
        Assert.AreEqual("DEBUG", entry.Env[0].Key);
        Assert.AreEqual("1", entry.Env[0].Value);
        Assert.AreEqual(string.Empty, entry.NewEnvKey);
    }

    [TestMethod]
    public void FromJson_RoundTripPreservesAllFields()
    {
        JsonObject obj = new()
        {
            ["type"] = "stdio",
            ["command"] = "npx",
            ["args"] = new JsonArray { "-y", "@ctx/mcp" },
            ["env"] = new JsonObject { ["NODE_ENV"] = "production" },
        };

        McpServerEntry entry = McpServerEntry.FromJson("ctx", obj);

        Assert.AreEqual("npx", entry.Command);
        Assert.AreEqual(2, entry.Args.Count);
        Assert.AreEqual("-y", entry.Args[0].Value);
        Assert.AreEqual(1, entry.Env.Count);
        Assert.AreEqual("NODE_ENV", entry.Env[0].Key);
    }

    [TestMethod]
    public void ToJson_RoundTripsCorrectly()
    {
        McpServerEntry entry = new("srv")
        {
            Type = "http",
            Url = "http://localhost:9000",
        };
        entry.NewEnvKey = "TOKEN";
        entry.NewEnvValue = "abc";
        entry.AddEnvCommand.Execute(null);

        JsonObject json = entry.ToJson();

        Assert.AreEqual("http", json["type"]!.GetValue<string>());
        Assert.AreEqual("http://localhost:9000", json["url"]!.GetValue<string>());
        Assert.IsNull(json["args"]); // no args
        JsonObject? env = json["env"] as JsonObject;
        Assert.IsNotNull(env);
        Assert.AreEqual("abc", env!["TOKEN"]!.GetValue<string>());
    }

    [TestMethod]
    public void AddArg_IgnoresEmptyText()
    {
        McpServerEntry entry = new("srv");
        entry.NewArg = "   ";
        entry.AddArgCommand.Execute(null);

        Assert.AreEqual(0, entry.Args.Count);
    }

    // ── Transport-level validation ────────────────────────────────────────────

    [TestMethod]
    public void StdioTransport_CommandMissing_WhenCommandBlank()
    {
        McpServerEntry entry = new("srv") { Type = "stdio", Command = "" };
        Assert.IsTrue(entry.CommandMissing,
            "CommandMissing must be true when transport is stdio and Command is blank.");
        Assert.IsTrue(entry.HasValidationError,
            "HasValidationError must be true when CommandMissing is true.");
        Assert.IsFalse(string.IsNullOrEmpty(entry.ValidationMessage),
            "ValidationMessage must be non-empty when there is a validation error.");
    }

    [TestMethod]
    public void StdioTransport_CommandNotMissing_WhenCommandPresent()
    {
        McpServerEntry entry = new("srv") { Type = "stdio", Command = "node server.js" };
        Assert.IsFalse(entry.CommandMissing,
            "CommandMissing must be false when transport is stdio and Command is non-blank.");
        Assert.IsFalse(entry.HasValidationError,
            "HasValidationError must be false when stdio entry has a command.");
        Assert.AreEqual(string.Empty, entry.ValidationMessage,
            "ValidationMessage must be empty when entry is valid.");
    }

    [TestMethod]
    public void SseTransport_UrlInvalid_WhenUrlNotHttps()
    {
        McpServerEntry entry = new("srv") { Type = "sse", Url = "ftp://example.com/mcp" };
        Assert.IsTrue(entry.UrlInvalid,
            "UrlInvalid must be true when transport is sse and URL scheme is not http/https.");
        Assert.IsTrue(entry.HasValidationError,
            "HasValidationError must be true when UrlInvalid is true.");
        Assert.IsFalse(string.IsNullOrEmpty(entry.ValidationMessage),
            "ValidationMessage must be non-empty when there is a URL validation error.");
    }

    [TestMethod]
    public void SseTransport_UrlValid_WhenUrlIsHttps()
    {
        McpServerEntry entry = new("srv") { Type = "sse", Url = "https://example.com/mcp" };
        Assert.IsFalse(entry.UrlInvalid,
            "UrlInvalid must be false when transport is sse and URL is a valid https address.");
        Assert.IsFalse(entry.HasValidationError,
            "HasValidationError must be false when sse entry has a valid URL.");
        Assert.AreEqual(string.Empty, entry.ValidationMessage,
            "ValidationMessage must be empty when entry is valid.");
    }

    [TestMethod]
    public void HasValidationError_FalseWhenValid()
    {
        // http transport with a valid http URL must also pass validation.
        McpServerEntry entry = new("srv") { Type = "http", Url = "http://localhost:8080/mcp" };
        Assert.IsFalse(entry.CommandMissing,
            "CommandMissing must be false for http transport.");
        Assert.IsFalse(entry.UrlInvalid,
            "UrlInvalid must be false for http transport with a valid http URL.");
        Assert.IsFalse(entry.HasValidationError,
            "HasValidationError must be false when no constraint is violated.");
    }
}