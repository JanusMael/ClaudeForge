using System.Reflection;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests;

/// <summary>
/// Reflection-based contract tests for the public Sdk surface. These tests
/// fail loudly if a future change accidentally adds a defaulted CancellationToken
/// parameter, leaks a JsonNode through a public API, or pulls in an Avalonia
/// reference.
/// </summary>
/// <remarks>
/// "Every async method on a stub interface takes a non-defaulted CancellationToken.
///  CI enforces this with a Roslyn analyzer rule (or a unit test that reflects
///  over the public surface)."  This is the unit-test option.
/// </remarks>
[TestClass]
public class PublicSurfaceContractTests
{
    private static Assembly SdkAssembly => typeof(IClaudeConfigClient).Assembly;

    [TestMethod]
    public void EveryAsyncMethod_HasRequiredCancellationToken()
    {
        // Walk every public method on every public type in the SDK assembly.
        // For each parameter typed CancellationToken, assert it has no default
        // value. The "= default" pattern is the specific shape we're banning;
        // consumers must pass CancellationToken.None explicitly when they
        // genuinely cannot be cancelled.
        List<string> offenders = new();

        foreach (Type type in SdkAssembly.GetExportedTypes())
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            // Compiler-generated members (record equality, etc.) — skip; they
            // don't have CT parameters anyway, so this is just hygiene.
            if (method.IsSpecialName)
            {
                continue;
            }

            foreach (ParameterInfo param in method.GetParameters())
            {
                if (param.ParameterType != typeof(CancellationToken))
                {
                    continue;
                }

                if (!param.HasDefaultValue)
                {
                    continue;
                }

                offenders.Add($"{type.FullName}.{method.Name}({param.Name})");
            }
        }

        Assert.AreEqual(
            0, offenders.Count,
            $"Defaulted CancellationToken parameter(s) found — required CTs only:\n  {string.Join("\n  ", offenders)}");
    }

    [TestMethod]
    public void NoPublicApi_LeaksSystemTextJsonNodes()
    {
        // The SDK contract is "no JsonNode in any public API". Walk public
        // types' members and surface any property / method / event that exposes
        // a System.Text.Json.Nodes type.
        List<string> offenders = new();

        foreach (Type type in SdkAssembly.GetExportedTypes())
        {
            foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsForbidden(prop.PropertyType))
                {
                    offenders.Add($"property {type.FullName}.{prop.Name}");
                }
            }

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.IsSpecialName)
                {
                    continue;
                }

                if (IsForbidden(method.ReturnType))
                {
                    offenders.Add($"return {type.FullName}.{method.Name}");
                }

                foreach (ParameterInfo param in method.GetParameters())
                {
                    if (IsForbidden(param.ParameterType))
                    {
                        offenders.Add($"param {type.FullName}.{method.Name}({param.Name})");
                    }
                }
            }
        }

        Assert.AreEqual(
            0, offenders.Count,
            $"System.Text.Json.Nodes types leaked into public Sdk API:\n  {string.Join("\n  ", offenders)}");
        return;

        bool IsForbidden(Type t)
        {
            return t.FullName?.StartsWith("System.Text.Json.Nodes.", StringComparison.Ordinal) == true;
        }
    }

    [TestMethod]
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

        Assert.AreEqual(
            0, offenders.Count,
            $"Sdk references Avalonia: {string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void IClaudeConfigClient_DocumentsThreadingContract()
    {
        // Acceptance criterion: "Threading contract (§7) is documented on
        // IClaudeConfigClient itself (XML doc on the interface), not just in
        // the design doc." This guards against a refactor that drops the
        // class-level remarks block and forgets to re-add it.
        //
        // We can't read XML doc comments at runtime via vanilla reflection —
        // they live in a separate .xml file alongside the assembly. Instead
        // assert the file exists and contains the expected key phrases. Both
        // are stable wording from the acceptance criterion.
        string asmPath = SdkAssembly.Location;
        string xmlPath = Path.ChangeExtension(asmPath, ".xml");

        Assert.IsTrue(File.Exists(xmlPath),
            $"Expected XML doc file alongside the Sdk assembly: {xmlPath}. " +
            "Confirm <GenerateDocumentationFile>true</GenerateDocumentationFile> in the csproj.");

        string xml = File.ReadAllText(xmlPath);
        Assert.IsTrue(xml.Contains("Thread-safe"),
            "IClaudeConfigClient XML doc must mention 'Thread-safe' (threading contract section).");
        Assert.IsTrue(xml.Contains("Cancellation"),
            "IClaudeConfigClient XML doc must mention the Cancellation contract.");
        Assert.IsTrue(xml.Contains("Disposal"),
            "IClaudeConfigClient XML doc must mention the Disposal contract.");
    }
}