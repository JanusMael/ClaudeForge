using System.Reflection;
using System.Xml.Linq;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tests;

/// <summary>
/// Every accessor on <see cref="Strings"/> actually resolves, and matches the resx 1:1.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>Neither the compiler nor the repo's dead-key guard can catch what this catches.</b> The
/// accessor names its resource with a literal base name — <c>RootNamespace</c> plus the folder —
/// and a mismatch throws <see cref="MissingManifestResourceException"/> at <b>runtime</b>, on
/// whichever screen first reads a string. The dead-key guard looks the other way: it fails when a
/// resx key has no <c>Strings.Key</c> reference, so it catches an orphaned <i>resource</i> and is
/// blind to an orphaned <i>accessor</i>, which returns <see langword="null"/> and renders as a
/// blank label.
/// </para>
/// <para>
/// This is the test the sibling app's accessor xmldoc already claimed existed. It did not — the
/// comment named <c>OpenCodeForgeStringsTests</c>, which was never written. A guard that exists
/// only in a comment guards nothing.
/// </para>
/// </remarks>
[TestClass]
public sealed class StringsResolveTests
{
    private static string ResxPath()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(
                dir, "src", "OpenCode.Avalonia", "Localization", "Strings.resx");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not locate src/OpenCode.Avalonia/Localization/Strings.resx by walking up from "
            + $"'{AppContext.BaseDirectory}'.");
    }

    private static IReadOnlySet<string> ResxKeys() =>
        XDocument
            .Load(ResxPath())
            .Root!
            .Elements("data")
            .Select(d => (string?)d.Attribute("name"))
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<PropertyInfo> StringAccessors() =>
    [
        .. typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string)),
    ];

    [TestMethod]
    public void EveryAccessorResolvesToRealText()
    {
        IReadOnlyList<PropertyInfo> accessors = StringAccessors();

        Assert.IsTrue(
            accessors.Count > 0,
            "Reflected no string accessors off Strings. Either the accessor was renamed or this "
            + "test is no longer reading it — either way it is guarding nothing.");

        List<string> broken = [];
        foreach (PropertyInfo property in accessors)
        {
            object? value = property.GetValue(null);
            if (value is not string text || text.Length == 0)
            {
                broken.Add($"{property.Name}: resolved to {(value is null ? "null" : "empty")}");
                continue;
            }

            // A ResourceManager miss does not throw for a single key — it returns null, and the
            // usual symptom is a blank control. Comparing against the key name additionally
            // catches a value that was accidentally set to its own key.
            if (string.Equals(text, property.Name, StringComparison.Ordinal))
            {
                broken.Add($"{property.Name}: value is identical to the key name");
            }
        }

        Assert.IsTrue(
            broken.Count == 0,
            $"{broken.Count} accessor(s) do not resolve. The usual cause is the literal resource "
            + "base name in Strings.Designer.cs not matching RootNamespace plus the folder — which "
            + $"fails at runtime, not at build time:\n  {string.Join("\n  ", broken)}");
    }

    [TestMethod]
    public void AccessorsAndResxKeysCorrespondExactly()
    {
        IReadOnlySet<string> resx = ResxKeys();
        HashSet<string> accessors = StringAccessors()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Culture and ResourceManager are infrastructure, not strings; they are already excluded
        // by the string-typed filter, but assert the counts are plausible before comparing.
        Assert.IsTrue(resx.Count > 0, $"Parsed no <data> keys out of '{ResxPath()}'.");

        List<string> accessorWithoutResx = [.. accessors.Except(resx).Order(StringComparer.Ordinal)];
        List<string> resxWithoutAccessor = [.. resx.Except(accessors).Order(StringComparer.Ordinal)];

        Assert.IsTrue(
            accessorWithoutResx.Count == 0,
            "Accessor(s) with no matching resx entry — these return null and render as a blank "
            + $"label:\n  {string.Join("\n  ", accessorWithoutResx)}");

        Assert.IsTrue(
            resxWithoutAccessor.Count == 0,
            "Resx key(s) with no accessor. Reachable only by name, which the dead-key guard cannot "
            + $"see:\n  {string.Join("\n  ", resxWithoutAccessor)}");
    }
}
