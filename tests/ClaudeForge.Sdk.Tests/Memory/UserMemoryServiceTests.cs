using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Memory;

[TestClass]
public class UserMemoryServiceTests
{
    private string _fakeHome = null!;
    private string _claudeHome => Path.Combine(_fakeHome, ".claude");

    [TestInitialize]
    public void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(),
            "claudeforge-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_claudeHome);
        PlatformPaths.TestUserProfileOverride = _fakeHome;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        if (Directory.Exists(_fakeHome))
        {
            try
            {
                Directory.Delete(_fakeHome, recursive: true);
            }
            catch
            {
                /* leave the temp dir if it's still locked */
            }
        }
    }

    private void Write(string relPath, string content)
    {
        string full = Path.Combine(_claudeHome, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    // -----------------------------------------------------------------------

    [TestMethod]
    public void Empty_ClaudeHome_YieldsEmptyList()
    {
        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles();
        Assert.AreEqual(0, files.Count);
    }

    [TestMethod]
    public void PrimaryMemory_BothCLAUDEAndAGENTS_BothAppear()
    {
        Write("CLAUDE.md", "# claude\nbody");
        Write("AGENTS.md", "# agents\nbody");

        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles();
        List<UserMemoryFile> primary = files.Where(f => f.Category == UserMemoryCategory.PrimaryMemory).ToList();
        Assert.AreEqual(2, primary.Count);
        CollectionAssert.AreEquivalent(
            new[] { "CLAUDE", "AGENTS" },
            primary.Select(f => f.DisplayName).ToArray());
    }

    [TestMethod]
    public void Subagent_AgentsDirectory_SurfacesMdFiles()
    {
        Write("agents/code-reviewer.md", "# reviewer\nlooks at code");
        Write("agents/tdd-guide.md", "# tdd");

        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles();
        List<UserMemoryFile> subagents = files.Where(f => f.Category == UserMemoryCategory.Subagent).ToList();
        Assert.AreEqual(2, subagents.Count);
    }

    [TestMethod]
    public void Subagent_NonMdFiles_AreIgnored()
    {
        Write("agents/notes.txt", "irrelevant");
        Write("agents/agent.md", "# real");

        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles();
        Assert.AreEqual(1, files.Count(f => f.Category == UserMemoryCategory.Subagent));
    }

    [TestMethod]
    public void Hook_AnyExtension_Surfaces()
    {
        // hooks/ uses "*" so .sh / .py / .js / no-extension all qualify.
        Write("hooks/precommit.sh", "#!/bin/bash");
        Write("hooks/format.py", "import sys");
        Write("hooks/runner", "shebangless");

        List<UserMemoryFile> hooks = UserMemoryService.SnapshotFiles()
                                                      .Where(f => f.Category == UserMemoryCategory.Hook)
                                                      .ToList();
        Assert.AreEqual(3, hooks.Count);
    }

    [TestMethod]
    public void Rules_AreEnumeratedRecursively()
    {
        Write("rules/common/security.md", "...");
        Write("rules/common/coding.md", "...");
        Write("rules/csharp/patterns.md", "...");

        List<UserMemoryFile> rules = UserMemoryService.SnapshotFiles()
                                                      .Where(f => f.Category == UserMemoryCategory.Rule)
                                                      .ToList();
        Assert.AreEqual(3, rules.Count);
    }

    [TestMethod]
    public void Skills_OnlySkillMd_Surfaces()
    {
        Write("skills/python-patterns/SKILL.md", "# python patterns\n...");
        Write("skills/python-patterns/extra.md", "ignored");
        Write("skills/no-skill-here/notes.md", "ignored");

        List<UserMemoryFile> skills = UserMemoryService.SnapshotFiles()
                                                       .Where(f => f.Category == UserMemoryCategory.Skill)
                                                       .ToList();
        Assert.AreEqual(1, skills.Count);
        StringAssert.EndsWith(skills[0].AbsolutePath, "SKILL.md");
    }

    [TestMethod]
    public void Skills_DisplayName_UsesParentDirectory_NotFileBaseName()
    {
        // Each skill's text file is named exactly SKILL.md with the actual
        // skill identity carried by the parent dir.  ResolveDisplayName must
        // surface "python-patterns" / "git-flow" rather than the literal "SKILL"
        // (the prior shape rendered every inventory row identically as "Skill").
        Write("skills/python-patterns/SKILL.md", "# python patterns");
        Write("skills/git-flow/SKILL.md", "# git flow");

        List<UserMemoryFile> skills = UserMemoryService.SnapshotFiles()
                                                       .Where(f => f.Category == UserMemoryCategory.Skill)
                                                       .OrderBy(f => f.DisplayName)
                                                       .ToList();

        Assert.AreEqual(2, skills.Count);
        Assert.AreEqual("git-flow", skills[0].DisplayName);
        Assert.AreEqual("python-patterns", skills[1].DisplayName);
    }

    [TestMethod]
    public void Subtitle_FirstNonEmptyLine_StripsHeaderHash()
    {
        Write("CLAUDE.md", "\n\n# Claude Code Guide\nrest of file");
        UserMemoryFile primary = UserMemoryService.SnapshotFiles()
                                                  .Single(f => f.Category == UserMemoryCategory.PrimaryMemory);
        Assert.AreEqual("Claude Code Guide", primary.Subtitle);
    }

    // ── Smoke-driven (2026-05-05) subtitle-quality tests ─────────────────

    [TestMethod]
    public void Subtitle_SkipsMarkdownHorizontalRule()
    {
        // The original heuristic returned "---" as the subtitle for any file
        // whose first content was a markdown HR or YAML front-matter
        // delimiter. The improved heuristic skips past it.
        Write("agents/horizontal-rule.md", "---\nAfter the rule\n");
        UserMemoryFile entry = UserMemoryService.SnapshotFiles()
                                                .Single(f => f.Category == UserMemoryCategory.Subagent
                                                             && f.DisplayName == "horizontal-rule");
        Assert.AreEqual("After the rule", entry.Subtitle);
    }

    [TestMethod]
    public void Subtitle_PrefersYamlFrontMatterDescription()
    {
        // Many agents/*.md files in the wild open with YAML front-matter
        // containing a description field. The heuristic should prefer
        // it over the post-front-matter body text.
        Write("agents/code-reviewer.md",
            "---\n"
            + "name: code-reviewer\n"
            + "description: Reviews code for quality, security, and maintainability.\n"
            + "---\n"
            + "# Code Reviewer\n"
            + "Body content here.");
        UserMemoryFile entry = UserMemoryService.SnapshotFiles()
                                                .Single(f => f.DisplayName == "code-reviewer");
        Assert.AreEqual(
            "Reviews code for quality, security, and maintainability.",
            entry.Subtitle);
    }

    [TestMethod]
    public void Subtitle_FallsBackToYamlNameWhenNoDescription()
    {
        Write("agents/named.md",
            "---\n"
            + "name: My Agent\n"
            + "---\n"
            + "# Body");
        UserMemoryFile entry = UserMemoryService.SnapshotFiles()
                                                .Single(f => f.DisplayName == "named");
        Assert.AreEqual("My Agent", entry.Subtitle);
    }

    [TestMethod]
    public void Subtitle_SkipsBareJsonOpener_AndUsesNextDescriptiveLine()
    {
        // For a JSON file whose first line is "{", continue past it to
        // the first descriptive line (e.g. a comment-style key). This
        // keeps the user from seeing "{" as a row's subtitle.
        Write("hooks/config.json",
            "{\n"
            + "  \"description\": \"hook config\",\n"
            + "  \"key\": \"value\"\n"
            + "}");
        UserMemoryFile entry = UserMemoryService.SnapshotFiles()
                                                .Single(f => f.Category == UserMemoryCategory.Hook
                                                             && f.DisplayName == "config");
        Assert.IsNotNull(entry.Subtitle);
        Assert.AreNotEqual("{", entry.Subtitle);
    }

    [TestMethod]
    public void Subtitle_SkipsCodeFenceOpener()
    {
        Write("plans/fenced.md",
            "```\n"
            + "Real plan content\n"
            + "```");
        UserMemoryFile entry = UserMemoryService.SnapshotFiles()
                                                .Single(f => f.DisplayName == "fenced");
        Assert.AreEqual("Real plan content", entry.Subtitle);
    }

    [TestMethod]
    public void Subtitle_TrimmedAt120Chars_WithEllipsis()
    {
        string longLine = new('a', 130);
        Write("plans/long.md", longLine);
        UserMemoryFile entry = UserMemoryService.SnapshotFiles()
                                                .Single(f => f.DisplayName == "long");
        Assert.IsNotNull(entry.Subtitle);
        Assert.IsTrue(entry.Subtitle!.Length <= 121); // 120 + ellipsis char
        Assert.IsTrue(entry.Subtitle.EndsWith('…'));
    }

    [TestMethod]
    public void Subtitle_AllNoiseFile_ReturnsNull()
    {
        // A file that contains nothing but separators / structural noise
        // has no useful subtitle. Returning null is preferable to a
        // misleading "---" or "{" rendering.
        Write("plans/empty-noise.md", "---\n***\n```\n");
        UserMemoryFile entry = UserMemoryService.SnapshotFiles()
                                                .Single(f => f.DisplayName == "empty-noise");
        Assert.IsNull(entry.Subtitle);
    }

    [TestMethod]
    public void ProjectMemory_WhenProjectRootProvided_Included()
    {
        string projectRoot = Path.Combine(_fakeHome, "myproject");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "CLAUDE.md"), "# project");

        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles(projectRoot);
        Assert.AreEqual(1, files.Count(f => f.Category == UserMemoryCategory.ProjectMemory));
    }

    [TestMethod]
    public void ProjectMemory_NoRoot_Excluded()
    {
        Write("CLAUDE.md", "# top");
        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles(projectRoot: null);
        Assert.AreEqual(0, files.Count(f => f.Category == UserMemoryCategory.ProjectMemory));
    }

    [TestMethod]
    public void CrossToolMemory_CodexAndGemini_Probed()
    {
        Directory.CreateDirectory(Path.Combine(_fakeHome, ".codex"));
        File.WriteAllText(Path.Combine(_fakeHome, ".codex", "AGENTS.md"), "# codex");
        Directory.CreateDirectory(Path.Combine(_fakeHome, ".gemini"));
        File.WriteAllText(Path.Combine(_fakeHome, ".gemini", "GEMINI.md"), "# gemini");

        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles();
        List<UserMemoryFile> cross = files.Where(f => f.Category == UserMemoryCategory.CrossToolMemory).ToList();
        Assert.AreEqual(2, cross.Count);
    }

    [TestMethod]
    public async Task ReadAsync_ReturnsContent_WhenFileExists()
    {
        Write("CLAUDE.md", "hello world");
        string path = Path.Combine(_claudeHome, "CLAUDE.md");

        string? text = await UserMemoryService.ReadAsync(path, CancellationToken.None);
        Assert.AreEqual("hello world", text);
    }

    [TestMethod]
    public async Task ReadAsync_ReturnsNull_WhenMissing()
    {
        string path = Path.Combine(_claudeHome, "missing.md");
        string? text = await UserMemoryService.ReadAsync(path, CancellationToken.None);
        Assert.IsNull(text);
    }
}