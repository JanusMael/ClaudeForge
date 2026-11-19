using System.Net;
using System.Text;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using SchemaRegistry = Bennewitz.Ninja.AgentForge.Core.Schema.SchemaRegistry;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Schema;

/// <summary>
/// The hook metadata Claude's client reads describes whichever copy of the schema loaded.
/// </summary>
/// <remarks>
/// The load order itself is tested on a neutral sample in <c>AgentForge.Core.Tests</c>, under the
/// same class name. These two need Claude's data: only Claude Code's schema declares
/// <c>$defs.hookCommand</c>, and only Claude's client reads it.
/// </remarks>
public sealed class SchemaLoadPrecedenceTests
{
    /// <summary>A property name no real schema will ever declare.</summary>
    private const string NetworkSentinelProperty = "zzzFetchedFromNetworkSentinel";

    /// <summary>Serves one canned body to every request.</summary>
    /// <remarks>
    /// Keeps every response it hands out and disposes them with itself, so none outlives the test
    /// whether or not the caller disposes it.
    /// </remarks>
    private sealed class CannedHandler(string body) : HttpMessageHandler
    {
        private readonly List<HttpResponseMessage> _responses = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            lock (_responses)
            {
                _responses.Add(response);
            }

            return Task.FromResult(response);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_responses)
                {
                    foreach (HttpResponseMessage response in _responses)
                    {
                        response.Dispose();
                    }

                    _responses.Clear();
                }
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>A schema declaring one sentinel property and nothing else.</summary>
    /// <remarks>
    /// No <c>$id</c>: JsonSchema.Net registers by <c>$id</c> globally, and this document must
    /// never collide with the real schema.
    /// </remarks>
    private static string SentinelSchema(string propertyName) =>
        $$"""
          {
            "$schema": "http://json-schema.org/draft-07/schema#",
            "type": "object",
            "properties": {
              "{{propertyName}}": { "type": "string" }
            }
          }
          """;

    /// <summary>
    /// ⛔ The hook-metadata reader describes the copy that loaded, not always the bundled one.
    /// </summary>
    /// <remarks>
    /// These readers used to go to the bundled resource and were right by construction, because
    /// bundled always won. Now that a fetch can win, the static overload would describe a
    /// different document than the tree was built from — silently, since both parse fine. The
    /// served body declares no <c>$defs.hookCommand</c>, so the instance reader must report
    /// none while the static still finds the bundled ones.
    /// </remarks>
    [Fact]
    public async Task TheHookMetadataReader_FollowsTheCopyThatActuallyLoaded()
    {
        CannedHandler handler = new(SentinelSchema(NetworkSentinelProperty));
        using SchemaRegistry registry = new(new HttpClient(handler));
        ProductDescriptor claudeCode = SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty);

        Assert.True(
            SchemaRegistry.GetHookCommandVariants(BundledProduct.Named("claude-code-settings.json")).Count > 0,
            "Premise: the BUNDLED Claude Code schema does declare hook command variants.");

        await registry.GetSettingsNodeAsync(claudeCode, TestContext.Current.CancellationToken);

        MessageAssert.Equal(
            0,
            registry.GetHookCommandVariantsFor(claudeCode).Count,
            "The instance reader still reported the bundled hook variants after a fetched copy "
            + "won the load. The hook editor would offer shapes the loaded schema does not "
            + "define, with nothing failing anywhere.");
    }

    /// <summary>
    /// <c>ClaudeConfigClientBase</c> uses the INSTANCE metadata readers, not the statics.
    /// </summary>
    /// <remarks>
    /// ⭐ <b>A source-text seam guard, because the behavioural one measured nothing.</b> A canary
    /// that reverted the SDK to <c>SchemaRegistry.GetHookEvents(...)</c> reddened zero tests: the
    /// registry-level contract is covered by
    /// <see cref="TheHookMetadataReader_FollowsTheCopyThatActuallyLoaded"/>, but nothing observed
    /// which overload the SDK picks. The static reads bundled, so choosing it would make the hook
    /// editor describe a document the client did not load — silently, since both parse.
    /// <para>
    /// Source text rather than reflection: both overloads exist and are legitimately callable, so
    /// there is no per-type metadata that distinguishes "called the right one".
    /// </para>
    /// </remarks>
    [Fact]
    public void TheClaudeSdkReadsHookMetadataFromItsOwnRegistryInstance()
    {
        string path = Path.Combine(
            RepoRoot(), "src", "ClaudeForge.Sdk.Claude", "ClaudeConfigClientBase.cs");
        Assert.True(File.Exists(path), $"'{path}' not found.");

        string source = File.ReadAllText(path);

        foreach (string bundledOnly in new[]
        {
            "SchemaRegistry.GetHookEvents(",
            "SchemaRegistry.GetHookCommandVariants(",
        })
        {
            Assert.False(
                source.Contains(bundledOnly, StringComparison.Ordinal),
                $"ClaudeConfigClientBase calls the static '{bundledOnly}', which always reads the "
                + "BUNDLED schema. Use the instance overload on SchemaRegistryInstance so the "
                + "metadata describes whichever copy this client actually loaded.");
        }

        Assert.True(
            source.Contains("SchemaRegistryInstance.GetHookEventsFor(", StringComparison.Ordinal),
            "Premise: the file should be calling the instance overload. If this fails the scan "
            + "has lost its subject and the assertions above pass vacuously.");
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "tests")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root by walking up from '{AppContext.BaseDirectory}'.");
    }
}
