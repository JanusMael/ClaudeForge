using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// plans/00008, decision 1: the five <c>AgentForge.*</c> libraries know nothing about Claude, and
/// three guards keep it that way. Each holds today's sites on an allow-list that must match exactly,
/// so a site can be removed (and the list lowered with it) but never added or re-added.
/// </summary>
/// <remarks>
/// <para>
/// <b>(a) Words.</b> Every non-comment line under <c>src/AgentForge.*</c> is searched for
/// <c>claude</c>, <c>anthropic</c> and the measured layout literals. The allow-list keys each hit by
/// file and normalized line text and holds a <b>count</b>, not a set: identical allowed lines already
/// repeat (<c>ClaudeEnvironment env,</c> in three files), so a set would let one more copy in unseen.
/// Persisted wire strings that must keep their text so old archives stay readable (decision 5) are a
/// separate, permanent list, stripped before matching.
/// </para>
/// <para>
/// <b>(b) Resources.</b> No <c>AgentForge.*.dll</c> embeds a manifest resource other than compiled
/// <c>.resx</c> strings; today only Core does, with Claude's and OpenCode's data.
/// </para>
/// <para>
/// <b>(c) Shapes.</b> No AgentForge assembly constructs a <see cref="ScopeLadder"/> or a
/// <see cref="FootprintCatalog"/>: Claude's ladder and catalog contain none of the words (a) looks
/// for. ⛔ <b>This reads compiled IL, not source</b>, and that is measured, not taste: both defaults
/// are built as <c>{ get; } = new(</c>, a target-typed property initializer that a text search for
/// <c>new ScopeLadder(</c> never matches. A <c>newobj</c> of the constructor is there in every
/// spelling.
/// </para>
/// <para>
/// ⚠ The allow-list is written by <see cref="WriteAllowList"/>, an explicit test that never runs
/// in the normal suite. Growing the list is visible in review as a diff to the file; the guard's
/// job is that nothing grows without that diff.
/// </para>
/// </remarks>
public sealed class ProductNeutralityTests
{
    private const string AllowListRelative = "tests/ClaudeForge.Tests/Architecture/ProductNeutrality/allow-list.tsv";

    private static readonly Regex WordPattern = new(
        @"claude|anthropic|managed-settings|mcp\.json|settings\.local|\.credentials|sk-ant|msix",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Decision 5's persisted wire strings: (file, exact literal). Removed from the line before the
    /// word search, so they never need the allow-list. Adding one is a decision, not a fix.
    /// </summary>
    /// <remarks>
    /// ⓘ The legacy archive folders <c>"ClaudeCode"</c> and <c>"ClaudeDesktop"</c> join this list
    /// in plans/00008 step 6, when they come to rest in <c>ExportManifest</c> for reading old
    /// archives; today they live in <c>SchemaRegistry</c>, which step 6 moves out. An entry naming
    /// a literal its file does not hold would exempt nothing and read as protection.
    /// </remarks>
    private static readonly (string File, string Literal)[] PermanentWireStrings =
    [
        ("src/AgentForge.Core/Backup/ExportManifest.cs", "\"includesClaudeCode\""),
        ("src/AgentForge.Core/Backup/ExportManifest.cs", "\"includesClaudeDesktop\""),
        ("src/AgentForge.Core/Backup/BackupJsonContext.cs", "\"_claudeforge_sanitization_error\""),
    ];

    [Fact]
    public void AgentForgeKnowsNothingItsAllowListDoesNotRecord()
    {
        Dictionary<string, int> current = Measure(out int filesScanned);

        // ⛔ The premise, asserted rather than assumed: a scan that found no source files would
        // report success having measured nothing.
        Assert.True(filesScanned > 50, $"Only {filesScanned} source file(s) under src/AgentForge.* were scanned.");

        Dictionary<string, int> allowed = ReadAllowList();

        List<string> grew = [];
        List<string> shrank = [];
        foreach ((string key, int count) in current)
        {
            int limit = allowed.GetValueOrDefault(key);
            if (count > limit)
            {
                grew.Add($"  {key}  (now {count}, allowed {limit})");
            }
        }

        foreach ((string key, int limit) in allowed)
        {
            int count = current.GetValueOrDefault(key);
            if (count < limit)
            {
                shrank.Add($"  {key}  (now {count}, allowed {limit})");
            }
        }

        StringBuilder message = new();
        if (grew.Count > 0)
        {
            message.AppendLine($"{grew.Count} Claude-specific site(s) in AgentForge that the allow-list does not hold. "
                + "AgentForge must not learn anything new about Claude (plans/00008): move the knowledge to "
                + "ClaudeForge.Sdk.Claude or behind a seam the product fills.");
            grew.ForEach(line => message.AppendLine(line));
        }

        if (shrank.Count > 0)
        {
            message.AppendLine($"{shrank.Count} allow-list entr(ies) no longer needed. Lower them in {AllowListRelative} "
                + "(run the explicit WriteAllowList test), so a removed site cannot come back unseen.");
            shrank.ForEach(line => message.AppendLine(line));
        }

        Assert.True(message.Length == 0, message.ToString());
    }

    /// <summary>
    /// Rewrites the allow-list from the tree as it is now. Explicit: it never runs with the suite.
    /// Run it after REMOVING sites, and read the diff before committing it.
    /// </summary>
    [Fact(Explicit = true)]
    public void WriteAllowList()
    {
        Dictionary<string, int> current = Measure(out _);
        string path = Path.Combine(RepoRoot(), AllowListRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        StringBuilder text = new();
        text.AppendLine("# plans/00008 decision 1: every Claude-specific site AgentForge still holds. It may only shrink.");
        text.AppendLine("# Written by ProductNeutralityTests.WriteAllowList; columns are kind, key, count.");
        foreach ((string key, int count) in current.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            text.Append(key).Append('\t').Append(count).Append('\n');
        }

        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    private static Dictionary<string, int> Measure(out int filesScanned)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        string root = RepoRoot();
        filesScanned = 0;

        // (a) Words.
        foreach (string file in AgentForgeSources(root))
        {
            filesScanned++;
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            string[] exempt = PermanentWireStrings.Where(p => p.File == relative).Select(p => p.Literal).ToArray();
            foreach (string rawLine in StripComments(File.ReadAllText(file)).Split('\n'))
            {
                string line = rawLine;
                foreach (string literal in exempt)
                {
                    line = line.Replace(literal, "\"\"", StringComparison.Ordinal);
                }

                if (!WordPattern.IsMatch(line))
                {
                    continue;
                }

                Increment(counts, $"words\t{relative}\t{Normalize(line)}");
            }
        }

        // (b) Resources and (c) shapes, from the built assemblies the tests already reference.
        foreach (Assembly assembly in AgentForgeAssemblies())
        {
            string name = assembly.GetName().Name!;
            foreach (string resource in assembly.GetManifestResourceNames())
            {
                if (!resource.EndsWith(".resources", StringComparison.Ordinal))
                {
                    Increment(counts, $"resource\t{name}\t{resource}");
                }
            }

            foreach (string site in ShapeConstructions(assembly))
            {
                Increment(counts, $"shape\t{name}\t{site}");
            }
        }

        return counts;
    }

    private static void Increment(Dictionary<string, int> counts, string key)
        => counts[key] = counts.GetValueOrDefault(key) + 1;

    private static string Normalize(string line)
        => Regex.Replace(line.Trim(), @"\s+", " ");

    private static IEnumerable<string> AgentForgeSources(string root)
    {
        string src = Path.Combine(root, "src");
        return Directory.GetDirectories(src, "AgentForge.*")
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(path =>
            {
                string[] parts = Path.GetRelativePath(src, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return !parts.Contains("bin", StringComparer.OrdinalIgnoreCase) && !parts.Contains("obj", StringComparer.OrdinalIgnoreCase);
            })
            .Order(StringComparer.Ordinal);
    }

    private static Assembly[] AgentForgeAssemblies()
    {
        Assembly[] assemblies =
        [
            .. Directory.GetFiles(AppContext.BaseDirectory, "AgentForge.*.dll")
                .Where(path => !Path.GetFileName(path).Contains(".Tests", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .Select(path => Assembly.Load(AssemblyName.GetAssemblyName(path))),
        ];

        // Premise: all five, or the resource and shape scans measured less than they claim.
        MessageAssert.Equal(5, assemblies.Length,
            "Expected the five AgentForge assemblies beside the test; found: "
            + string.Join(", ", assemblies.Select(a => a.GetName().Name)));
        return assemblies;
    }

    /// <summary>
    /// Every method, constructor and type initializer in <paramref name="assembly"/> that executes
    /// <c>newobj</c> on a <see cref="ScopeLadder"/> or <see cref="FootprintCatalog"/> constructor,
    /// named by declaring type and member. Compiler-generated types (lambdas, state machines) are
    /// included, because a construction inside a lambda is still one.
    /// </summary>
    private static IEnumerable<string> ShapeConstructions(Assembly assembly)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (Type type in assembly.GetTypes())
        {
            IEnumerable<MethodBase> members = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
            foreach (MethodBase member in members)
            {
                byte[]? il = member.GetMethodBody()?.GetILAsByteArray();
                if (il is null)
                {
                    continue;
                }

                foreach (int token in NewObjTokens(il))
                {
                    MethodBase? ctor = member.Module.ResolveMethod(
                        token,
                        type.IsGenericType ? type.GetGenericArguments() : null,
                        member.IsGenericMethod ? member.GetGenericArguments() : null);
                    if (ctor?.DeclaringType == typeof(ScopeLadder) || ctor?.DeclaringType == typeof(FootprintCatalog))
                    {
                        yield return $"{type.FullName}::{member.Name} new {ctor.DeclaringType!.Name}";
                    }
                }
            }
        }
    }

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(op => op.Value);

    /// <summary>The metadata tokens of every <c>newobj</c> in a method body, decoding each operand's size.</summary>
    private static IEnumerable<int> NewObjTokens(byte[] il)
    {
        int i = 0;
        while (i < il.Length)
        {
            short value = il[i] == 0xFE && i + 1 < il.Length ? (short)(0xFE00 | il[i + 1]) : il[i];
            i += il[i] == 0xFE ? 2 : 1;
            OpCode op = OpCodesByValue[value];
            if (op == OpCodes.Newobj)
            {
                yield return BitConverter.ToInt32(il, i);
            }

            i += op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, i)),
                _ => 4,
            };
        }
    }

    /// <summary>
    /// The source with every <c>//</c> and <c>/* */</c> comment removed and every string and char
    /// literal kept, so a commented-out Claude path does not count and a Claude path in a string
    /// does. Newlines inside block comments are kept so lines stay lines.
    /// </summary>
    internal static string StripComments(string source)
    {
        StringBuilder output = new(source.Length);
        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && next == '*')
            {
                i += 2;
                while (i < source.Length && !(source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/'))
                {
                    if (source[i] == '\n')
                    {
                        output.Append('\n');
                    }

                    i++;
                }

                i += 2;
                continue;
            }

            if (c == '"')
            {
                i = CopyString(source, i, output);
                continue;
            }

            if (c == '\'')
            {
                output.Append(c);
                i++;
                while (i < source.Length && source[i] != '\'' && source[i] != '\n')
                {
                    if (source[i] == '\\' && i + 1 < source.Length)
                    {
                        output.Append(source[i]);
                        i++;
                    }

                    output.Append(source[i]);
                    i++;
                }

                if (i < source.Length && source[i] == '\'')
                {
                    output.Append('\'');
                    i++;
                }

                continue;
            }

            output.Append(c);
            i++;
        }

        return output.ToString();
    }

    /// <summary>Copies one string literal starting at its opening quote; returns the index after it.</summary>
    private static int CopyString(string source, int start, StringBuilder output)
    {
        int quotes = 0;
        while (start + quotes < source.Length && source[start + quotes] == '"')
        {
            quotes++;
        }

        // A raw string literal: three or more quotes open it, the same run closes it.
        if (quotes >= 3)
        {
            string fence = new('"', quotes);
            int end = source.IndexOf(fence, start + quotes, StringComparison.Ordinal);
            end = end < 0 ? source.Length : end + quotes;
            output.Append(source, start, end - start);
            return end;
        }

        bool verbatim = false;
        for (int back = start - 1; back >= 0 && (source[back] == '@' || source[back] == '$'); back--)
        {
            verbatim |= source[back] == '@';
        }

        int i = start + 1;
        output.Append('"');
        while (i < source.Length)
        {
            char c = source[i];
            if (verbatim && c == '"' && i + 1 < source.Length && source[i + 1] == '"')
            {
                output.Append("\"\"");
                i += 2;
                continue;
            }

            if (!verbatim && c == '\\' && i + 1 < source.Length)
            {
                output.Append(c).Append(source[i + 1]);
                i += 2;
                continue;
            }

            output.Append(c);
            i++;
            if (c == '"' || (!verbatim && c == '\n'))
            {
                break;
            }
        }

        return i;
    }

    private static Dictionary<string, int> ReadAllowList()
    {
        string path = Path.Combine(RepoRoot(), AllowListRelative);
        Assert.True(File.Exists(path), $"{AllowListRelative} is missing.");
        Dictionary<string, int> allowed = new(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int tab = line.LastIndexOf('\t');
            allowed[line[..tab]] = int.Parse(line[(tab + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        }

        return allowed;
    }

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
}
