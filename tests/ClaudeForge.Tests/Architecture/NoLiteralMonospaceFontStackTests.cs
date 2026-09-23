using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Headless;
using Avalonia.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// A monospace font is a design token, exactly as a colour is, and naming one inline is the same
/// mistake as writing a hex literal into a view.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>Six spellings across 67 markup sites, and a seventh in C#</b>, is the state this guard
/// was written to end. Four differed in substance rather than whitespace: two led with
/// <c>Cascadia Mono</c> and so drew a DIFFERENT TYPEFACE from their neighbours on the same screen
/// under Windows; two fell back to <c>Courier New</c> and one to bare <c>monospace</c>, which on
/// macOS means Courier rather than Menlo. Nothing failed, because nothing was watching.
/// </para>
/// <para>
/// ⭐ <b>The font is BUNDLED, which is what makes the rule enforceable.</b> JetBrains Mono NL
/// ships inside the <c>Bennewitz.Ninja.ScopedEditors.AvaloniaUI</c> package (it was
/// <c>LayeredEditors.Avalonia</c> until plans/00005), so there is no platform stack left to argue
/// about — a literal stack is now always wrong, not merely inconsistent.
/// </para>
/// <para>
/// ⛔ <b>TWO DECLARATION FORMS, and checking only one is how the first sweep missed a site.</b> A
/// font is set either as an attribute on the element or as a style <c>Setter</c>. An
/// attribute-only sweep silently left <c>MarkdownBodyView</c>'s code-block Setter behind — the one
/// surface whose entire job is showing literal bytes. It was found by reconciling the replacement
/// count against a prediction, not by the sweep reporting a problem.
/// </para>
/// <para>
/// ⚠ <b>Prose is exempt, and must be.</b> The comment in <c>App.axaml</c> that explains this rule
/// necessarily quotes the old stacks. So the scan looks for a font stack in a POSITION that
/// renders — an attribute value or a Setter value — never for the word anywhere in the file. The
/// same trap as a text search matching the comment above a flag rather than the flag.
/// </para>
/// </remarks>
[TestClass]
public sealed class NoLiteralMonospaceFontStackTests
{
    /// <summary>The key every rendering site must bind instead of naming a face.</summary>
    private const string Token = "AppMonoFontFamily";

    /// <summary>
    /// A font family set as an attribute (<c>FontFamily="…"</c>) or as a style
    /// <c>&lt;Setter Property="FontFamily" Value="…"/&gt;</c>. Both render; both must use the token.
    /// </summary>
    private static readonly Regex FontFamilyDeclaration = new(
        """(?:FontFamily="(?<v>[^"]*)")|(?:<Setter\s+Property="FontFamily"\s+Value="(?<v>[^"]*)")""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [TestMethod]
    public void NoAxamlNamesAMonospaceFaceInline()
    {
        List<string> offenders = [];

        foreach (string path in EnumerateAxaml())
        {
            string text = File.ReadAllText(path);

            foreach (Match match in FontFamilyDeclaration.Matches(text))
            {
                string value = match.Groups["v"].Value;

                // A DynamicResource/StaticResource binding is the whole point of the rule.
                if (value.Contains("Resource", StringComparison.Ordinal)) { continue; }

                // The token's own definition names the bundled font by URI; that is the one
                // place allowed to, and it is a FontFamily ELEMENT rather than an attribute,
                // so it does not match here anyway. Guarded explicitly in case that changes.
                if (value.StartsWith("avares://", StringComparison.Ordinal)) { continue; }

                if (LooksMonospace(value))
                {
                    offenders.Add($"{RepoRelative(path)}: FontFamily=\"{value}\"");
                }
            }
        }

        Assert.AreEqual(
            0,
            offenders.Count,
            $"{offenders.Count} markup site(s) name a monospace face inline instead of binding " +
            $"{{DynamicResource {Token}}}. The font is bundled, so a literal stack is not a " +
            "portability hedge — it is a second source of truth.\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>
    /// ⭐ The token is worthless if nothing binds it, and an assertion that only counts
    /// offenders passes just as happily when every site has been deleted. This pins the
    /// positive side.
    /// </summary>
    [TestMethod]
    public void TheMonospaceTokenIsActuallyBoundByMarkup()
    {
        int sites = EnumerateAxaml()
            .Where(p => !string.Equals(Path.GetFileName(p), "App.axaml", StringComparison.Ordinal))
            .Sum(p => Regex.Matches(File.ReadAllText(p), Regex.Escape(Token)).Count);

        Assert.IsTrue(
            sites >= 60,
            $"Only {sites} markup site(s) bind {Token}. There were 67 when the token was " +
            "introduced; a collapse means the sweep was reverted or the key was renamed in " +
            "markup without this guard being updated.");
    }

    /// <summary>
    /// Every <c>avares://</c> font URI names a folder that really holds font files.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔⛔ <b>A dangling font URI does not throw — Avalonia falls back to the default face and
    /// says nothing.</b> So a typo, a moved folder, or a build where the font never got embedded
    /// all present as "the monospace surfaces look a bit off", on every platform, with a green
    /// suite. That is strictly worse than the literal stacks this class replaced, because at
    /// least those named a real font.
    /// </para>
    /// <para>
    /// ⚠ <b>This is the half the compiler cannot cover.</b> The glyph font is reached through
    /// <c>x:Static</c>, so a missing member is a build error — loud, and it duly failed a
    /// package-mode build. The font URI is a STRING; nothing checks it at all.
    /// </para>
    /// <para>
    /// ⚠ <b>Since plans/00005 the fonts ship in a PACKAGE, so there is no folder to look in.</b> A URI
    /// naming an assembly built in this tree is still checked on disk; any other is resolved the way
    /// the running app resolves it — Avalonia's <c>AssetLoader</c> over the consumed assembly's
    /// embedded resources — so a renamed package assembly or a moved folder fails here rather than
    /// at first layout.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task EveryBundledFontUriPointsAtRealFontFiles()
    {
        Regex fontUri = new(
            @"avares://(?<asm>[^/]+)/(?<path>[^#""<]+)#",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        List<string> offenders = [];
        int checkedUris = 0;

        IEnumerable<string> sources = EnumerateAxaml()
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
                .Where(p =>
                    !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                    !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)));

        foreach (string path in sources)
        {
            foreach (Match m in fontUri.Matches(File.ReadAllText(path)))
            {
                checkedUris++;

                string assembly = m.Groups["asm"].Value;
                string folder = m.Groups["path"].Value.Trim('/');

                if (Directory.Exists(Path.Combine(RepoRoot(), "src", assembly)))
                {
                    string onDisk = Path.Combine(RepoRoot(), "src", assembly, folder.Replace('/', Path.DirectorySeparatorChar));

                    if (!Directory.Exists(onDisk) ||
                        Directory.GetFiles(onDisk, "*.ttf").Length == 0)
                    {
                        offenders.Add($"{RepoRelative(path)}: avares://{assembly}/{folder}# -> no .ttf at {onDisk}");
                    }

                    continue;
                }

                int packaged = await PackagedFontCountAsync(assembly, folder);
                if (packaged == 0)
                {
                    offenders.Add($"{RepoRelative(path)}: avares://{assembly}/{folder}# -> no .ttf embedded in the consumed {assembly} assembly");
                }
            }
        }

        Assert.AreNotEqual(
            0,
            checkedUris,
            "No avares:// font URI was found anywhere. Either the bundled font was removed, or " +
            "this pattern stopped matching how they are written — both make this test vacuous.");

        Assert.AreEqual(
            0,
            offenders.Count,
            $"{offenders.Count} font URI(s) name a location with no font files. Avalonia resolves " +
            "these silently, so this would ship as a wrong typeface rather than as an error.\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>
    /// How many <c>.ttf</c> assets the consumed <paramref name="assembly"/> embeds under
    /// <paramref name="folder"/>, asked of Avalonia's own loader. 0 when the assembly cannot be
    /// loaded, because that is exactly the state in which the app would draw the fallback face.
    /// Runs on the headless session: <c>AssetLoader</c> needs a platform to resolve through.
    /// </summary>
    private static Task<int> PackagedFontCountAsync(string assembly, string folder) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly()).Dispatch(() =>
        {
            try
            {
                return AssetLoader.GetAssets(new Uri($"avares://{assembly}/{folder}/"), null)
                    .Count(a => a.AbsolutePath.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase));
            }
            catch (FileNotFoundException)
            {
                return 0;
            }
        }, CancellationToken.None);

    /// <summary>
    /// Matches a font stack that is asking for a fixed-pitch face. Deliberately broad: the point
    /// is to catch a NEW literal nobody thought about, not to enumerate the six already removed.
    /// </summary>
    private static bool LooksMonospace(string value)
    {
        string[] needles =
        [
            "monospace", "Consolas", "Menlo", "Courier", "Cascadia",
            "JetBrains Mono", "DejaVu Sans Mono", "Monaco", "Fira Mono", "Fira Code",
        ];

        return needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateAxaml()
    {
        string src = Path.Combine(RepoRoot(), "src");

        return Directory
            .EnumerateFiles(src, "*.axaml", SearchOption.AllDirectories)
            .Where(p =>
                !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string RepoRelative(string path) =>
        path[(RepoRoot().Length + 1)..].Replace('\\', '/');

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClaudeForge.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.IsNotNull(dir, "Could not locate the repository root from the test output folder.");
        return dir.FullName;
    }
}
