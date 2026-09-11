namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// The named directory roots a product's footprint categories are expressed against.
/// </summary>
/// <remarks>
/// <para>
/// Claude supplies one (<c>"home"</c> = <c>~/.claude</c>). OpenCode supplies four — <c>config</c>,
/// <c>data</c>, <c>state</c>, <c>cache</c> — because its footprint genuinely spans all of them and
/// the largest item on disk is under a different root from the only irreplaceable one.
/// </para>
/// <para>
/// ⛔ <b>Resolve these per use, never capture them.</b> The paths derive from
/// <c>PlatformPaths.UserProfile</c>, which honours an <c>AsyncLocal</c> test override, and a
/// footprint service is cached for the lifetime of a client. This type is cheap to build, so
/// <see cref="FootprintService"/> takes a factory rather than an instance.
/// </para>
/// </remarks>
public sealed class FootprintRoots
{
    /// <summary>The root key Claude's single-root footprint uses.</summary>
    public const string Home = "home";

    private readonly IReadOnlyDictionary<string, string> _roots;

    /// <param name="roots">
    /// Key → absolute path. Keys are compared ordinally and case-sensitively: they are data
    /// written in a catalog beside the code that reads them, not user input.
    /// </param>
    public FootprintRoots(IReadOnlyDictionary<string, string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        _roots = roots;
    }

    /// <summary>A single-root set, the shape Claude uses.</summary>
    public static FootprintRoots Single(string key, string absolutePath) =>
        new(new Dictionary<string, string>(StringComparer.Ordinal) { [key] = absolutePath });

    /// <summary>Every key this set supplies, for diagnostics and guard tests.</summary>
    public IReadOnlyCollection<string> Keys => (IReadOnlyCollection<string>)_roots.Keys;

    /// <summary>
    /// The absolute path for <paramref name="key"/>, or <see langword="null"/> when this product
    /// does not supply that root.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Null rather than throw, deliberately.</b> A catalog that names a root the product
    /// lacks is a bug, but surfacing it as an empty category keeps the rest of the page working
    /// while <c>FootprintCatalogTests</c> reddens on the real cause. A throw here would take out
    /// every category because <see cref="FootprintService.GetStatsAsync"/> walks them in one pass.
    /// </remarks>
    public string? TryResolve(string key) => _roots.TryGetValue(key, out string? path) ? path : null;
}
