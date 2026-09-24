namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

public class McpServerEntryTests
{
    [Fact]
    public void AddArg_AddsArgItemToCollection()
    {
        McpServerEntry entry = new("srv");
        entry.NewArg = "--verbose";
        entry.AddArgCommand.Execute(null);

        Assert.Single(entry.Args);
        Assert.Equal("--verbose", entry.Args[0].Value);
        Assert.Equal(string.Empty, entry.NewArg);
    }

    [Fact]
    public void RemoveArg_RemovesItem()
    {
        McpServerEntry entry = new("srv");
        entry.NewArg = "-v";
        entry.AddArgCommand.Execute(null);
        entry.RemoveArgCommand.Execute(entry.Args[0]);

        Assert.Empty(entry.Args);
    }

    [Fact]
    public void AddEnv_AddsEnvVar()
    {
        McpServerEntry entry = new("srv");
        entry.NewEnvKey = "DEBUG";
        entry.NewEnvValue = "1";
        entry.AddEnvCommand.Execute(null);

        Assert.Single(entry.Env);
        Assert.Equal("DEBUG", entry.Env[0].Key);
        Assert.Equal("1", entry.Env[0].Value);
        Assert.Equal(string.Empty, entry.NewEnvKey);
    }

    [Fact]
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

        Assert.Equal("npx", entry.Command);
        Assert.Equal(2, entry.Args.Count);
        Assert.Equal("-y", entry.Args[0].Value);
        Assert.Single(entry.Env);
        Assert.Equal("NODE_ENV", entry.Env[0].Key);
    }

    [Fact]
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

        Assert.Equal("http", json["type"]!.GetValue<string>());
        Assert.Equal("http://localhost:9000", json["url"]!.GetValue<string>());
        Assert.Null(json["args"]); // no args
        JsonObject? env = json["env"] as JsonObject;
        Assert.NotNull(env);
        Assert.Equal("abc", env!["TOKEN"]!.GetValue<string>());
    }

    [Fact]
    public void AddArg_IgnoresEmptyText()
    {
        McpServerEntry entry = new("srv");
        entry.NewArg = "   ";
        entry.AddArgCommand.Execute(null);

        Assert.Empty(entry.Args);
    }

    // ── Transport-level validation ────────────────────────────────────────────

    [Fact]
    public void StdioTransport_CommandMissing_WhenCommandBlank()
    {
        McpServerEntry entry = new("srv") { Type = "stdio", Command = "" };
        Assert.True(entry.CommandMissing,
            "CommandMissing must be true when transport is stdio and Command is blank.");
        Assert.True(entry.HasValidationError,
            "HasValidationError must be true when CommandMissing is true.");
        Assert.False(string.IsNullOrEmpty(entry.ValidationMessage),
            "ValidationMessage must be non-empty when there is a validation error.");
    }

    [Fact]
    public void StdioTransport_CommandNotMissing_WhenCommandPresent()
    {
        McpServerEntry entry = new("srv") { Type = "stdio", Command = "node server.js" };
        Assert.False(entry.CommandMissing,
            "CommandMissing must be false when transport is stdio and Command is non-blank.");
        Assert.False(entry.HasValidationError,
            "HasValidationError must be false when stdio entry has a command.");
        MessageAssert.Equal(string.Empty, entry.ValidationMessage,
            "ValidationMessage must be empty when entry is valid.");
    }

    [Fact]
    public void SseTransport_UrlInvalid_WhenUrlNotHttps()
    {
        McpServerEntry entry = new("srv") { Type = "sse", Url = "ftp://example.com/mcp" };
        Assert.True(entry.UrlInvalid,
            "UrlInvalid must be true when transport is sse and URL scheme is not http/https.");
        Assert.True(entry.HasValidationError,
            "HasValidationError must be true when UrlInvalid is true.");
        Assert.False(string.IsNullOrEmpty(entry.ValidationMessage),
            "ValidationMessage must be non-empty when there is a URL validation error.");
    }

    [Fact]
    public void SseTransport_UrlValid_WhenUrlIsHttps()
    {
        McpServerEntry entry = new("srv") { Type = "sse", Url = "https://example.com/mcp" };
        Assert.False(entry.UrlInvalid,
            "UrlInvalid must be false when transport is sse and URL is a valid https address.");
        Assert.False(entry.HasValidationError,
            "HasValidationError must be false when sse entry has a valid URL.");
        MessageAssert.Equal(string.Empty, entry.ValidationMessage,
            "ValidationMessage must be empty when entry is valid.");
    }

    [Fact]
    public void HasValidationError_FalseWhenValid()
    {
        // http transport with a valid http URL must also pass validation.
        McpServerEntry entry = new("srv") { Type = "http", Url = "http://localhost:8080/mcp" };
        Assert.False(entry.CommandMissing,
            "CommandMissing must be false for http transport.");
        Assert.False(entry.UrlInvalid,
            "UrlInvalid must be false for http transport with a valid http URL.");
        Assert.False(entry.HasValidationError,
            "HasValidationError must be false when no constraint is violated.");
    }
}