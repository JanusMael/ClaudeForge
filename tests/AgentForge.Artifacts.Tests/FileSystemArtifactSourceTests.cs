namespace Bennewitz.Ninja.AgentForge.Artifacts.Tests;

/// <summary>
/// The three filesystem sources: what they name an entry, and what they refuse to surface.
/// </summary>
/// <remarks>
/// ⚠ <b>The naming rules are the interesting half, not the walking.</b> A source's entry name is
/// the identity resolution groups by, so a name that is too coarse invents a shadowing
/// relationship that does not exist — two rule files in different subdirectories, or two unrelated
/// tools' <c>AGENTS.md</c>, becoming "one artifact declared twice". Each naming test below exists
/// because getting it wrong produces a confident false statement rather than a visible failure.
/// </remarks>
[TestClass]
public sealed class FileSystemArtifactSourceTests
{
    private static readonly ArtifactScope Scope = new("test", "Test", 10);

    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "agentforge-artifacts-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Leave the temp directory if something still holds a handle.
        }
    }

    private string Write(string relativePath, string content = "x")
    {
        string full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private static ArtifactSourceIdentity Identity(ArtifactKind kind = ArtifactKind.Rule)
    {
        return new ArtifactSourceIdentity("src", kind, Scope);
    }

    private static DirectoryArtifactSource Walk(
        string root, string pattern = "*.md", bool recursive = false, bool stripExtension = true)
    {
        return new DirectoryArtifactSource(Identity(), root, pattern, recursive, stripExtension);
    }

    // ── FileProbeArtifactSource ──────────────────────────────────────────────

    [TestMethod]
    public void Probe_OnlyExistingPathsBecomeEntries()
    {
        string present = Write("CLAUDE.md");
        var source = new FileProbeArtifactSource(
            Identity(ArtifactKind.Memory),
            [
                new ArtifactProbe("CLAUDE", present),
                new ArtifactProbe("AGENTS", Path.Combine(_root, "AGENTS.md")),
            ]);

        ArtifactRef[] entries = [.. source.Enumerate()];

        Assert.AreEqual(1, entries.Length);
        Assert.AreEqual("CLAUDE", entries[0].Name);
        Assert.AreEqual(present, entries[0].Location);
        Assert.AreEqual(ArtifactForm.File, entries[0].Form);
        Assert.AreEqual("src", entries[0].SourceId);
    }

    [TestMethod]
    public void Probe_NameIsTheCallersNotTheFilesystems()
    {
        // The caller names a probe because the file name alone often does not say what the file
        // IS — a sibling tool's AGENTS.md is not this tool's AGENTS.md, and resolution would
        // group them if both were called "AGENTS".
        string path = Write("AGENTS.md");
        var source = new FileProbeArtifactSource(
            Identity(ArtifactKind.Memory), [new ArtifactProbe(".codex/AGENTS", path)]);

        Assert.AreEqual(".codex/AGENTS", source.Enumerate().Single().Name);
    }

    [TestMethod]
    public void Probe_MissingDirectoryIsNotAnError()
    {
        var source = new FileProbeArtifactSource(
            Identity(),
            [new ArtifactProbe("gone", Path.Combine(_root, "no-such-dir", "file.md"))]);

        Assert.AreEqual(0, source.Enumerate().Count());
    }

    // ── DirectoryArtifactSource ──────────────────────────────────────────────

    [TestMethod]
    public void Directory_MissingRootYieldsNothing_AndDoesNotThrow()
    {
        Assert.AreEqual(0, Walk(Path.Combine(_root, "absent")).Enumerate().Count());
    }

    [TestMethod]
    public void Directory_NonRecursive_IgnoresSubdirectories()
    {
        Write("top.md");
        Write("nested/deep.md");

        string[] names = [.. Walk(_root).Enumerate().Select(e => e.Name)];

        CollectionAssert.AreEquivalent(new[] { "top" }, names);
    }

    [TestMethod]
    public void Directory_Recursive_NamesCarryTheRelativePath()
    {
        // ⚠ The whole point: rules/common/security.md and rules/csharp/security.md are two
        // artifacts, and the tool reads both. Naming them both "security" would make resolution
        // report one shadowing the other — a confident false statement.
        Write("common/security.md");
        Write("csharp/security.md");

        string[] names = [.. Walk(_root, recursive: true).Enumerate().Select(e => e.Name)];

        CollectionAssert.AreEquivalent(new[] { "common/security", "csharp/security" }, names);
    }

    [TestMethod]
    public void Directory_RelativeNamesUseForwardSlashes_OnEveryPlatform()
    {
        // The name is an identity that travels into UI, comparisons and eventually config; a
        // backslash on Windows and a slash elsewhere would make the same artifact compare unequal
        // across machines.
        Write("nested/inner/file.md");

        string name = Walk(_root, recursive: true).Enumerate().Single().Name;

        Assert.AreEqual("nested/inner/file", name);
        Assert.IsFalse(name.Contains('\\', StringComparison.Ordinal));
    }

    [TestMethod]
    public void Directory_StripExtensionFalse_KeepsIt()
    {
        // Configuration files are identified WITH their extension: "settings" alone is meaningless
        // where "settings.json" is the file the tool reads.
        Write("10-policy.json");

        string name = Walk(_root, pattern: "*.json", stripExtension: false).Enumerate().Single().Name;

        Assert.AreEqual("10-policy.json", name);
    }

    [TestMethod]
    public void Directory_StarPattern_TakesEveryExtension()
    {
        // Hook scripts have no agreed extension — .sh, .py, or none at all.
        Write("precommit.sh");
        Write("format.py");
        Write("runner");

        string[] names = [.. Walk(_root, pattern: "*").Enumerate().Select(e => e.Name)];

        CollectionAssert.AreEquivalent(new[] { "precommit", "format", "runner" }, names);
    }

    [TestMethod]
    public void Directory_ExcludedSuffixes_AreSkipped_CaseInsensitively()
    {
        Write("real.md");
        Write("real.md.bak");
        Write("other.md.BAK");

        var source = new DirectoryArtifactSource(Identity(), _root, "*", recursive: false, stripExtension: true)
        {
            ExcludedSuffixes = [".bak"],
        };

        CollectionAssert.AreEquivalent(new[] { "real" }, source.Enumerate().Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void Directory_ExcludedSuffixes_DefaultToNone()
    {
        // Which sidecars count as noise is the caller's policy. A default here would silently
        // apply one product's judgement to every source.
        Write("real.md.bak");

        Assert.AreEqual(1, Walk(_root, pattern: "*").Enumerate().Count());
    }

    [TestMethod]
    public void Directory_NamePrefix_QualifiesTheIdentity()
    {
        Write("notes.md");

        var source = new DirectoryArtifactSource(Identity(ArtifactKind.Memory), _root, "*.md",
            recursive: false, stripExtension: true)
        {
            NamePrefix = ".opencode/",
        };

        Assert.AreEqual(".opencode/notes", source.Enumerate().Single().Name);
    }

    // ── SkillDirectoryArtifactSource ─────────────────────────────────────────

    [TestMethod]
    public void Skills_NameIsTheDirectory_NotTheManifestFile()
    {
        // Every skill's file is called SKILL.md. Naming entries after the file would make every
        // skill in a root one artifact with N entries all shadowing each other.
        Write(Path.Combine("python-patterns", "SKILL.md"));
        Write(Path.Combine("git-flow", "SKILL.md"));

        var source = new SkillDirectoryArtifactSource(
            Identity(ArtifactKind.Skill), _root, "SKILL.md");

        CollectionAssert.AreEquivalent(
            new[] { "python-patterns", "git-flow" },
            source.Enumerate().Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void Skills_LocationIsTheManifest_NotTheDirectory()
    {
        Write(Path.Combine("python-patterns", "SKILL.md"));

        var source = new SkillDirectoryArtifactSource(Identity(ArtifactKind.Skill), _root, "SKILL.md");

        StringAssert.EndsWith(source.Enumerate().Single().Location, "SKILL.md");
    }

    [TestMethod]
    public void Skills_DirectoryWithoutTheManifest_IsNotASkill()
    {
        Write(Path.Combine("real-skill", "SKILL.md"));
        Write(Path.Combine("not-a-skill", "notes.md"));

        var source = new SkillDirectoryArtifactSource(Identity(ArtifactKind.Skill), _root, "SKILL.md");

        Assert.AreEqual("real-skill", source.Enumerate().Single().Name);
    }

    [TestMethod]
    public void Skills_MissingRootYieldsNothing_AndDoesNotThrow()
    {
        var source = new SkillDirectoryArtifactSource(
            Identity(ArtifactKind.Skill), Path.Combine(_root, "absent"), "SKILL.md");

        Assert.AreEqual(0, source.Enumerate().Count());
    }

    // ── The contract the resolver depends on ─────────────────────────────────

    [TestMethod]
    public void EveryEntryCarriesTheSourcesIdentity()
    {
        // SourceId is how a consumer recovers what an entry cannot say for itself — the memory
        // inventory maps it back to a category, and a chain uses it to say "the global skills root"
        // rather than merely "global".
        Write("one.md");
        Write(Path.Combine("skill", "SKILL.md"));

        List<ArtifactRef> entries =
        [
            .. new DirectoryArtifactSource(
                new ArtifactSourceIdentity("walker", ArtifactKind.Rule, Scope), _root, "*.md",
                recursive: false, stripExtension: true).Enumerate(),
            .. new SkillDirectoryArtifactSource(
                new ArtifactSourceIdentity("skiller", ArtifactKind.Skill, Scope), _root, "SKILL.md")
                .Enumerate(),
        ];

        Assert.AreEqual(2, entries.Count);
        Assert.IsTrue(entries.All(e => e.Scope == Scope));
        CollectionAssert.AreEquivalent(new[] { "walker", "skiller" }, entries.Select(e => e.SourceId).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { ArtifactKind.Rule, ArtifactKind.Skill }, entries.Select(e => e.Kind).ToArray());
    }
}
