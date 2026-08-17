using System.Reflection;
using System.Xml.Linq;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Enforces the one architectural invariant the OpenCodeForge plan rests on:
/// <b><c>AgentForge.*</c> may never reference <c>ClaudeForge.*</c> or <c>OpenCode.*</c>.</b>
/// </summary>
/// <remarks>
/// <para>
/// The shared foundation only stays shared if it cannot see either product. Nothing in the
/// build enforces that — a <c>ProjectReference</c> in the wrong direction compiles perfectly
/// — so it regresses quietly. It already did: <c>LayeredEditors.Avalonia.Services</c>, a
/// product-neutral services library, referenced <c>ClaudeForge.Sdk</c> for years purely to
/// name four dialog primitives. Nobody noticed because nothing looked.
/// </para>
/// <para>
/// <b>Two checks, because either alone has a blind spot:</b>
/// </para>
/// <list type="number">
///   <item><description>
///   <see cref="SharedProjectsNeverDeclareAProductReference"/> reads the <c>.csproj</c>
///   files. This is the leading indicator and the one that matters: a bad
///   <c>ProjectReference</c> is an architectural decision the moment it is written, even
///   before anything uses it.
///   </description></item>
///   <item><description>
///   <see cref="SharedAssembliesNeverReferenceAProduct"/> reads the compiled reference
///   tables, which catches violations that arrive without a direct <c>ProjectReference</c>
///   — a transitive leak, or a reference injected by a props/targets file.
///   </description></item>
/// </list>
/// <para>
/// The csproj check is not redundant: the compiler <b>omits unused references from the
/// assembly reference table entirely</b>, so a declared-but-not-yet-used bad reference is
/// invisible to reflection. Verified the hard way — an earlier version of this class had
/// only the reflection check, and adding a live <c>ClaudeForge.Core</c> reference to
/// <c>AgentForge.Abstractions</c> did not fail it.
/// </para>
/// </remarks>
[TestClass]
public sealed class AssemblyLayeringTests
{
    /// <summary>Name prefixes that identify a product-specific assembly or project.</summary>
    private static readonly string[] ProductPrefixes = ["ClaudeForge", "OpenCode"];

    private const string SharedPrefix = "AgentForge";

    // ── csproj-level check (the leading indicator) ───────────────────────────

    private static string FindSourceDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(dir, "src");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate src/ by walking up from '{AppContext.BaseDirectory}'.");
    }

    private static IReadOnlyList<string> SharedProjectFiles =>
        Directory.GetFiles(FindSourceDirectory(), $"{SharedPrefix}.*.csproj", SearchOption.AllDirectories);

    [TestMethod]
    public void AtLeastOneSharedProjectExists_SoTheseTestsAreNotVacuous()
    {
        // Without this, renaming the shared projects turns every assertion below into a
        // no-op pass — the classic way an architecture test quietly stops testing anything.
        Assert.IsTrue(
            SharedProjectFiles.Count > 0,
            $"No '{SharedPrefix}.*.csproj' found under {FindSourceDirectory()}. Either the "
            + "shared projects were renamed (update this test) or they no longer exist, in "
            + "which case the layering rule is unguarded.");
    }

    [TestMethod]
    public void SharedProjectsNeverDeclareAProductReference()
    {
        List<string> violations = [];

        foreach (string projectFile in SharedProjectFiles)
        {
            string shared = Path.GetFileNameWithoutExtension(projectFile);
            XDocument doc = XDocument.Load(projectFile);

            IEnumerable<string> referencePaths = doc
                                                 .Descendants()
                                                 .Where(e => e.Name.LocalName == "ProjectReference")
                                                 .Select(e => e.Attribute("Include")?.Value)
                                                 .Where(v => !string.IsNullOrEmpty(v))
                                                 .Select(v => v!);

            foreach (string reference in referencePaths)
            {
                string referencedProject = Path.GetFileNameWithoutExtension(
                    reference.Replace('\\', '/'));

                if (ProductPrefixes.Any(p => referencedProject.StartsWith(p, StringComparison.Ordinal)))
                {
                    violations.Add($"{shared}.csproj -> {referencedProject}");
                }
            }
        }

        Assert.IsTrue(
            violations.Count == 0,
            "A shared AgentForge project declares a ProjectReference to a product-specific "
            + "project, which breaks the foundation both apps are supposed to sit on:\n  "
            + string.Join("\n  ", violations)
            + "\n\nMove the shared type into AgentForge.Abstractions rather than pointing the "
            + "foundation at a product. If it genuinely needs product knowledge, it does not "
            + "belong in AgentForge.*.");
    }

    // ── compiled-reference check (catches transitive / injected leaks) ───────

    /// <summary>
    /// The test host's own directory holds every project's output, so the shared assemblies
    /// can be found without hardcoding a path out of the repo tree.
    /// </summary>
    private static string OutputDirectory =>
        Path.GetDirectoryName(typeof(AssemblyLayeringTests).Assembly.Location)!;

    [TestMethod]
    public void SharedAssembliesNeverReferenceAProduct()
    {
        string[] sharedAssemblies = Directory.GetFiles(OutputDirectory, $"{SharedPrefix}.*.dll");
        Assert.IsTrue(
            sharedAssemblies.Length > 0,
            $"No '{SharedPrefix}.*.dll' in {OutputDirectory} — this test cannot see the shared "
            + "assemblies, so it is not guarding anything.");

        List<string> violations = [];

        foreach (string path in sharedAssemblies)
        {
            AssemblyName[] referenced;
            string shared;
            try
            {
                Assembly assembly = Assembly.LoadFrom(path);
                shared = assembly.GetName().Name ?? Path.GetFileNameWithoutExtension(path);
                referenced = assembly.GetReferencedAssemblies();
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException)
            {
                continue; // not a managed assembly we can inspect
            }

            foreach (AssemblyName reference in referenced)
            {
                string name = reference.Name ?? string.Empty;
                if (ProductPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
                {
                    violations.Add($"{shared} -> {name}");
                }
            }
        }

        Assert.IsTrue(
            violations.Count == 0,
            "A shared AgentForge assembly has a compiled reference to a product-specific "
            + "assembly:\n  " + string.Join("\n  ", violations)
            + "\n\nThis one fires even without a direct ProjectReference, so check props/"
            + "targets files and transitive references too.");
    }
}
