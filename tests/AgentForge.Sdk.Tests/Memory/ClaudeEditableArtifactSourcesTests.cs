using Bennewitz.Ninja.AgentForge.Artifacts;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Memory;

/// <summary>
/// The editable surface's source list, and the plugin tree that shaped
/// <see cref="IArtifactSource"/>.
/// </summary>
/// <remarks>
/// ⚠ <b>What <c>EditableMemoryServiceTests</c> cannot see.</b> That suite asserts the flattened
/// row list, so a plugin artifact and a user artifact of the same name look like two unrelated rows
/// whether they are or not. Every claim here is about identity and traversal — the parts that are
/// invisible once the chain is flattened, and therefore the parts that can be quietly wrong.
/// </remarks>
[TestClass]
public sealed class ClaudeEditableArtifactSourcesTests
{
    private string _sandbox = null!;

    private string Home => Path.Combine(_sandbox, ".claude");

    /// <summary>The sandbox, addressed the way production addresses a real profile.</summary>
    private ClaudeArtifactPaths Paths => new(_sandbox);

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudeforge-editable-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Home);
        PlatformPaths.TestUserProfileOverride = _sandbox;
    }

    [TestCleanup]
    public void Cleanup()
    {
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
            // Leave the temp directory if something still holds a handle.
        }
    }

    private static void Write(string path, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private IReadOnlyList<ResolvedArtifact> Resolve(string? projectRoot = null)
    {
        return ArtifactResolver.Resolve(
            ClaudeEditableArtifactSources.ForEditor(Paths, projectRoot).Select(s => s.Source));
    }

    // ── The source list itself ───────────────────────────────────────────────

    [TestMethod]
    public void EverySourceIdIsUnique()
    {
        // The editor maps SourceId back to an editing scope through a dictionary — a duplicated id
        // throws while building it, i.e. the Agents & Skills page fails to load at all.
        foreach (string? projectRoot in new[] { null, Path.Combine(_sandbox, "proj") })
        {
            string[] ids = [.. ClaudeEditableArtifactSources.ForEditor(Paths, projectRoot)
                                                            .Select(s => s.Source.Id)];

            CollectionAssert.AllItemsAreUnique(
                ids, $"Duplicate source id with projectRoot={projectRoot ?? "(none)"}.");
        }
    }

    [TestMethod]
    public void OnlyTheThreeEditableKindsAreProduced()
    {
        // EditableMemoryService maps kind → category with a switch that throws on anything else,
        // so a source wired to the wrong kind is a crash rather than a mislabelled row. This is
        // the guard that catches it at the list, before a user's machine does.
        Write(Path.Combine(Home, "agents", "a.md"));
        Write(Path.Combine(Home, "commands", "c.md"));
        Write(Path.Combine(Home, "skills", "s", "SKILL.md"));
        Write(Path.Combine(Home, "plugins", "p", "agents", "pa.md"));
        Write(Path.Combine(Home, "plugins", "p", "commands", "pc.md"));
        Write(Path.Combine(Home, "plugins", "p", "skills", "ps", "SKILL.md"));

        ArtifactKind[] kinds = [.. Resolve().SelectMany(a => a.Entries).Select(e => e.Kind).Distinct()];

        CollectionAssert.AreEquivalent(
            new[] { ArtifactKind.Agent, ArtifactKind.Command, ArtifactKind.Skill }, kinds);
    }

    // ── Identity across scopes ───────────────────────────────────────────────

    [TestMethod]
    public void AUserAgentAndAPluginAgentOfTheSameName_ResolveAsOneChain_UserFirst()
    {
        // ⭐ The relationship the old walk could not express. Both rows still appear on the page —
        // see the companion test below — but the chain now says which is the user's own.
        Write(Path.Combine(Home, "agents", "reviewer.md"));
        Write(Path.Combine(Home, "plugins", "some-plugin", "agents", "reviewer.md"));

        ResolvedArtifact reviewer = Resolve()
            .Single(a => a.Kind == ArtifactKind.Agent && a.Name == "reviewer");

        Assert.IsTrue(reviewer.IsShadowed);
        Assert.AreEqual(ClaudeScopes.User, reviewer.Effective.Scope);
        Assert.AreEqual("some-plugin", reviewer.Shadowed.Single().Scope.DisplayName);
    }

    [TestMethod]
    public void BothCopiesAreStillListed_GroupingIsNotFiltering()
    {
        // ⛔ The failure mode: listing `Effective` instead of `Entries` would hide the plugin's
        // copy the moment the user happened to have a same-named agent — so the page would show
        // fewer artifacts than the user has, silently, and only for the people with a collision.
        Write(Path.Combine(Home, "agents", "reviewer.md"));
        Write(Path.Combine(Home, "plugins", "some-plugin", "agents", "reviewer.md"));

        List<EditableMemoryEntry> rows =
            [.. EditableMemoryService.Snapshot().Where(e => e.DisplayName == "reviewer")];

        Assert.AreEqual(2, rows.Count);
        Assert.IsTrue(rows.Any(r => r is { Scope: EditableMemoryScope.User, IsWritable: true }));
        Assert.IsTrue(rows.Any(r => r is { Scope: EditableMemoryScope.Plugin, IsWritable: false }));
    }

    [TestMethod]
    public void TwoPluginsWithTheSameSkillName_StayDistinguishableByScope()
    {
        // A plugin is a scope, and its display name is what the row shows. Collapsing all plugins
        // into one scope would make these two rows identical on screen.
        Write(Path.Combine(Home, "plugins", "alpha", "skills", "widget", "SKILL.md"));
        Write(Path.Combine(Home, "plugins", "beta", "skills", "widget", "SKILL.md"));

        ResolvedArtifact widget = Resolve().Single(a => a.Kind == ArtifactKind.Skill && a.Name == "widget");

        Assert.AreEqual(2, widget.Entries.Count);
        CollectionAssert.AreEquivalent(
            new[] { "alpha", "beta" }, widget.Entries.Select(e => e.Scope.DisplayName).ToArray());
    }

    // ── Traversal bounds ─────────────────────────────────────────────────────

    [TestMethod]
    public void PluginWalk_StopsAtItsDepthBound()
    {
        // Plugins are git repositories, so an unbounded walk crawls their dependency trees. The
        // bound was never tested — only the node_modules skip was, and the two are independent
        // (a deep tree of ordinarily-named directories skips nothing).
        Write(Path.Combine(Home, "plugins", "a", "b", "c", "d", "e", "f", "SKILL.md"));
        Write(Path.Combine(Home, "plugins", "a", "b", "c", "d", "e", "f", "g", "SKILL.md"));

        string[] names = [.. Resolve().Where(a => a.Kind == ArtifactKind.Skill).Select(a => a.Name)];

        CollectionAssert.Contains(names, "f", "Depth 6 is inside the bound.");
        CollectionAssert.DoesNotContain(names, "g", "Depth 7 is past the bound and must not be walked.");
    }

    [TestMethod]
    public void PluginWalk_SkipsDotDirectories()
    {
        // A plugin is a git repo, so .git is always there — and any other dot-directory is
        // similarly tooling rather than content.
        Write(Path.Combine(Home, "plugins", "p", ".hidden", "skills", "x", "SKILL.md"));
        Write(Path.Combine(Home, "plugins", "p", "skills", "real", "SKILL.md"));

        string[] names = [.. Resolve().Where(a => a.Kind == ArtifactKind.Skill).Select(a => a.Name)];

        CollectionAssert.Contains(names, "real");
        CollectionAssert.DoesNotContain(names, "x");
    }

    [TestMethod]
    public void PluginsDirectoryAbsent_YieldsNothing_AndDoesNotThrow()
    {
        Write(Path.Combine(Home, "agents", "only-user.md"));

        Assert.AreEqual(1, Resolve().Count);
    }
}
