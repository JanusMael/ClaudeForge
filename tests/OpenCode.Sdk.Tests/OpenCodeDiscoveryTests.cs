using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.OpenCode.Sdk;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// Every scope-ladder permutation the plan asks for: no project, project present, each of the
/// three project file names, <c>OPENCODE_CONFIG</c> set, <c>OPENCODE_CONFIG_DIR</c> relocated,
/// and <c>OPENCODE_DISABLE_PROJECT_CONFIG</c>.
/// </summary>
/// <remarks>
/// <para>
/// The home directory is sandboxed through <c>PlatformPaths.TestUserProfileOverride</c>, and
/// the environment is <b>passed as a value</b> rather than set on the process. Setting a real
/// variable would leak into whatever runs alongside these tests and surface later as a flake
/// with no obvious cause — the failure mode this suite has already been bitten by twice.
/// </para>
/// <para>
/// Ordering is asserted deliberately: discovery returns highest priority first, and the merge
/// engine relies on that. A test that only checked set membership would pass while precedence
/// was inverted.
/// </para>
/// </remarks>
[TestClass]
public class OpenCodeDiscoveryTests
{
    private string _sandbox = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "oc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    private string Touch(string relativePath, string content = "{}")
    {
        string full = Path.Combine(_sandbox, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private static IReadOnlyList<string> ScopeNames(IEnumerable<DiscoveredFile> files)
        => files.Select(f => f.Scope.ToString()).ToList();

    // ── the default installation ─────────────────────────────────────────────

    /// <summary>
    /// With nothing set and no project, exactly one scope is discovered — and it is reported
    /// even though the file does not exist, because the app needs a canonical path to create
    /// the first time the user edits.
    /// </summary>
    [TestMethod]
    public void NoProjectNoEnvironment_YieldsGlobalOnly_EvenWhenAbsent()
    {
        IReadOnlyList<DiscoveredFile> files =
            OpenCodeDiscovery.DiscoverConfig(projectRoot: null, OpenCodeEnvironment.Empty);

        CollectionAssert.AreEqual(new[] { "Global" }, ScopeNames(files).ToArray());
        Assert.IsFalse(files[0].Exists);
        StringAssert.Contains(files[0].FilePath, Path.Combine(".config", "opencode"));
    }

    /// <summary>
    /// OpenCode writes <c>opencode.jsonc</c> to the global directory by default, so looking
    /// only for the <c>.json</c> name reports "no global config" on an ordinary install.
    /// </summary>
    [TestMethod]
    public void GlobalJsonc_IsFoundWhenOnlyItExists()
    {
        string jsonc = Touch(Path.Combine(".config", "opencode", "opencode.jsonc"));

        IReadOnlyList<DiscoveredFile> files =
            OpenCodeDiscovery.DiscoverConfig(projectRoot: null, OpenCodeEnvironment.Empty);

        Assert.AreEqual(jsonc, files[0].FilePath,
            "A .jsonc global config must be found. OpenCode writes that form by default.");
        Assert.IsTrue(files[0].Exists);
    }

    /// <summary>When both exist, the plain <c>.json</c> name wins — one answer, not two.</summary>
    [TestMethod]
    public void GlobalJson_WinsWhenBothExist()
    {
        string json = Touch(Path.Combine(".config", "opencode", "opencode.json"));
        Touch(Path.Combine(".config", "opencode", "opencode.jsonc"));

        IReadOnlyList<DiscoveredFile> files =
            OpenCodeDiscovery.DiscoverConfig(projectRoot: null, OpenCodeEnvironment.Empty);

        Assert.AreEqual(json, files[0].FilePath);
    }

    // ── OPENCODE_CONFIG_DIR ──────────────────────────────────────────────────

    [TestMethod]
    public void ConfigDir_RelocatesTheGlobalScope()
    {
        string elsewhere = Path.Combine(_sandbox, "relocated");
        Directory.CreateDirectory(elsewhere);
        string moved = Path.Combine(elsewhere, "opencode.json");
        File.WriteAllText(moved, "{}");

        IReadOnlyList<DiscoveredFile> files = OpenCodeDiscovery.DiscoverConfig(
            projectRoot: null,
            new OpenCodeEnvironment(ConfigDir: elsewhere));

        Assert.AreEqual(moved, files[0].FilePath);
        Assert.IsTrue(files[0].Exists);
    }

    [TestMethod]
    public void ConfigDir_AlsoMovesTheTuiConfig()
    {
        string elsewhere = Path.Combine(_sandbox, "relocated");
        Directory.CreateDirectory(elsewhere);

        IReadOnlyList<DiscoveredFile> tui =
            OpenCodeDiscovery.DiscoverTui(new OpenCodeEnvironment(ConfigDir: elsewhere));

        Assert.AreEqual(Path.Combine(elsewhere, "tui.json"), tui[0].FilePath,
            "tui.json sits beside the main config, so relocating the directory moves both.");
    }

    // ── the project walk ─────────────────────────────────────────────────────

    /// <summary>
    /// All three project forms must be found. Checking only <c>opencode.json</c> shows the
    /// user a different authoritative file than the one the agent reads.
    /// </summary>
    [TestMethod]
    [DataRow("opencode.json")]
    [DataRow("opencode.jsonc")]
    [DataRow(".opencode/opencode.json")]
    public void EachProjectFileName_IsFound(string relative)
    {
        string projectRoot = Path.Combine(_sandbox, "proj");
        Directory.CreateDirectory(projectRoot);
        string expected = Path.Combine(projectRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
        File.WriteAllText(expected, "{}");

        IReadOnlyList<DiscoveredFile> files =
            OpenCodeDiscovery.DiscoverConfig(projectRoot, OpenCodeEnvironment.Empty);

        Assert.AreEqual(expected, files[0].FilePath, $"'{relative}' was not discovered.");
        Assert.AreEqual("Project", files[0].Scope.ToString());
    }

    /// <summary>The walk goes upward, so a config above the working directory still applies.</summary>
    [TestMethod]
    public void TheWalkGoesUpward()
    {
        string root = Path.Combine(_sandbox, "repo");
        string nested = Path.Combine(root, "packages", "app", "src");
        Directory.CreateDirectory(nested);
        string atRoot = Path.Combine(root, "opencode.json");
        File.WriteAllText(atRoot, "{}");

        IReadOnlyList<DiscoveredFile> files =
            OpenCodeDiscovery.DiscoverConfig(nested, OpenCodeEnvironment.Empty);

        Assert.AreEqual(atRoot, files[0].FilePath);
    }

    /// <summary>
    /// The nearer directory wins even when its file uses a later-listed name. Trying every
    /// name at each level before moving up is what makes that true; trying each name across
    /// all levels in turn would pick the parent's <c>opencode.json</c> instead.
    /// </summary>
    [TestMethod]
    public void TheNearestDirectoryWins_EvenAcrossFileNames()
    {
        string root = Path.Combine(_sandbox, "repo");
        string child = Path.Combine(root, "child");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(root, "opencode.json"), "{}");
        string nearer = Path.Combine(child, "opencode.jsonc");
        File.WriteAllText(nearer, "{}");

        IReadOnlyList<DiscoveredFile> files =
            OpenCodeDiscovery.DiscoverConfig(child, OpenCodeEnvironment.Empty);

        Assert.AreEqual(nearer, files[0].FilePath,
            "The nearer opencode.jsonc must beat the parent's opencode.json.");
    }

    [TestMethod]
    public void NoProjectRoot_MeansNoProjectScope()
    {
        Touch(Path.Combine("proj", "opencode.json"));

        IReadOnlyList<DiscoveredFile> files =
            OpenCodeDiscovery.DiscoverConfig(projectRoot: null, OpenCodeEnvironment.Empty);

        CollectionAssert.DoesNotContain(ScopeNames(files).ToArray(), "Project");
    }

    /// <summary>
    /// <c>OPENCODE_DISABLE_PROJECT_CONFIG=1</c> removes the layer outright. If the effective
    /// view ignored it, the app would show settings the running agent is not applying.
    /// </summary>
    [TestMethod]
    public void DisableProjectConfig_RemovesTheProjectScope()
    {
        string projectRoot = Path.Combine(_sandbox, "proj");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "opencode.json"), "{}");

        IReadOnlyList<DiscoveredFile> files = OpenCodeDiscovery.DiscoverConfig(
            projectRoot,
            new OpenCodeEnvironment(ProjectConfigDisabled: true));

        CollectionAssert.DoesNotContain(ScopeNames(files).ToArray(), "Project");
    }

    // ── OPENCODE_CONFIG, and the full ordering ───────────────────────────────

    [TestMethod]
    public void ConfigPath_AddsTheCustomScope_ReportedEvenWhenMissing()
    {
        string missing = Path.Combine(_sandbox, "nowhere", "custom.json");

        IReadOnlyList<DiscoveredFile> files = OpenCodeDiscovery.DiscoverConfig(
            projectRoot: null,
            new OpenCodeEnvironment(ConfigPath: missing));

        DiscoveredFile custom = files.Single(f => f.Scope.ToString() == "Custom");
        Assert.AreEqual(missing, custom.FilePath);
        Assert.IsFalse(custom.Exists,
            "A custom path that does not exist is still the scope the user pointed at.");
    }

    /// <summary>
    /// The measurement S1 made, expressed as discovery order: project outranks custom, custom
    /// outranks global. Highest priority first, which is what the merge engine consumes.
    /// </summary>
    [TestMethod]
    public void AllThreeScopes_AreOrderedHighestPriorityFirst()
    {
        string projectRoot = Path.Combine(_sandbox, "proj");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "opencode.json"), "{}");
        string custom = Path.Combine(_sandbox, "custom.json");
        File.WriteAllText(custom, "{}");
        Touch(Path.Combine(".config", "opencode", "opencode.json"));

        IReadOnlyList<DiscoveredFile> files = OpenCodeDiscovery.DiscoverConfig(
            projectRoot,
            new OpenCodeEnvironment(ConfigPath: custom));

        CollectionAssert.AreEqual(
            new[] { "Project", "Custom", "Global" },
            ScopeNames(files).ToArray(),
            "S1 measured OPENCODE_CONFIG < project. Reversed here, the wrong file wins every "
            + "conflict and nothing else looks different.");
    }

    // ── the two rungs deliberately not discovered ────────────────────────────

    /// <summary>
    /// Inline and Managed are on the ladder but are never discovered, and that is a stated
    /// gap rather than an oversight. This test exists so the day one of them starts being
    /// discovered is a deliberate change with a failing test attached, not a silent one.
    /// </summary>
    [TestMethod]
    public void InlineAndManaged_AreNotDiscovered_Yet()
    {
        string[] scopes = ScopeNames(OpenCodeDiscovery.DiscoverConfig(
            projectRoot: null,
            new OpenCodeEnvironment(InlineContent: "{\"model\":\"x\"}"))).ToArray();

        CollectionAssert.DoesNotContain(scopes, "Inline",
            "$OPENCODE_CONFIG_CONTENT has no file behind it, and DiscoveredFile is a path. "
            + "Supporting it needs the shared loader to accept content that never came from "
            + "disk — see OpenCodeDiscovery's remarks.");
        CollectionAssert.DoesNotContain(scopes, "Managed",
            "OpenCode's managed location has never been measured. A guessed path produces a "
            + "scope that silently never populates, indistinguishable from a working one.");
    }

    // ── labelling ────────────────────────────────────────────────────────────

    [TestMethod]
    public void FilesAreLabelledWithTheirOwnProductsFileType()
    {
        Assert.AreEqual(
            ConfigFileType.OpenCodeConfig,
            OpenCodeDiscovery.DiscoverConfig(null, OpenCodeEnvironment.Empty)[0].FileType);

        Assert.AreEqual(
            ConfigFileType.OpenCodeTui,
            OpenCodeDiscovery.DiscoverTui(OpenCodeEnvironment.Empty)[0].FileType);
    }

    /// <summary>
    /// Read-only-ness comes from the ladder, not from a second copy of the rule. Two of
    /// OpenCode's rungs are read-only, and none of the three discovered ones are.
    /// </summary>
    [TestMethod]
    public void DiscoveredScopesAreWritable_AndSaySoFromTheLadder()
    {
        string projectRoot = Path.Combine(_sandbox, "proj");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "opencode.json"), "{}");

        IReadOnlyList<DiscoveredFile> files = OpenCodeDiscovery.DiscoverConfig(
            projectRoot,
            new OpenCodeEnvironment(ConfigPath: Path.Combine(_sandbox, "c.json")));

        foreach (DiscoveredFile file in files)
        {
            Assert.AreEqual(file.Scope.IsReadOnly, file.IsReadOnly,
                $"'{file.Scope}' disagrees with its own ladder rung about being read-only. "
                + "A second copy of that rule is how a policy-locked scope becomes editable.");
            Assert.IsFalse(file.IsReadOnly);
        }
    }
}
