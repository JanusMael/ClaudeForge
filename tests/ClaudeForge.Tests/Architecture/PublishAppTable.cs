using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Reads <c>src/publish/PublishApps.ps1</c> — the table every publish script and both release
/// workflows are keyed on — so the tests that assert against it all read it the same way.
/// </summary>
/// <remarks>
/// <para>
/// A source scan rather than an invocation of <c>pwsh</c>, the same choice
/// <c>ProductionSchemaRegistryTests</c> makes and for a related reason: running the script would
/// prove the table evaluates, not that it says the right things, and it would make unit tests
/// depend on a shell being installed on every machine that clones this repository.
/// </para>
/// <para>
/// ⚠ <b>One reader, deliberately.</b> Two tests need this file and an earlier draft gave each
/// its own regex — which is the same drift the table itself exists to prevent, one level up.
/// </para>
/// </remarks>
internal static class PublishAppTable
{
    internal const string RelativePath = "src/publish/PublishApps.ps1";

    /// <summary>One row, reduced to the fields the tests assert on.</summary>
    /// <param name="TagPrefix">
    /// Empty for the app that publishes unprefixed tags. Never <see langword="null"/> — an
    /// absent field is a malformed row, which <see cref="Read"/> refuses rather than defaults.
    /// </param>
    internal sealed record Row(
        string Name,
        string ProjectPath,
        string AssemblyName,
        string StartupLogToken,
        string TagPrefix);

    /// <summary>Splits the table into <c>[pscustomobject]@{ … }</c> blocks.</summary>
    private static readonly Regex RowRegex = new(
        @"\[pscustomobject\]@\{(?<body>.*?)\r?\n    \}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// One <c>Key = 'value'</c> assignment. <c>$null</c> is matched so an unset optional field
    /// is recognised as deliberately absent rather than skipped as unparseable.
    /// </summary>
    private static readonly Regex FieldRegex = new(
        @"(?<key>[A-Za-z]+)\s*=\s*(?:'(?<value>[^']*)'|(?<null>\$null))",
        RegexOptions.Compiled);

    /// <summary>The five fields every row must carry; every publish script reads all of them.</summary>
    private static readonly string[] Required =
        ["Name", "ProjectPath", "AssemblyName", "StartupLogToken", "TagPrefix"];

    /// <summary>The repo root, by walking up from the test assembly.</summary>
    /// <remarks>Matches <c>BuildFilePathIntegrityTests.FindRepoRoot()</c>.</remarks>
    internal static string FindRepoRoot()
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

    /// <summary>Every row in the table.</summary>
    /// <exception cref="InvalidOperationException">
    /// The file is missing, or a row omits one of <see cref="Required"/>. Both are thrown rather
    /// than tolerated: a silently-shorter table would let every test over it pass by checking
    /// less.
    /// </exception>
    internal static List<Row> Read(string repoRoot)
    {
        string tablePath = Path.Combine(
            repoRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(tablePath))
        {
            throw new InvalidOperationException(
                $"'{RelativePath}' not found. Every script under src/publish/ dot-sources it, so "
                + "its absence breaks publishing entirely — and nothing else in this suite would "
                + "say so.");
        }

        string text = File.ReadAllText(tablePath);
        List<Row> rows = [];

        foreach (Match row in RowRegex.Matches(text))
        {
            Dictionary<string, string?> fields = [];
            foreach (Match field in FieldRegex.Matches(row.Groups["body"].Value))
            {
                fields[field.Groups["key"].Value] =
                    field.Groups["null"].Success ? null : field.Groups["value"].Value;
            }

            foreach (string required in Required)
            {
                if (!fields.TryGetValue(required, out string? present) || present is null)
                {
                    throw new InvalidOperationException(
                        $"A row in {RelativePath} has no '{required}'. Every publish script reads "
                        + $"that field:\n{row.Value}");
                }
            }

            rows.Add(new Row(
                fields["Name"]!,
                fields["ProjectPath"]!,
                fields["AssemblyName"]!,
                fields["StartupLogToken"]!,
                fields["TagPrefix"]!));
        }

        return rows;
    }
}
