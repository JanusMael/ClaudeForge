using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Sdk;

/// <summary>
/// Tests for <see cref="IClaudeConfigClient.SearchSchema"/>.
/// Uses a sandboxed profile path so <c>ConfigFileDiscoverer</c> reads/writes
/// within the temp directory, never touching the user's real <c>~/.claude/</c>.
/// The bundled schema resource is always available; no HTTP calls are made.
/// </summary>
[TestClass]
public sealed class SearchSchemaTests
{
    private string _sandbox = null!;

    [TestInitialize]
    public void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_sandbox, ".claude"));
        PlatformPaths.TestUserProfileOverride = _sandbox;
    }

    [TestCleanup]
    public void Cleanup()
    {
        // Reset before attempting disk cleanup so later tests don't inherit a stale override
        // even if the Directory.Delete call below throws.
        PlatformPaths.TestUserProfileOverride = null;
        if (!Directory.Exists(_sandbox))
        {
            return;
        }

        try
        {
            Directory.Delete(_sandbox, recursive: true);
        }
        catch (IOException)
        {
            // Windows Defender / file-system indexer may hold a transient lock on the schema
            // cache file written by SchemaRegistry.  The temp directory is cleaned up by the OS
            // on next boot; swallowing the error here keeps the test from being marked as failed
            // by a TestCleanup exception that is unrelated to the code under test.
        }
        catch (UnauthorizedAccessException)
        {
            /* same */
        }
    }

    // ── Before open ───────────────────────────────────────────────────────

    [TestMethod]
    public void SearchSchema_BeforeOpen_ReturnsEmpty()
    {
        // Schema nodes are populated during OpenAsync; before that the cache is
        // null and SearchSchema should return gracefully, not throw.
        ClaudeCodeClient client = new();
        try
        {
            IReadOnlyList<SchemaSearchResult> results = client.SearchSchema("model");
            Assert.AreEqual(0, results.Count,
                "SearchSchema before OpenAsync must return empty, not throw.");
        }
        finally
        {
            client.Dispose();
        }
    }

    [TestMethod]
    public void SearchSchema_EmptyQuery_ReturnsEmpty()
    {
        ClaudeCodeClient client = new();
        try
        {
            IReadOnlyList<SchemaSearchResult> results = client.SearchSchema(string.Empty);
            Assert.AreEqual(0, results.Count);

            results = client.SearchSchema("   ");
            Assert.AreEqual(0, results.Count);
        }
        finally
        {
            client.Dispose();
        }
    }

    // ── After open: content matching ──────────────────────────────────────

    private static async Task<ClaudeCodeClient> OpenedClientAsync()
    {
        ClaudeCodeClient client = new();
        // null project root → User-only workspace; settings file may be absent
        // (ConfigFileLoader creates a placeholder document).
        await client.OpenAsync(null, CancellationToken.None);
        return client;
    }

    [TestMethod]
    public async Task SearchSchema_FindsByPropertyName()
    {
        using ClaudeCodeClient client = await OpenedClientAsync();

        IReadOnlyList<SchemaSearchResult> results = client.SearchSchema("model");

        Assert.IsTrue(results.Count > 0, "Searching 'model' must find the model property.");
        Assert.IsTrue(results.Any(r => r.Name.Equals("model", StringComparison.OrdinalIgnoreCase)),
            "At least one result should have Name == 'model'.");
    }

    [TestMethod]
    public async Task SearchSchema_FindsByTitle()
    {
        using ClaudeCodeClient client = await OpenedClientAsync();

        // The Claude Code schema's 'model' property has Title "Model" or similar.
        // Use a general capitalized term that appears as a title in the schema.
        IReadOnlyList<SchemaSearchResult> results = client.SearchSchema("Model");

        Assert.IsTrue(results.Count > 0, "Title-based search must return results.");
        // Every result should have matched somewhere (not necessarily title).
        Assert.IsTrue(results.All(r =>
                r.Name.Contains("model", StringComparison.OrdinalIgnoreCase) ||
                r.Title.Contains("model", StringComparison.OrdinalIgnoreCase) ||
                r.Description.Contains("model", StringComparison.OrdinalIgnoreCase) ||
                r.JsonPath.Contains("model", StringComparison.OrdinalIgnoreCase)),
            "Each result must contain 'model' in at least one field.");
    }

    [TestMethod]
    public async Task SearchSchema_FindsByDescription()
    {
        using ClaudeCodeClient client = await OpenedClientAsync();

        // "permission" appears in descriptions of permission-related properties.
        IReadOnlyList<SchemaSearchResult> results = client.SearchSchema("permission");

        Assert.IsTrue(results.Count > 0,
            "Description-based search for 'permission' must return results.");
    }

    [TestMethod]
    public async Task SearchSchema_FindsByJsonPath_DottedPath()
    {
        using ClaudeCodeClient client = await OpenedClientAsync();

        // permissions.allow / permissions.deny / hooks.PreToolUse etc. are now
        // searchable nodes — SchemaTreeBuilder recurses into Complex-typed nodes
        // that expose fixed named properties (the "properties" JSON Schema keyword).
        // Dynamic bags like "mcpServers" (uses "additionalProperties") remain leaf nodes.
        IReadOnlyList<SchemaSearchResult> results = client.SearchSchema("permissions.allow");
        Assert.IsTrue(results.Count > 0,
            "Searching 'permissions.allow' must return results after Complex-recursion fix.");
        Assert.IsTrue(results.Any(r => r.JsonPath.Equals("permissions.allow", StringComparison.OrdinalIgnoreCase)),
            "The exact node 'permissions.allow' must be found when searching for its path.");
    }

    [TestMethod]
    public async Task SearchSchema_PartialPath_MatchesAllNestedNodes()
    {
        using ClaudeCodeClient client = await OpenedClientAsync();

        // Searching the parent prefix "permissions" must return the parent node
        // AND all its fixed named sub-properties (allow, deny, ask, …).
        IReadOnlyList<SchemaSearchResult> results = client.SearchSchema("permissions");
        List<string> paths = results.Select(r => r.JsonPath).ToList();

        Assert.IsTrue(paths.Contains("permissions"),
            "The 'permissions' top-level node must appear in results for 'permissions' query.");
        Assert.IsTrue(paths.Contains("permissions.allow"),
            "'permissions.allow' must be reachable via the 'permissions' prefix query.");
        Assert.IsTrue(paths.Contains("permissions.deny"),
            "'permissions.deny' must be reachable via the 'permissions' prefix query.");
    }

    [TestMethod]
    public async Task SearchSchema_CaseInsensitive()
    {
        using ClaudeCodeClient client = await OpenedClientAsync();

        IReadOnlyList<SchemaSearchResult> lower = client.SearchSchema("model");
        IReadOnlyList<SchemaSearchResult> upper = client.SearchSchema("MODEL");

        Assert.AreEqual(lower.Count, upper.Count,
            "Case should not affect result count — search is case-insensitive.");
        CollectionAssert.AreEqual(
            lower.Select(r => r.JsonPath).ToArray(),
            upper.Select(r => r.JsonPath).ToArray(),
            "Case-insensitive search must return the same paths in the same order.");
    }

    [TestMethod]
    public async Task SearchSchema_MaxResults_Caps_At_Requested_Limit()
    {
        using ClaudeCodeClient client = await OpenedClientAsync();

        // A two-letter query that matches many properties.
        IReadOnlyList<SchemaSearchResult> results = client.SearchSchema("is", maxResults: 3);

        Assert.IsTrue(results.Count <= 3,
            $"maxResults:3 must cap results to at most 3; got {results.Count}.");
    }

    [TestMethod]
    public async Task SearchSchema_ResultFields_AllPopulated()
    {
        using ClaudeCodeClient client = await OpenedClientAsync();

        IReadOnlyList<SchemaSearchResult> results = client.SearchSchema("model");

        Assert.IsTrue(results.Count > 0);
        SchemaSearchResult first = results[0];
        Assert.IsFalse(string.IsNullOrWhiteSpace(first.JsonPath), "JsonPath must not be empty.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(first.Name), "Name must not be empty.");
        // Title may fall back to Name; it should never be null/empty.
        Assert.IsFalse(string.IsNullOrWhiteSpace(first.Title), "Title must not be empty.");
    }

    [TestMethod]
    public async Task SearchSchema_DenyNotReturnedFor_PermissionsAllow_Query()
    {
        using ClaudeCodeClient client = await OpenedClientAsync();

        // Searching for the exact path "permissions.allow" must NOT return
        // "permissions.deny" because "permissions.deny" does not contain
        // the literal string "permissions.allow".
        IReadOnlyList<SchemaSearchResult> results = client.SearchSchema("permissions.allow");
        List<string> paths = results.Select(r => r.JsonPath).ToList();

        Assert.IsFalse(paths.Contains("permissions.deny"),
            "'permissions.deny' must not appear in results for 'permissions.allow' query.");
    }

    [TestMethod]
    public async Task SearchSchema_Dispose_DoesNotThrow()
    {
        // Dispose while client is idle must not throw.
        ClaudeCodeClient client = await OpenedClientAsync();
        _ = client.SearchSchema("model"); // warm up
        client.Dispose();
        client.Dispose(); // idempotent
    }
}