using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Memory;

public class UserMemoryServiceTests : IDisposable
{
    private string _fakeHome = null!;
    private string _claudeHome => Path.Combine(_fakeHome, ".claude");

    public UserMemoryServiceTests() => Setup();

    private void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(),
            "claudeforge-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_claudeHome);
        PlatformPaths.TestUserProfileOverride = _fakeHome;
    }

    private void Cleanup()
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

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    private void Write(string relPath, string content)
    {
        string full = Path.Combine(_claudeHome, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    // -----------------------------------------------------------------------

    [Fact]
    public void Empty_ClaudeHome_YieldsEmptyList()
    {
        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty);
        Assert.Empty(files);
    }

    [Fact]
    public void PrimaryMemory_BothCLAUDEAndAGENTS_BothAppear()
    {
        Write("CLAUDE.md", "# claude\nbody");
        Write("AGENTS.md", "# agents\nbody");

        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty);
        List<UserMemoryFile> primary = files.Where(f => f.Category == UserMemoryCategory.PrimaryMemory).ToList();
        Assert.Equal(2, primary.Count);
        MessageAssert.SameElements(
            new[] { "CLAUDE", "AGENTS" },
            primary.Select(f => f.DisplayName).ToArray());
    }

    [Fact]
    public void Subagent_AgentsDirectory_SurfacesMdFiles()
    {
        Write("agents/code-reviewer.md", "# reviewer\nlooks at code");
        Write("agents/tdd-guide.md", "# tdd");

        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty);
        List<UserMemoryFile> subagents = files.Where(f => f.Category == UserMemoryCategory.Subagent).ToList();
        Assert.Equal(2, subagents.Count);
    }

    [Fact]
    public void Subagent_NonMdFiles_AreIgnored()
    {
        Write("agents/notes.txt", "irrelevant");
        Write("agents/agent.md", "# real");

        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty);
        Assert.Single(files, f => f.Category == UserMemoryCategory.Subagent);
    }

    [Fact]
    public void Hook_AnyExtension_Surfaces()
    {
        // hooks/ uses "*" so .sh / .py / .js / no-extension all qualify.
        Write("hooks/precommit.sh", "#!/bin/bash");
        Write("hooks/format.py", "import sys");
        Write("hooks/runner", "shebangless");

        List<UserMemoryFile> hooks = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty)
                                                      .Where(f => f.Category == UserMemoryCategory.Hook)
                                                      .ToList();
        Assert.Equal(3, hooks.Count);
    }

    [Fact]
    public void Rules_AreEnumeratedRecursively()
    {
        Write("rules/common/security.md", "...");
        Write("rules/common/coding.md", "...");
        Write("rules/csharp/patterns.md", "...");

        List<UserMemoryFile> rules = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty)
                                                      .Where(f => f.Category == UserMemoryCategory.Rule)
                                                      .ToList();
        Assert.Equal(3, rules.Count);
    }

    [Fact]
    public void Skills_OnlySkillMd_Surfaces()
    {
        Write("skills/python-patterns/SKILL.md", "# python patterns\n...");
        Write("skills/python-patterns/extra.md", "ignored");
        Write("skills/no-skill-here/notes.md", "ignored");

        List<UserMemoryFile> skills = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty)
                                                       .Where(f => f.Category == UserMemoryCategory.Skill)
                                                       .ToList();
        Assert.Single(skills);
        OrdinalAssert.EndsWith("SKILL.md", skills[0].AbsolutePath);
    }

    [Fact]
    public void Skills_DisplayName_UsesParentDirectory_NotFileBaseName()
    {
        // Each skill's text file is named exactly SKILL.md with the actual
        // skill identity carried by the parent dir.  ResolveDisplayName must
        // surface "python-patterns" / "git-flow" rather than the literal "SKILL"
        // (the prior shape rendered every inventory row identically as "Skill").
        Write("skills/python-patterns/SKILL.md", "# python patterns");
        Write("skills/git-flow/SKILL.md", "# git flow");

        List<UserMemoryFile> skills = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty)
                                                       .Where(f => f.Category == UserMemoryCategory.Skill)
                                                       .OrderBy(f => f.DisplayName)
                                                       .ToList();

        Assert.Equal(2, skills.Count);
        Assert.Equal("git-flow", skills[0].DisplayName);
        Assert.Equal("python-patterns", skills[1].DisplayName);
    }

    [Fact]
    public void Subtitle_FirstNonEmptyLine_StripsHeaderHash()
    {
        Write("CLAUDE.md", "\n\n# Claude Code Guide\nrest of file");
        UserMemoryFile primary = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty)
                                                  .Single(f => f.Category == UserMemoryCategory.PrimaryMemory);
        Assert.Equal("Claude Code Guide", primary.Subtitle);
    }

    /// <summary>
    /// The subtitle read runs in the background on every memory-editor refresh, so it must share
    /// with a writer: one holding the file (an editor mid-save) must not blank the subtitle, and
    /// the read must not lock that writer out.
    /// </summary>
    /// <remarks>
    /// ⛔ It opened with <c>FileShare.Read</c>. After a reload it reaches <c>settings.json</c>, and a
    /// write landing during it failed "being used by another process" — <c>ReloadHardeningTests</c>'
    /// timing flake, whose holder a first-chance stack capture named as this read. The sharing check
    /// is symmetric, so this deterministic direction guards both. ⓘ Windows-only enforcement.
    /// </remarks>
    [Fact]
    public void Subtitle_StillReads_WhileAWriterHoldsTheFile()
    {
        Write("CLAUDE.md", "# Claude Code Guide\nrest of file");
        using FileStream writer = new(Path.Combine(_claudeHome, "CLAUDE.md"), FileMode.Open, FileAccess.Write, FileShare.Read);

        UserMemoryFile primary = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty)
                                                  .Single(f => f.Category == UserMemoryCategory.PrimaryMemory);

        MessageAssert.Equal("Claude Code Guide", primary.Subtitle,
            "A writer holding the file must not blank its subtitle; the preview read shares write access.");
    }

    // ── Smoke-driven (2026-05-05) subtitle-quality tests ─────────────────

    [Fact]
    public void Subtitle_SkipsMarkdownHorizontalRule()
    {
        // The original heuristic returned "---" as the subtitle for any file
        // whose first content was a markdown HR or YAML front-matter
        // delimiter. The improved heuristic skips past it.
        Write("agents/horizontal-rule.md", "---\nAfter the rule\n");
        UserMemoryFile entry = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty)
                                                .Single(f => f.Category == UserMemoryCategory.Subagent
                                                             && f.DisplayName == "horizontal-rule");
        Assert.Equal("After the rule", entry.Subtitle);
    }

    [Fact]
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
        UserMemoryFile entry = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty)
                                                .Single(f => f.DisplayName == "code-reviewer");
        Assert.Equal(
            "Reviews code for quality, security, and maintainability.",
            entry.Subtitle);
    }

    [Fact]
    public void Subtitle_FallsBackToYamlNameWhenNoDescription()
    {
        Write("agents/named.md",
            "---\n"
            + "name: My Agent\n"
            + "---\n"
            + "# Body");
        UserMemoryFile entry = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty)
                                                .Single(f => f.DisplayName == "named");
        Assert.Equal("My Agent", entry.Subtitle);
    }

    [Fact]
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
        UserMemoryFile entry = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty)
                                                .Single(f => f.Category == UserMemoryCategory.Hook
                                                             && f.DisplayName == "config");
        Assert.NotNull(entry.Subtitle);
        Assert.NotEqual("{", entry.Subtitle);
    }

    [Fact]
    public void Subtitle_SkipsCodeFenceOpener()
    {
        Write("plans/fenced.md",
            "```\n"
            + "Real plan content\n"
            + "```");
        UserMemoryFile entry = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty)
                                                .Single(f => f.DisplayName == "fenced");
        Assert.Equal("Real plan content", entry.Subtitle);
    }

    [Fact]
    public void Subtitle_TrimmedAt120Chars_WithEllipsis()
    {
        string longLine = new('a', 130);
        Write("plans/long.md", longLine);
        UserMemoryFile entry = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty)
                                                .Single(f => f.DisplayName == "long");
        Assert.NotNull(entry.Subtitle);
        Assert.True(entry.Subtitle!.Length <= 121); // 120 + ellipsis char
        Assert.True(entry.Subtitle.EndsWith('…'));
    }

    [Fact]
    public void Subtitle_AllNoiseFile_ReturnsNull()
    {
        // A file that contains nothing but separators / structural noise
        // has no useful subtitle. Returning null is preferable to a
        // misleading "---" or "{" rendering.
        Write("plans/empty-noise.md", "---\n***\n```\n");
        UserMemoryFile entry = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty)
                                                .Single(f => f.DisplayName == "empty-noise");
        Assert.Null(entry.Subtitle);
    }

    [Fact]
    public void ProjectMemory_WhenProjectRootProvided_Included()
    {
        string projectRoot = Path.Combine(_fakeHome, "myproject");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "CLAUDE.md"), "# project");

        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty, projectRoot);
        Assert.Single(files, f => f.Category == UserMemoryCategory.ProjectMemory);
    }

    [Fact]
    public void ProjectMemory_NoRoot_Excluded()
    {
        Write("CLAUDE.md", "# top");
        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty, projectRoot: null);
        Assert.DoesNotContain(files, f => f.Category == UserMemoryCategory.ProjectMemory);
    }

    [Fact]
    public void CrossToolMemory_CodexAndGemini_Probed()
    {
        Directory.CreateDirectory(Path.Combine(_fakeHome, ".codex"));
        File.WriteAllText(Path.Combine(_fakeHome, ".codex", "AGENTS.md"), "# codex");
        Directory.CreateDirectory(Path.Combine(_fakeHome, ".gemini"));
        File.WriteAllText(Path.Combine(_fakeHome, ".gemini", "GEMINI.md"), "# gemini");

        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty);
        List<UserMemoryFile> cross = files.Where(f => f.Category == UserMemoryCategory.CrossToolMemory).ToList();
        Assert.Equal(2, cross.Count);
    }

    [Fact]
    public async Task ReadAsync_ReturnsContent_WhenFileExists()
    {
        Write("CLAUDE.md", "hello world");
        string path = Path.Combine(_claudeHome, "CLAUDE.md");

        string? text = await UserMemoryService.ReadAsync(path, CancellationToken.None);
        Assert.Equal("hello world", text);
    }

    [Fact]
    public async Task ReadAsync_ReturnsNull_WhenMissing()
    {
        string path = Path.Combine(_claudeHome, "missing.md");
        string? text = await UserMemoryService.ReadAsync(path, CancellationToken.None);
        Assert.Null(text);
    }

    // ── Configuration category ────────────────────────────────────────────

    [Fact]
    public void Configuration_UserScopeJsonFiles_AreDiscovered()
    {
        Write("settings.json", "{}");
        Write("mcp.json", "{}");
        File.WriteAllText(Path.Combine(_fakeHome, ".claude.json"), "{}");

        // ⛔ Written deliberately, and deliberately NOT expected below. Managed policy moved to
        // the per-OS system directory, where Claude Code actually reads it — so a file sitting at
        // ~/.claude/managed-settings.json is no longer a config file this product models. It used
        // to be listed here, which is the same wrong belief that made the settings pages show it
        // as enforced policy while the agent ignored it.
        Write("managed-settings.json", "{}");
        Write(Path.Combine("managed-settings.d", "10-policy.json"), "{}");

        string[] names = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty)
            .Where(f => f.Category == UserMemoryCategory.Configuration)
            .Select(f => f.DisplayName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        MessageAssert.SameElements(
            new[] { ".claude.json", "mcp.json", "settings.json" },
            names,
            "Every USER-scope config file must be discoverable — and managed policy is not one, "
            + "because it lives in a system directory shared by every user on the machine.");
    }

    [Fact]
    public void Configuration_KeepsFileExtension_UnlikeMemoryEntries()
    {
        // Memory entries strip the extension (CLAUDE.md -> "CLAUDE"); config entries must
        // NOT, or the rows would read as a meaningless "settings" / "mcp".
        Write("settings.json", "{}");

        UserMemoryFile config = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty)
            .Single(f => f.Category == UserMemoryCategory.Configuration);

        Assert.Equal("settings.json", config.DisplayName);
    }

    [Fact]
    public void Configuration_ProjectScope_IsSuffixed_SoItDoesNotLookLikeTheUserFile()
    {
        Write("settings.json", "{}");
        string projectRoot = Path.Combine(_fakeHome, "proj");
        Directory.CreateDirectory(Path.Combine(projectRoot, ".claude"));
        File.WriteAllText(Path.Combine(projectRoot, ".claude", "settings.json"), "{}");
        File.WriteAllText(Path.Combine(projectRoot, ".claude", "settings.local.json"), "{}");

        string[] names = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty, projectRoot)
            .Where(f => f.Category == UserMemoryCategory.Configuration)
            .Select(f => f.DisplayName)
            .ToArray();

        Assert.Contains("settings.json", names);
        Assert.Contains("settings.json (project)", names);
        Assert.Contains("settings.local.json (project)", names);
    }

    [Fact]
    public void Configuration_CredentialsFile_IsNeverListed()
    {
        // Credentials hold live auth tokens; a one-click "open" for them in a browsable
        // inventory is a needless disclosure risk. Must stay out of the list.
        Write("settings.json", "{}");
        Write(".credentials.json", "{\"token\":\"secret\"}");

        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles(ClaudeEnvironment.Empty);

        Assert.False(
            files.Any(f => f.AbsolutePath.Contains("credentials", StringComparison.OrdinalIgnoreCase)),
            "The credentials file must never be surfaced in the inventory.");
    }
}