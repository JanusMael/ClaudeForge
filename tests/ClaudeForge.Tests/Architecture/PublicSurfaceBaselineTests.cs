using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Pins the public API of every packable project against a checked-in baseline, so a change to
/// what the eleven NuGet packages expose is a deliberate act with a reviewable diff.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>Nothing pinned this, and the gap was measured rather than supposed.</b> On 2026-09-16 a
/// breaking signature change to <c>IShareService</c> — a type in the packable
/// <c>LayeredEditors.Avalonia.Services</c> — went through a full green suite of 4,367 tests with
/// nothing noticing. <c>PublicSurfaceContractTests</c> looks like the guard and is not: it lives
/// in <c>AgentForge.Sdk.Tests</c>, covers one assembly, and enforces house style (no defaulted
/// <c>CancellationToken</c>, no <c>JsonNode</c> leak, no Avalonia reference) rather than API shape.
/// </para>
/// <para>
/// ⚠ <b>The window for this closes at the first publish.</b> A published package version is
/// immutable and the CalVer is day-resolution, so an accidental break shipped to the feed cannot
/// be replaced — recovery is the next calendar day, and consumers have the broken version
/// meanwhile. Nothing is on the feed yet, which is the only reason a baseline can be established
/// from the current surface rather than reconstructed from release history.
/// </para>
/// <para>
/// ⭐ <b>The assembly list is DERIVED, never written down here.</b> It comes from
/// <c>&lt;IsPackable&gt;true&lt;/IsPackable&gt;</c> in the csproj files — the same source of truth
/// <c>PackageMetadataTests</c> uses. A hardcoded list would be a twelfth place to update, and the
/// failure would be silence: a newly packable library with no baseline, publishing an unguarded
/// surface while this test stayed green.
/// </para>
/// <para>
/// ⓘ <b>Known blind spot: nullable annotations.</b> Reference nullability lives in attributes this
/// renderer does not read, so turning <c>string</c> into <c>string?</c> is invisible here. That is
/// a source-compatibility change, not a binary one, and closing it would mean decoding
/// <c>NullableAttribute</c>'s byte encoding — worth knowing about, not worth pretending is covered.
/// </para>
/// </remarks>
[TestClass]
public sealed class PublicSurfaceBaselineTests
{
    /// <summary>Where the baselines live, relative to the repo root.</summary>
    private static readonly string[] BaselineDir = ["tests", "ClaudeForge.Tests", "Architecture", "PublicSurface"];

    [TestMethod]
    public void EveryPackableAssembly_MatchesItsCheckedInSurfaceBaseline()
    {
        string repoRoot = FindRepoRoot();
        IReadOnlyList<string> packable = PackableAssemblyNames(repoRoot);

        // ⚠ Premise before claim. An empty list, or one whose assemblies are not beside the test,
        // would make every assertion below vacuously true — the shape of guard that reports green
        // for years while measuring nothing.
        Assert.IsTrue(packable.Count > 0,
            "No project under src/ declares <IsPackable>true</IsPackable>, so this guard has "
            + "nothing to compare and would pass no matter what the packages expose.");

        string baselineDir = Path.Combine([repoRoot, .. BaselineDir]);
        Directory.CreateDirectory(baselineDir);

        List<string> problems = [];

        foreach (string name in packable)
        {
            string dll = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            if (!File.Exists(dll))
            {
                problems.Add(
                    $"[{name}] is packable but '{name}.dll' is not beside the test assembly, so "
                    + "its surface cannot be read. Add a ProjectReference from ClaudeForge.Tests, "
                    + "or this package publishes unguarded.");
                continue;
            }

            string actual = RenderSurface(Assembly.LoadFrom(dll));
            string baselinePath = Path.Combine(baselineDir, name + ".txt");

            if (!File.Exists(baselinePath))
            {
                WriteActual(baselinePath, actual);
                problems.Add(
                    $"[{name}] has no surface baseline. One has been written next to it as "
                    + $"'{name}.txt.actual' — read it, and if the surface is what this package "
                    + $"should expose, rename it to '{name}.txt' and commit it in this change.");
                continue;
            }

            string expected = Normalise(File.ReadAllText(baselinePath));
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                WriteActual(baselinePath, actual);
                problems.Add($"[{name}] public surface changed:{Environment.NewLine}"
                             + Diff(expected, actual)
                             + $"{Environment.NewLine}  Full output: '{name}.txt.actual'.");
            }
        }

        if (problems.Count > 0)
        {
            Assert.Fail(
                "The public surface of one or more PACKAGED libraries differs from its baseline."
                + Environment.NewLine + Environment.NewLine
                + string.Join(Environment.NewLine + Environment.NewLine, problems)
                + Environment.NewLine + Environment.NewLine
                + "⚠ If the change is deliberate, update the baseline IN THE SAME COMMIT so the "
                + "diff is reviewable — and say so in the message, because consumers of the "
                + "published package pay for it and a published version can never be replaced.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Discovery
    // ─────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> PackableAssemblyNames(string repoRoot)
    {
        List<string> names = [];

        foreach (string csproj in Directory
                     .EnumerateFiles(Path.Combine(repoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            string? packable = XDocument.Load(csproj).Descendants()
                .Where(e => e.Name.LocalName == "IsPackable")
                .Select(e => e.Value.Trim())
                .FirstOrDefault();

            if (string.Equals(packable, "true", StringComparison.OrdinalIgnoreCase))
            {
                // ⚠ The assembly name is the file name here, and AssemblyName is never overridden
                // in this repo — PackageMetadataTests reads the same shape.
                names.Add(Path.GetFileNameWithoutExtension(csproj));
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Rendering — deterministic, or the baseline churns and stops being read
    // ─────────────────────────────────────────────────────────────────────────

    private static string RenderSurface(Assembly assembly)
    {
        List<string> lines = [];

        foreach (Type type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            lines.Add(RenderTypeHeader(type));

            List<string> members = [];

            foreach (MemberInfo member in type.GetMembers(
                         BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                string? rendered = RenderMember(member);
                if (rendered is not null)
                {
                    members.Add("    " + rendered);
                }
            }

            members.Sort(StringComparer.Ordinal);
            lines.AddRange(members);
        }

        return string.Join("\n", lines) + "\n";
    }

    private static string RenderTypeHeader(Type type)
    {
        string kind = type.IsEnum ? "enum"
            : type.IsInterface ? "interface"
            : type.IsValueType ? "struct"
            : "class";

        StringBuilder sb = new();
        sb.Append(kind).Append(' ').Append(TypeName(type));

        if (type.IsSealed && !type.IsValueType && !type.IsEnum && !type.IsInterface && !type.IsAbstract)
        {
            sb.Append(" [sealed]");
        }

        if (type.IsAbstract && !type.IsInterface && !(type.IsSealed && type.IsAbstract))
        {
            sb.Append(" [abstract]");
        }

        if (type.IsEnum)
        {
            sb.Append(" : ").Append(TypeName(type.GetEnumUnderlyingType()));
        }
        else if (type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(ValueType))
        {
            sb.Append(" : ").Append(TypeName(baseType));
        }

        // ⓘ Enums are skipped here on purpose: every enum implements IComparable, IConvertible,
        // IFormattable and ISpanFormattable, so the list is four identical lines of noise per
        // enum that can never change. A baseline nobody reads is a baseline nobody updates.
        string[] interfaces = type.IsEnum
            ? []
            : type.GetInterfaces()
                .Where(i => i.IsPublic || i.IsNestedPublic)
                .Select(TypeName)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

        if (interfaces.Length > 0)
        {
            sb.Append(" implements ").Append(string.Join(", ", interfaces));
        }

        return sb.ToString();
    }

    private static string? RenderMember(MemberInfo member)
    {
        switch (member)
        {
            // An enum's `value__` backing field is emitted by the compiler for every enum and
            // says nothing about the contract.
            case FieldInfo { Name: "value__" }:
                return null;

            case FieldInfo field when IsVisible(field.IsPublic, field.IsFamily, field.IsFamilyOrAssembly):
                // Enum members render as their value, which is what a consumer's switch depends on
                // and what a renumbering would silently change.
                return field.DeclaringType!.IsEnum && !field.IsSpecialName
                    ? $"{field.Name} = {Convert.ToInt64(field.GetRawConstantValue(), CultureInfo.InvariantCulture)}"
                    : $"{Modifiers(field.IsStatic, isConst: field.IsLiteral)}{TypeName(field.FieldType)} {field.Name}";

            case PropertyInfo property:
            {
                MethodInfo? getter = property.GetGetMethod(nonPublic: true);
                MethodInfo? setter = property.GetSetMethod(nonPublic: true);
                MethodInfo? any = getter ?? setter;
                if (any is null || !IsVisible(any.IsPublic, any.IsFamily, any.IsFamilyOrAssembly))
                {
                    return null;
                }

                string accessors = string.Concat(
                    getter is not null && IsVisible(getter.IsPublic, getter.IsFamily, getter.IsFamilyOrAssembly) ? "get;" : "",
                    setter is null || !IsVisible(setter.IsPublic, setter.IsFamily, setter.IsFamilyOrAssembly)
                        ? ""
                        : IsInitOnly(setter) ? "init;" : "set;");

                return $"{Modifiers(any.IsStatic)}{TypeName(property.PropertyType)} {property.Name} "
                       + "{ " + accessors + " }";
            }

            case EventInfo evt when evt.GetAddMethod(nonPublic: true) is { } add
                                    && IsVisible(add.IsPublic, add.IsFamily, add.IsFamilyOrAssembly):
                return $"{Modifiers(add.IsStatic)}event {TypeName(evt.EventHandlerType!)} {evt.Name}";

            case ConstructorInfo ctor when IsVisible(ctor.IsPublic, ctor.IsFamily, ctor.IsFamilyOrAssembly):
                return $".ctor({Parameters(ctor.GetParameters())})";

            case MethodInfo method when IsVisible(method.IsPublic, method.IsFamily, method.IsFamilyOrAssembly):
            {
                // Property and event accessors are already rendered with their member.
                if (method.IsSpecialName &&
                    (method.Name.StartsWith("get_", StringComparison.Ordinal)
                     || method.Name.StartsWith("set_", StringComparison.Ordinal)
                     || method.Name.StartsWith("add_", StringComparison.Ordinal)
                     || method.Name.StartsWith("remove_", StringComparison.Ordinal)))
                {
                    return null;
                }

                string generics = method.IsGenericMethodDefinition
                    ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">"
                    : "";

                return $"{Modifiers(method.IsStatic)}{TypeName(method.ReturnType)} {method.Name}{generics}"
                       + $"({Parameters(method.GetParameters())})";
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Public and protected both count. ⚠ <c>internal</c> deliberately does not, even though this
    /// solution grants itself friend access everywhere — a package's consumers cannot see it.
    /// </summary>
    private static bool IsVisible(bool isPublic, bool isFamily, bool isFamilyOrAssembly)
        => isPublic || isFamily || isFamilyOrAssembly;

    private static bool IsInitOnly(MethodInfo setter)
        => setter.ReturnParameter.GetRequiredCustomModifiers()
            .Any(m => string.Equals(m.FullName, "System.Runtime.CompilerServices.IsExternalInit", StringComparison.Ordinal));

    private static string Modifiers(bool isStatic, bool isConst = false)
        => (isConst ? "const " : "") + (isStatic && !isConst ? "static " : "");

    private static string Parameters(ParameterInfo[] parameters)
        => string.Join(", ", parameters.Select(p =>
        {
            string prefix = p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "";
            string name = TypeName(p.ParameterType);

            // ⚠ Defaults are rendered because REMOVING one breaks every caller that omitted the
            // argument, and CHANGING one silently changes their behaviour — the same shape of
            // defect as an inverted default, which this repo has shipped before.
            string dflt = p.HasDefaultValue ? " = " + Literal(p.RawDefaultValue) : "";
            return $"{prefix}{name} {p.Name}{dflt}";
        }));

    private static string Literal(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?",
    };

    private static string TypeName(Type type)
    {
        if (type.IsByRef)
        {
            return TypeName(type.GetElementType()!);
        }

        if (type.IsArray)
        {
            return TypeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (type.IsGenericType)
        {
            string bare = type.GetGenericTypeDefinition().FullName ?? type.Name;
            int tick = bare.IndexOf('`', StringComparison.Ordinal);
            if (tick >= 0)
            {
                bare = bare[..tick];
            }

            return bare + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";
        }

        return type.FullName ?? type.Name;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reporting
    // ─────────────────────────────────────────────────────────────────────────

    private static void WriteActual(string baselinePath, string actual)
    {
        // ⚠ Written beside the baseline, never OVER it. A test that repairs its own baseline is a
        // test that can never fail twice — it would report the break once and then bless it.
        File.WriteAllText(baselinePath + ".actual", actual);
    }

    private static string Diff(string expected, string actual)
    {
        HashSet<string> before = new(Normalise(expected).Split('\n'), StringComparer.Ordinal);
        HashSet<string> after = new(Normalise(actual).Split('\n'), StringComparer.Ordinal);

        IEnumerable<string> removed = before.Except(after, StringComparer.Ordinal)
            .Where(l => l.Length > 0).OrderBy(l => l, StringComparer.Ordinal).Select(l => "  - " + l.Trim());
        IEnumerable<string> added = after.Except(before, StringComparer.Ordinal)
            .Where(l => l.Length > 0).OrderBy(l => l, StringComparer.Ordinal).Select(l => "  + " + l.Trim());

        // Removals first: they are the breaking half, and a long diff gets read from the top.
        return string.Join(Environment.NewLine, removed.Concat(added).Take(40));
    }

    /// <summary>
    /// ⚠ <c>.gitattributes</c> sets <c>* text=auto</c>, so a Windows checkout reads CRLF while the
    /// renderer emits LF. Comparing raw text would fail on every machine but the one that wrote
    /// the baseline — the same line-ending trap the schema refresh script already documents.
    /// </summary>
    private static string Normalise(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string FindRepoRoot()
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
