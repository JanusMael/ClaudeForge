using System.Reflection;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests;

/// <summary>
/// Reflection-based contract tests for the public Sdk surface: no Avalonia reference, and the
/// threading contract documented on <see cref="IAgentConfigClient"/>.
/// </summary>
/// <remarks>
/// ⓘ Two tests left this class in plans/00010 step Q, each after a canary reddened it and its
/// replacement together: the defaulted-token check is BNAQ1001, now over every packable
/// assembly, and the JsonNode check is BNAQ1002, still Sdk only. Both live in
/// <c>ClaudeForge.Tests</c>' <c>Architecture/AssemblyQualityTests</c>.
/// </remarks>
public class PublicSurfaceContractTests
{
    private static Assembly SdkAssembly => typeof(IAgentConfigClient).Assembly;

    [Fact]
    public void Sdk_HasNoAvaloniaReference()
    {
        // The Sdk powers headless consumers (MCP servers, CLIs) — pulling in
        // Avalonia would be both expensive and wrong. Catch any future
        // accidental dependency at test time rather than at headless-consumer
        // build time.
        List<string> offenders = SdkAssembly
                                 .GetReferencedAssemblies()
                                 .Where(a => a.Name?.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) == true)
                                 .Select(a => a.Name!)
                                 .ToList();

        MessageAssert.Equal(
            0, offenders.Count,
            $"Sdk references Avalonia: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void IAgentConfigClient_DocumentsThreadingContract()
    {
        // Acceptance criterion: "Threading contract (§7) is documented on
        // IAgentConfigClient itself (XML doc on the interface), not just in
        // the design doc." This guards against a refactor that drops the
        // class-level remarks block and forgets to re-add it.
        //
        // We can't read XML doc comments at runtime via vanilla reflection —
        // they live in a separate .xml file alongside the assembly. Instead
        // assert the file exists and contains the expected key phrases. Both
        // are stable wording from the acceptance criterion.
        string asmPath = SdkAssembly.Location;
        string xmlPath = Path.ChangeExtension(asmPath, ".xml");

        Assert.True(File.Exists(xmlPath),
            $"Expected XML doc file alongside the Sdk assembly: {xmlPath}. " +
            "Confirm <GenerateDocumentationFile>true</GenerateDocumentationFile> in the csproj.");

        string xml = File.ReadAllText(xmlPath);
        Assert.True(xml.Contains("Thread-safe"),
            "IAgentConfigClient XML doc must mention 'Thread-safe' (threading contract section).");
        Assert.True(xml.Contains("Cancellation"),
            "IAgentConfigClient XML doc must mention the Cancellation contract.");
        Assert.True(xml.Contains("Disposal"),
            "IAgentConfigClient XML doc must mention the Disposal contract.");
    }
}