using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Navigation;
using Bennewitz.Ninja.OpenCodeForge.Adapters;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// What a schema refresh is allowed to change without a human noticing.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>The nav grouping map names schema keys, and nothing checked they still exist.</b>
/// <see cref="OpenCodePageLayout"/> assigns each top-level property to a page by name. A key
/// that upstream renames or removes leaves an entry pointing at nothing — and the failure is
/// silent in both directions: the vanished property simply never appears, and the page it was
/// assigned to loses a row nobody is counting. `scripts/refresh-schema.ps1` makes that a
/// one-command event, so the guard has to exist before the tooling gets used.
/// </para>
/// <para>
/// ⚠ <b>Only the map-to-schema direction is a defect.</b> The reverse — a NEW upstream key with
/// no map entry — is legal by design: it falls to <see cref="OpenCodePageLayout.FallbackPage"/>.
/// It also cannot ship untriaged, because <c>OpenCodeDangerTableTests</c> already fails on any
/// schema key with no danger-table entry. Asserting it here too would duplicate that guard and
/// make every upstream addition fail in two places at once.
/// </para>
/// <para>
/// ⭐ <b>Counts are pinned per schema, and they are not magic numbers.</b> They are the arity a
/// human last looked at. A change means upstream restructured; confirm the new shape and update
/// the number deliberately. <c>opencode-config</c>'s 36 is asserted separately and for a
/// different reason by <c>RootRefSchemaTreeTests</c>, which is about the root-$ref fallback
/// firing at all rather than about drift.
/// </para>
/// <para>
/// Schemas are read from the source tree: <c>BundledResource</c> is internal to
/// <c>AgentForge.Core</c>, and the file that gets embedded is the file on disk. That the
/// embedding happens is asserted elsewhere.
/// </para>
/// </remarks>
[TestClass]
public sealed class SchemaRefreshDriftTests
{
    /// <summary>Top-level property counts as last reviewed by a human, 2026-09-08.</summary>
    /// <remarks>
    /// Measured against the live upstreams the refresh script pulls from, so these are the
    /// counts a refresh run today would produce — not a stale snapshot of the bundled copies.
    /// </remarks>
    private static readonly (string File, int Count)[] ExpectedPropertyCounts =
    [
        ("claude-code-settings.json", 142),
        ("opencode-config.json", 36),
        ("opencode-tui.json", 13),
    ];

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "tests")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root by walking up from '{AppContext.BaseDirectory}'.");
    }

    private static JsonDocument LoadSchema(string fileName)
    {
        string path = Path.Combine(
            RepoRoot(), "src", "AgentForge.Core", "Assets", "Schemas", fileName);
        Assert.IsTrue(File.Exists(path), $"'{path}' not found.");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// The top-level properties, from whichever of the two root shapes this schema uses.
    /// </summary>
    /// <remarks>
    /// <c>opencode-config.json</c> hangs everything off a root <c>$ref</c> and declares no root
    /// <c>properties</c>; the other two are ordinary object schemas. Handling both here keeps
    /// the count table uniform.
    /// </remarks>
    private static IReadOnlyList<string> TopLevelPropertyNames(string fileName)
    {
        using JsonDocument doc = LoadSchema(fileName);
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("properties", out JsonElement direct))
        {
            return [.. direct.EnumerateObject().Select(p => p.Name)];
        }

        Assert.IsTrue(root.TryGetProperty("$ref", out JsonElement refElement),
            $"'{fileName}' has neither root `properties` nor a root `$ref`. The schema's root "
            + "shape changed and this helper can no longer find its top-level properties.");

        string pointer = refElement.GetString() ?? string.Empty;
        Assert.IsTrue(pointer.StartsWith("#/", StringComparison.Ordinal),
            $"'{fileName}' root $ref is '{pointer}', which is not a local pointer.");

        JsonElement node = root;
        foreach (string segment in pointer[2..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.IsTrue(node.TryGetProperty(segment, out node),
                $"'{fileName}' root $ref '{pointer}' does not resolve — '{segment}' is missing.");
        }

        Assert.IsTrue(node.TryGetProperty("properties", out JsonElement viaRef),
            $"'{fileName}' root $ref target has no `properties`.");

        return [.. viaRef.EnumerateObject().Select(p => p.Name)];
    }

    [TestMethod]
    public void EverySchemaHasThePropertyCountAHumanLastReviewed()
    {
        List<string> drift = [];

        foreach ((string file, int expected) in ExpectedPropertyCounts)
        {
            int actual = TopLevelPropertyNames(file).Count;
            if (actual != expected)
            {
                drift.Add($"  {file}: {expected} -> {actual}");
            }
        }

        Assert.AreEqual(0, drift.Count,
            $"{drift.Count} bundled schema(s) changed top-level property count:\n"
            + string.Join('\n', drift)
            + "\n\nA refresh restructured something upstream. Read the diff, confirm the editors "
            + "still dispatch a typed editor for each new property rather than falling back to "
            + "JsonRaw, then update the count here deliberately.");
    }

    /// <summary>
    /// Every key the nav grouping map assigns to a page still exists in its schema.
    /// </summary>
    [TestMethod]
    [DataRow("opencode-config.json", "Config")]
    [DataRow("opencode-tui.json", "Tui")]
    public void EveryKeyTheLayoutMapsStillExistsInTheSchema(string fileName, string layoutName)
    {
        SchemaPageLayout layout = layoutName switch
        {
            "Config" => OpenCodePageLayout.Config,
            "Tui" => OpenCodePageLayout.Tui,
            _ => throw new ArgumentOutOfRangeException(nameof(layoutName), layoutName, null),
        };

        HashSet<string> schemaKeys = [.. TopLevelPropertyNames(fileName)];

        Assert.IsTrue(schemaKeys.Count > 0,
            $"No top-level properties were read from '{fileName}', so this test checked nothing.");
        Assert.IsTrue(layout.PropertyToPage.Count > 0,
            $"The {layoutName} layout maps no properties, so this test checked nothing.");

        List<string> orphaned =
        [
            .. layout.PropertyToPage.Keys
                .Where(k => !schemaKeys.Contains(k))
                .Order(StringComparer.Ordinal)
                .Select(k => $"  {k} -> {layout.PropertyToPage[k]}"),
        ];

        Assert.AreEqual(0, orphaned.Count,
            $"{orphaned.Count} key(s) in OpenCodePageLayout.{layoutName} name a property that is "
            + $"not in '{fileName}':\n"
            + string.Join('\n', orphaned)
            + "\n\nUpstream renamed or removed them. The entry is now inert: the property never "
            + "renders, and the page it was assigned to silently loses a row. Re-point or delete "
            + "each entry.");
    }

    /// <summary>
    /// Every page a layout entry targets is in that layout's declared order.
    /// </summary>
    /// <remarks>
    /// Not schema drift, but the same class of silent breakage and it costs one assertion: a page
    /// present in <c>PropertyToPage</c> but absent from <c>PageOrder</c> is appended sorted by
    /// title, which relocates it without any error. Worth pinning while a refresh is about to
    /// start moving these around.
    /// </remarks>
    [TestMethod]
    [DataRow("Config")]
    [DataRow("Tui")]
    public void EveryPageTheLayoutTargetsIsInItsDeclaredOrder(string layoutName)
    {
        SchemaPageLayout layout = layoutName switch
        {
            "Config" => OpenCodePageLayout.Config,
            "Tui" => OpenCodePageLayout.Tui,
            _ => throw new ArgumentOutOfRangeException(nameof(layoutName), layoutName, null),
        };

        HashSet<string> ordered = [.. layout.PageOrder];

        List<string> missing =
        [
            .. layout.PropertyToPage.Values
                .Distinct(StringComparer.Ordinal)
                .Where(p => !ordered.Contains(p))
                .Order(StringComparer.Ordinal),
        ];

        Assert.AreEqual(0, missing.Count,
            $"{missing.Count} page(s) are targeted by OpenCodePageLayout.{layoutName} but absent "
            + "from its PageOrder: " + string.Join(", ", missing)
            + ". They will be appended sorted by title rather than placed deliberately.");
    }
}
