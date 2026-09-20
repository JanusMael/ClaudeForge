using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Platform;

/// <summary>
/// <see cref="ClaudeEnvironment"/> as a value: every case constructed directly, with no process
/// environment mutated anywhere in this file.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>That is the design paying off, not a limitation.</b> The record exists so
/// <c>CLAUDE_CONFIG_DIR</c> can be exercised without <c>Environment.SetEnvironmentVariable</c>,
/// which is process-global: a test that set it would leak into whatever ran alongside it in this
/// single-process suite, and the failure would look like a flake rather than a missing reset.
/// </para>
/// <para>
/// ⛔ <b><see cref="ClaudeEnvironment.FromProcess"/> is deliberately NOT exercised here</b>, and
/// it is the one member that reads the real environment. Covering it would mean setting the
/// variable, which is the thing this shape exists to avoid — so it is kept to a single line that
/// does nothing but read and delegate.
/// </para>
/// </remarks>
[TestClass]
public sealed class ClaudeEnvironmentTests
{
    [TestMethod]
    public void Empty_HasNoConfigDir()
    {
        Assert.IsNull(ClaudeEnvironment.Empty.ConfigDir);
        Assert.IsNull(ClaudeEnvironment.Empty.ResolvedConfigDir);
    }

    [TestMethod]
    public void AnAbsolutePath_ResolvesToItself()
    {
        string absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "claude-env-abs"));

        ClaudeEnvironment env = new(absolute);

        Assert.AreEqual(absolute, env.ResolvedConfigDir);
    }

    /// <summary>
    /// ⚠ A blank value is treated as unset, but only <see cref="ClaudeEnvironment.FromProcess"/>
    /// does that normalisation — so a record constructed with whitespace keeps it, and resolving
    /// it must not silently produce the current directory.
    /// </summary>
    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\t")]
    public void ABlankValue_DoesNotResolveToTheCurrentDirectory(string blank)
    {
        ClaudeEnvironment env = new(blank);

        Assert.AreNotEqual(
            Directory.GetCurrentDirectory(),
            env.ResolvedConfigDir,
            "A blank CLAUDE_CONFIG_DIR must not resolve to the working directory — that would " +
            "silently root the whole config tree wherever the app happened to be launched from.");
    }

    /// <summary>
    /// ⭐ The reason <see cref="ClaudeEnvironment.ResolvedConfigDir"/> exists at all: a relative
    /// value re-resolved per call would change its answer when the working directory changed,
    /// which defeats reading the environment once at composition.
    /// </summary>
    [TestMethod]
    public void ARelativePath_IsMadeAbsolute()
    {
        ClaudeEnvironment env = new("some-relative-dir");

        string? resolved = env.ResolvedConfigDir;

        Assert.IsNotNull(resolved);
        Assert.IsTrue(
            Path.IsPathRooted(resolved),
            $"Expected an absolute path, got '{resolved}'.");
    }

    /// <summary>
    /// ⛔ Resolving must NOT create the directory. A path accessor with a filesystem side effect
    /// turns a typo in the variable into a new empty config tree instead of an error anyone sees.
    /// </summary>
    [TestMethod]
    public void Resolving_DoesNotCreateTheDirectory()
    {
        string target = Path.Combine(Path.GetTempPath(), "claude-env-" + Guid.NewGuid().ToString("N"));

        // Assert the premise, or the claim below is vacuous.
        Assert.IsFalse(Directory.Exists(target), "Precondition: the scratch path must not exist.");

        ClaudeEnvironment env = new(target);
        string? resolved = env.ResolvedConfigDir;

        Assert.AreEqual(target, resolved);
        Assert.IsFalse(
            Directory.Exists(target),
            "ResolvedConfigDir created the directory. It must resolve only.");
    }

    /// <summary>
    /// A record, so equality is by value — which is what lets a test hand one to a path
    /// implementation and compare results without any ambient state.
    /// </summary>
    [TestMethod]
    public void TwoRecordsWithTheSameValue_AreEqual()
    {
        Assert.AreEqual(new ClaudeEnvironment("/tmp/x"), new ClaudeEnvironment("/tmp/x"));
        Assert.AreNotEqual(new ClaudeEnvironment("/tmp/x"), new ClaudeEnvironment("/tmp/y"));
    }
}
