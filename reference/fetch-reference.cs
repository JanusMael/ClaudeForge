#!/usr/bin/env dotnet
#:property TreatWarningsAsErrors=true
#:property Nullable=enable
#:property ImplicitUsings=enable

// Read-only checkouts of the upstream sources a repository builds against: fetches what
// reference/sources.json lists into folders under reference/, each at its pin, so a search reads the
// very version the build uses, whole, instead of single files fetched through `gh api`. It merges
// DiffView's and XamlQuality's scripts of the same name, and holds nothing of any one repository: what a
// repository fetches is its manifest's.
//
// ⭐ IT BUILDS ALIKE IN EVERY REPOSITORY. It lives beside the shields, so MSBuild walking up from it
// finds their empty Directory.Build.props and never the repository's: the directives above are the
// settings it builds with, warnings as errors among them, whatever analysers a repository adds.
//
// ⭐ READ FROM THE PINS. An entry that names packages is fetched at the commit their nuspecs record for
// the version the repository pins: in the PackageVersion items of Directory.Packages.props, or without
// one, in the PackageReference and PackageVersion items of the tracked project, props and targets files.
// A VersionOverride counts as a pin in either, and a $(Name) takes the literal value a property of that
// name is given in those files. The version is normalised as NuGet does, an exact range [1.2.3] read as
// 1.2.3, and any other range refused, since it pins nothing. The nuspec is read from the global
// packages folder, or from nuget.org's flat container when no folder holds it. The packages must be
// pinned alike and record one repository and one commit, so the checkout cannot drift from what the
// build compiles against, and a "url" given beside them must be the one they record. Where the nuspec
// records no commit, the entry's "tag", holding {version}, is fetched instead. A source no package pins
// names its "ref": a tag, a branch, or a commit's full hash.
//
// ⭐ "local" IS A FASTER SOURCE, NEVER ANOTHER PIN. It names a sibling checkout, relative to the
// repository root, to clone from when it exists. The checkout under reference/ is still made at the
// pin, and when the sibling cannot supply it the url does, which the report line says.
//
// ⛔ LF, SHALLOW, SPARSE, BOUNDED. Every git call takes core.autocrlf=false, core.eol=lf and
// core.longpaths=true, and every checkout keeps them in its own config, so a checkout holds the same
// bytes on every platform and Windows takes its paths past 260 characters. A checkout is shallow at
// its pin and sparse to the folders its entry lists. git never prompts, so a repository that wants
// credentials fails instead of waiting, and a git call still running after 15 minutes is killed.
//
// ⛔ ONLY FOLDERS UNDER reference/, AND ONLY ONES GIT IGNORES. Every "dir" is a folder under reference/,
// and nothing is fetched until git confirms it ignores each of them: without /reference/*/ in
// .gitignore the script stops. The files beside the checkouts are tracked: this script, sources.json,
// the README and three shields, Directory.Build.props, Directory.Build.targets and
// Directory.Packages.props, which MSBuild walking up from a checkout that ships none of its own finds
// before the repository's. Inheriting its central package management made every versioned
// PackageReference NU1008 in DiffView's CI. The script writes any shield that is missing, to be
// committed.
//
// Each checkout records in .fetched-ref, written last, the commit it was made at, and the tag or branch
// when that was its pin. One off its pin, or whose HEAD has moved from that commit, is replaced whole
// only with --update; a folder an interrupted fetch left is replaced by any run that fetches.
//
// Usage, from anywhere inside the repository:
//   dotnet run --file reference/fetch-reference.cs                 fetch each source that is missing
//   dotnet run --file reference/fetch-reference.cs -- --update     also fetch again each source off its pin
//   dotnet run --file reference/fetch-reference.cs -- --status     report only, fetching nothing
//   dotnet run --file reference/fetch-reference.cs -- --quiet      print only what changed and what is wrong
//   dotnet run --file reference/fetch-reference.cs -- <name> ...   only the sources named, in any case
//
// Exit codes: 0 every source asked for is at its pin; 1 one is missing, off its pin, or failed to fetch;
// 2 a usage error, or a manifest, pin or ignore problem.

using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

if (args.Any(argument => argument is "-h" or "--help"))
{
    Console.WriteLine(Cli.Help);
    return 0;
}

bool update = false, statusOnly = false, quiet = false;
List<string> only = [];
foreach (string argument in args)
{
    switch (argument)
    {
        case "--update":
            update = true;
            break;
        case "--status":
            statusOnly = true;
            break;
        case "--quiet":
            quiet = true;
            break;
        case var option when option.StartsWith('-'):
            return Cli.Usage($"unknown option '{option}'.");
        default:
            only.Add(argument);
            break;
    }
}

if (update && statusOnly)
{
    return Cli.Usage("--update fetches and --status does not: pass one of them.");
}

string root;
Source[] sources;
try
{
    root = Repository.Root();
    sources = Manifest.Read(root);
}
catch (Exception error) when (error is InvalidDataException or InvalidOperationException or TimeoutException or IOException or UnauthorizedAccessException or JsonException or XmlException)
{
    return Cli.Fail(error.Message);
}

string[] unknown = [.. only.Where(name => !sources.Any(source => source.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))];
if (unknown.Length > 0)
{
    return Cli.Usage($"the manifest lists no source called {string.Join(", ", unknown)}.");
}

// No checkout is fetched until git ignores every folder the manifest names. Each is asked about through
// its pin record, a path inside it: git judges a path's leading folders as folders, so /reference/*/
// matches one that does not exist yet, which it would not match by its own name.
string[] unignored;
try
{
    unignored = Repository.NotIgnored(root, [.. sources.Select(source => (source.Folder, $"{source.Folder}/{Checkout.PinFile}"))]);
}
catch (Exception error) when (error is InvalidOperationException or TimeoutException)
{
    return Cli.Fail(error.Message);
}

if (unignored.Length > 0)
{
    return Cli.Fail($"git does not ignore {string.Join(", ", unignored)}: add /reference/*/ to .gitignore, so no checkout can be committed.");
}

Source[] chosen = [.. sources.Where(source => only.Count == 0 || only.Contains(source.Name, StringComparer.OrdinalIgnoreCase))];
Nuspecs nuspecs = new(root);
List<(Source Source, Pin Pin, State State, string? Why)> plan = [];
try
{
    foreach (Source source in chosen)
    {
        Pin pin = source.Resolve(nuspecs);
        (State state, string? why) = Checkout.Inspect(root, source, pin);
        plan.Add((source, pin, state, why));
    }

    Shields.Write(root);
}
catch (Exception error) when (error is InvalidDataException or InvalidOperationException or TimeoutException or IOException or UnauthorizedAccessException)
{
    return Cli.Fail(error.Message);
}

int width = plan.Count == 0 ? 0 : plan.Max(item => item.Source.Name.Length);
int failures = 0;
foreach ((Source source, Pin pin, State state, string? why) in plan)
{
    string at = $"{pin.Label} in {source.Folder}";
    if (state == State.Present)
    {
        Report(source.Name, "present", at + pin.Note);
        continue;
    }

    if (statusOnly || (state == State.OffPin && !update))
    {
        failures++;
        Report(source.Name, state == State.OffPin ? "off-pin" : "missing", state switch
        {
            State.OffPin => $"{why}: run with --update",
            State.Unfinished => $"{source.Folder} holds an unfinished fetch, and the pin is {pin.Label}: run {Cli.Command}",
            _ => $"{pin.Label}: run {Cli.Command}",
        });
        continue;
    }

    string folder = source.PathUnder(root);
    Console.WriteLine($"fetching {source.Name} {pin.Label} from {Checkout.Origin(root, source, pin)} ...");
    try
    {
        if (Directory.Exists(folder))
        {
            Checkout.Delete(folder);
        }

        string via = Checkout.Fetch(root, source, pin);
        Report(source.Name, "fetched", at + via + pin.Note);
    }
    catch (Exception error) when (error is InvalidOperationException or TimeoutException or IOException or UnauthorizedAccessException)
    {
        failures++;
        Report(source.Name, "failed", error.Message);
        Checkout.TryDelete(folder);
    }
}

return failures == 0 ? 0 : 1;

void Report(string name, string state, string detail)
{
    if (!(quiet && state == "present"))
    {
        Console.WriteLine($"{name.PadRight(width)}  {state,-8} {detail}");
    }
}

/// <summary>What a checkout under reference/ holds, against its pin.</summary>
enum State
{
    /// <summary>Fetched at its pin, and its HEAD still there.</summary>
    Present,

    /// <summary>Not there at all.</summary>
    Missing,

    /// <summary>A folder with no pin record: a fetch that never finished, or a folder made by hand.</summary>
    Unfinished,

    /// <summary>Fetched at another pin, or its HEAD moved since.</summary>
    OffPin,
}

/// <summary>The command line's words.</summary>
static class Cli
{
    public const string Command = "dotnet run --file reference/fetch-reference.cs";

    public const string Help = """
        fetch-reference: fetches the read-only upstream checkouts reference/sources.json lists into
        folders under reference/, each at its pin, from anywhere inside the repository.

          dotnet run --file reference/fetch-reference.cs                 fetch each source that is missing
          dotnet run --file reference/fetch-reference.cs -- --update     also fetch again each source off its pin
          dotnet run --file reference/fetch-reference.cs -- --status     report only, fetching nothing
          dotnet run --file reference/fetch-reference.cs -- --quiet      print only what changed and what is wrong
          dotnet run --file reference/fetch-reference.cs -- <name> ...   only the sources named, in any case

        Exit codes: 0 every source asked for is at its pin; 1 one is missing, off its pin, or failed to
        fetch; 2 a usage error, or a manifest, pin or ignore problem. reference/README.md says the rest.
        """;

    public static int Usage(string problem)
    {
        Console.Error.WriteLine("fetch-reference: " + problem);
        Console.Error.WriteLine($"usage: {Command} -- [--update | --status] [--quiet] [<name> ...]");
        return 2;
    }

    public static int Fail(string problem)
    {
        Console.Error.WriteLine("fetch-reference: " + problem);
        return 2;
    }
}

/// <summary>One source the manifest lists.</summary>
/// <param name="Version">The version the repository pins for <paramref name="Packages"/>, normalised, when it names any.</param>
sealed record Source(string Name, string Folder, string? Url, string? Local, string[] Sparse, string[] Packages, string? Version, string? Tag, string? Ref)
{
    /// <summary>The checkout's folder under <paramref name="root"/>.</summary>
    public string PathUnder(string root) => Path.Combine([root, .. Folder.Split('/')]);

    /// <summary>What to fetch: the ref, or the commit the packages' nuspecs record, or the tag their version makes.</summary>
    public Pin Resolve(Nuspecs nuspecs)
    {
        if (Ref is { } reference)
        {
            bool commit = Pin.IsCommitHash(reference);
            string pinned = commit ? reference.ToLowerInvariant() : reference;
            return new Pin(Url!, pinned, commit, Pin.Shorten(pinned), "");
        }

        string version = Version!;
        (string Package, Nuspec Spec)[] specs = [.. Packages.Select(package => (package, nuspecs.Read(package, version)))];
        Nuspec first = specs[0].Spec;
        if (specs.Any(item => !Urls.Same(item.Spec.Url, first.Url) || !string.Equals(item.Spec.Commit, first.Commit, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"{Name}: its packages record different sources, and one checkout cannot match them all: " +
                string.Join("; ", specs.Select(item => $"{item.Package} {version} records {item.Spec.Describe()}")) + ".");
        }

        if (Url is not null && first.Url is not null && !Urls.Same(Url, first.Url))
        {
            throw new InvalidDataException($"{Name}'s url, {Url}, is not the repository its packages record, {first.Url}.");
        }

        string url = Url ?? first.Url?.TrimEnd('/')
            ?? throw new InvalidDataException($"{Name}: {Packages[0]} {version} records no repository url, so the entry needs a \"url\".");
        string note = specs.Any(item => item.Spec.FromNuGetOrg) ? ", its nuspec read from nuget.org" : "";
        if (first.Commit is { } recorded)
        {
            string commit = recorded.ToLowerInvariant();
            return new Pin(url, commit, IsCommit: true, $"{version} @ {Pin.Shorten(commit)}", note);
        }

        if (Tag is { } tag)
        {
            string made = tag.Replace("{version}", version, StringComparison.Ordinal);
            return new Pin(url, made, IsCommit: false, $"{version} @ {made}", note);
        }

        throw new InvalidDataException($"{Name}: {Packages[0]} {version} records no commit, and the entry has no \"tag\" to fall back on.");
    }
}

/// <summary>What a checkout is fetched at, and how the report names it.</summary>
/// <param name="Ref">The commit, tag or branch fetched.</param>
/// <param name="Note">What the report adds about where the pin was read, or nothing.</param>
sealed record Pin(string Url, string Ref, bool IsCommit, string Label, string Note)
{
    /// <summary>A commit's full hash, SHA-1 or SHA-256: the only form git fetches by hash.</summary>
    public static bool IsCommitHash(string text) => text.Length is 40 or 64 && text.All(char.IsAsciiHexDigit);

    /// <summary>A commit's first seven digits, or a tag or branch as it is.</summary>
    public static string Shorten(string reference) => reference.Length == 0 ? "an empty pin record" : IsCommitHash(reference) ? reference[..7] : reference;
}

/// <summary>Reads and checks reference/sources.json.</summary>
/// <remarks>
/// ⚠ Read through JsonDocument, not the serializer: a file-based app is analysed for native AOT, which
/// rejects reflection-based deserialization, and warnings are errors here.
/// </remarks>
static class Manifest
{
    public const string Location = "reference/sources.json";

    private static readonly string[] Properties = ["name", "dir", "url", "packages", "tag", "ref", "local", "sparse"];

    public static Source[] Read(string root)
    {
        string path = Path.Combine(root, "reference", "sources.json");
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"{Location} is missing, and it lists what to fetch.");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement top = document.RootElement;
        if (top.ValueKind != JsonValueKind.Object || !top.TryGetProperty("sources", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{Location} holds no \"sources\" array.");
        }

        if (top.EnumerateObject().Select(property => property.Name).FirstOrDefault(name => name != "sources") is { } stray)
        {
            throw new InvalidDataException($"{Location} has a property \"{stray}\", and holds only \"sources\".");
        }

        Pins? pins = null;
        List<Source> sources = [];
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            string label = $"source {sources.Count + 1}";
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{Location}: {label} is not an object.");
            }

            string name = Text(entry, "name", label) ?? throw new InvalidDataException($"{Location}: {label} has no name.");
            if (entry.EnumerateObject().Select(property => property.Name).FirstOrDefault(property => !Properties.Contains(property)) is { } unknown)
            {
                throw new InvalidDataException($"{name} has a property \"{unknown}\", which is none of {string.Join(", ", Properties)}.");
            }

            string folder = (Text(entry, "dir", name) ?? throw new InvalidDataException($"{name} has no dir.")).Replace('\\', '/');
            if (!folder.StartsWith("reference/", StringComparison.Ordinal) || folder.Split('/').Any(part => part is "" or "." or ".."))
            {
                throw new InvalidDataException($"{name}'s dir, {folder}, is not a folder under reference/, the one folder this script writes.");
            }

            string[] sparse = [.. Texts(entry, "sparse", name).Select(part => part.Replace('\\', '/').Trim('/'))];
            if (sparse.Any(part => part.Split('/').Any(segment => segment is "" or "." or "..")))
            {
                throw new InvalidDataException($"{name}'s sparse lists a folder that is not one inside the checkout: {string.Join(", ", sparse)}.");
            }

            string? url = Text(entry, "url", name);
            string? local = Text(entry, "local", name);
            string? tag = Text(entry, "tag", name);
            string? reference = Text(entry, "ref", name);
            string[] packages = Texts(entry, "packages", name);
            string? version = null;
            if (packages.Length > 0 == (reference is not null))
            {
                throw new InvalidDataException($"{name} needs one pin: the packages whose pinned version makes it, or a ref.");
            }

            if (reference is not null)
            {
                if (url is null)
                {
                    throw new InvalidDataException($"{name} pins a ref and names no url to fetch it from.");
                }

                if (tag is not null)
                {
                    throw new InvalidDataException($"{name} pins a ref and a tag; a tag is made from its packages' version, so it goes with packages.");
                }
            }
            else
            {
                if (tag is not null && !tag.Contains("{version}", StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"{name}'s tag, {tag}, holds no {{version}}, so it would not follow the pin.");
                }

                Pins pinned = pins ??= Pins.Read(root);
                string[] versions = [.. packages.Select(package => pinned.Of(package, name)).Distinct(StringComparer.OrdinalIgnoreCase)];
                if (versions.Length > 1)
                {
                    throw new InvalidDataException(
                        $"{name}: {string.Join(" and ", packages)} are pinned at {string.Join(" and ", versions)}, and one checkout cannot match them all.");
                }

                version = versions[0];
            }

            if (sources.Any(other => other.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"{name} is listed twice, and a name picks one source.");
            }

            if (sources.FirstOrDefault(other => Nested(other.Folder, folder) || Nested(folder, other.Folder)) is { } overlap)
            {
                throw new InvalidDataException($"{name}'s dir, {folder}, overlaps {overlap.Name}'s, {overlap.Folder}, and a fetch of one would delete the other.");
            }

            sources.Add(new Source(name, folder, url, local, sparse, packages, version, tag, reference));
        }

        return [.. sources];
    }

    private static bool Nested(string outer, string inner) =>
        inner.Equals(outer, StringComparison.OrdinalIgnoreCase) || inner.StartsWith(outer + "/", StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonElement entry, string property, string owner)
    {
        if (!entry.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : throw new InvalidDataException($"{owner}'s {property} is not a string with something in it.");
    }

    private static string[] Texts(JsonElement entry, string property, string owner)
    {
        if (!entry.TryGetProperty(property, out JsonElement values))
        {
            return [];
        }

        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() == 0
            || values.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())))
        {
            throw new InvalidDataException($"{owner}'s {property} is not a list of strings with something in each; leave it out to name nothing.");
        }

        return [.. values.EnumerateArray().Select(value => value.GetString()!.Trim())];
    }
}

/// <summary>The versions the repository pins for its packages, each with the file that pins it.</summary>
/// <remarks>
/// With central package management, the PackageVersion items of the root Directory.Packages.props;
/// without it, the PackageReference and PackageVersion items' Version in the tracked project, props and
/// targets files, which git lists, so no ignored or generated tree is ever read. A VersionOverride counts
/// as a pin in either. A $(Name) takes the literal value a property of that name is given in those files.
/// </remarks>
sealed class Pins
{
    private const string Central = "Directory.Packages.props";

    private readonly bool _central;
    private readonly List<(string Package, string File, string Where, string Text)> _found = [];
    private readonly Dictionary<string, List<(string File, string Value)>> _properties = new(StringComparer.OrdinalIgnoreCase);

    private Pins(bool central) => _central = central;

    public static Pins Read(string root)
    {
        Pins pins = new(File.Exists(Path.Combine(root, Central)));
        string[] files = Repository.Tracked(root, ["*.csproj", "*.props", "*.targets"]);
        if (pins._central && !files.Contains(Central, StringComparer.OrdinalIgnoreCase))
        {
            files = [Central, .. files];
        }

        foreach (string file in files)
        {
            XDocument document;
            try
            {
                document = XDocument.Load(Path.Combine(root, file));
            }
            catch (XmlException error)
            {
                throw new InvalidDataException($"{file} is no MSBuild file this script can read: {error.Message}");
            }

            foreach (XElement property in document.Descendants().Where(element => element.Parent?.Name.LocalName == "PropertyGroup" && !element.HasElements))
            {
                if (!pins._properties.TryGetValue(property.Name.LocalName, out List<(string File, string Value)>? values))
                {
                    pins._properties[property.Name.LocalName] = values = [];
                }

                values.Add((file, property.Value.Trim()));
            }

            bool central = pins._central && file.Equals(Central, StringComparison.OrdinalIgnoreCase);
            foreach (XElement item in document.Descendants().Where(element => element.Name.LocalName is "PackageReference" or "PackageVersion"))
            {
                string kind = item.Name.LocalName;
                string[] attributes = !pins._central ? ["Version", "VersionOverride"] : central && kind == "PackageVersion" ? ["Version"] : ["VersionOverride"];
                string ids = item.Attribute("Include")?.Value ?? item.Attribute("Update")?.Value ?? "";
                foreach (string attribute in attributes)
                {
                    // Metadata is an attribute, or a child element of the same name.
                    string? text = item.Attribute(attribute)?.Value ?? item.Elements().FirstOrDefault(element => element.Name.LocalName == attribute)?.Value;
                    foreach (string id in text is null ? [] : ids.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        pins._found.Add((id, file, $"{file}: {kind} {id}'s {attribute}", text!.Trim()));
                    }
                }
            }
        }

        return pins;
    }

    /// <summary>The version every pin of <paramref name="package"/> agrees on, normalised.</summary>
    /// <param name="owner">The source that asks, for a refusal.</param>
    public string Of(string package, string owner)
    {
        (string File, string Version)[] versions =
        [
            .. _found.Where(found => found.Package.Equals(package, StringComparison.OrdinalIgnoreCase))
                .Select(found => (found.File, Versions.Normalize(Expand(found.Text, found.Where), found.Where))),
        ];
        if (versions.Length == 0)
        {
            throw new InvalidDataException(_central
                ? $"{owner}: {Central} pins no version of {package}."
                : $"{owner}: no tracked project, props or targets file pins a version of {package}.");
        }

        if (versions.Select(pin => pin.Version).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            throw new InvalidDataException(
                $"{owner}: {package} is pinned at more than one version, and one checkout cannot match them all: " +
                string.Join(", ", versions.Select(pin => $"{pin.Version} in {pin.File}")) + ".");
        }

        return versions[0].Version;
    }

    /// <summary>The text with each $(Name) replaced by the one literal value the files give that property.</summary>
    private string Expand(string text, string where)
    {
        StringBuilder expanded = new();
        int at = 0;
        for (int start = text.IndexOf("$(", StringComparison.Ordinal); start >= 0; start = text.IndexOf("$(", at, StringComparison.Ordinal))
        {
            int end = text.IndexOf(')', start);
            if (end < 0)
            {
                break;
            }

            string name = text[(start + 2)..end];
            List<(string File, string Value)> given = _properties.TryGetValue(name, out List<(string File, string Value)>? values) ? values : [];
            string[] literal = [.. given.Select(value => value.Value).Distinct(StringComparer.Ordinal)];
            if (given.Count == 0)
            {
                throw new InvalidDataException($"{where} is '{text}', and no tracked file gives {name} a value.");
            }

            if (literal.Length != 1 || !Literal(literal[0]))
            {
                throw new InvalidDataException(
                    $"{where} is '{text}', and {name} is not given one literal value: {string.Join(", ", given.Select(value => $"'{value.Value}' in {value.File}"))}.");
            }

            expanded.Append(text, at, start - at).Append(literal[0]);
            at = end + 1;
        }

        string result = expanded.Append(text, at, text.Length - at).ToString();
        return Literal(result) ? result : throw new InvalidDataException($"{where} is '{text}', which this script cannot evaluate.");
    }

    private static bool Literal(string value) =>
        value.Length > 0 && !value.Contains("$(", StringComparison.Ordinal) && !value.Contains("@(", StringComparison.Ordinal) && !value.Contains("%(", StringComparison.Ordinal);
}

/// <summary>A version as NuGet normalises it, which names its folders and its flat container's paths.</summary>
static class Versions
{
    /// <summary>
    /// Three numbers at least, a fourth kept only when it is not zero, no leading zeros, a pre-release
    /// label kept and build metadata dropped; an exact range [1.2.3] is its version. A floating version
    /// or any other range pins nothing, and is refused, as is anything that is no version.
    /// </summary>
    /// <param name="where">Where the version is written, for the refusal.</param>
    public static string Normalize(string text, string where)
    {
        string version = text.Trim();
        if (version.StartsWith('[') && version.EndsWith(']') && !version.Contains(','))
        {
            version = version[1..^1].Trim();
        }

        if (version.Contains('*') || version.IndexOfAny(['[', ']', '(', ')', ',']) >= 0)
        {
            throw new InvalidDataException($"{where} is '{text}', a floating version or a range, which pins nothing.");
        }

        string core = version.Split('+')[0];
        string release = "";
        int dash = core.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            release = core[dash..];
            core = core[..dash];
        }

        string[] parts = core.Split('.');
        if (parts.Length > 4 || parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit))
            || (release.Length > 0 && (release.Length == 1 || !release[1..].All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))))
        {
            throw new InvalidDataException($"{where} is '{text}', which is no version.");
        }

        List<string> numbers = [.. parts.Select(part => part.TrimStart('0') is { Length: > 0 } digits ? digits : "0")];
        while (numbers.Count < 3)
        {
            numbers.Add("0");
        }

        if (numbers.Count == 4 && numbers[3] == "0")
        {
            numbers.RemoveAt(3);
        }

        return string.Join('.', numbers) + release;
    }
}

/// <summary>Repository URLs compared as one: case, a trailing slash and a trailing .git aside.</summary>
static class Urls
{
    public static bool Same(string? one, string? other) =>
        one is null || other is null ? one is null && other is null : Plain(one).Equals(Plain(other), StringComparison.OrdinalIgnoreCase);

    private static string Plain(string url)
    {
        string plain = url.Trim().TrimEnd('/');
        return (plain.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? plain[..^4] : plain).TrimEnd('/');
    }
}

/// <summary>The repository and commit a package's nuspec records.</summary>
sealed record Nuspec(string? Url, string? Commit, bool FromNuGetOrg)
{
    public string Describe() => $"{Url ?? "no repository url"} at {Commit ?? "no commit"}";

    public static Nuspec Parse(string text, string where, bool fromNuGetOrg)
    {
        XElement? repository;
        try
        {
            repository = XDocument.Parse(text).Descendants().FirstOrDefault(element => element.Name.LocalName == "repository");
        }
        catch (XmlException error)
        {
            throw new InvalidDataException($"{where} is not a nuspec this script can read: {error.Message}");
        }

        string? url = repository?.Attribute("url")?.Value.Trim() is { Length: > 0 } recordedUrl ? recordedUrl : null;
        string? commit = repository?.Attribute("commit")?.Value.Trim() is { Length: > 0 } recordedCommit ? recordedCommit : null;
        if (commit is not null && !Pin.IsCommitHash(commit))
        {
            throw new InvalidDataException($"{where} records the commit '{commit}', which is no commit's full hash.");
        }

        return new Nuspec(url, commit, fromNuGetOrg);
    }
}

/// <summary>Finds a package's nuspec: in the global packages folder, else on nuget.org.</summary>
sealed class Nuspecs(string root)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(1) };

    private bool _asked;
    private string? _configured;

    /// <param name="version">The version normalised, as every folder and URL names it.</param>
    public Nuspec Read(string id, string version)
    {
        string lowerId = id.ToLowerInvariant();
        string lowerVersion = version.ToLowerInvariant();
        foreach (string folder in Folders())
        {
            string path = Path.Combine(folder, lowerId, lowerVersion, lowerId + ".nuspec");
            if (File.Exists(path))
            {
                return Nuspec.Parse(File.ReadAllText(path), path, fromNuGetOrg: false);
            }
        }

        string url = $"https://api.nuget.org/v3-flatcontainer/{lowerId}/{lowerVersion}/{lowerId}.nuspec";
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            using HttpResponseMessage response = Http.Send(request);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new InvalidDataException($"{id} {version} is in no global packages folder, and nuget.org has no such package.");
            }

            response.EnsureSuccessStatusCode();
            using StreamReader reader = new(response.Content.ReadAsStream());
            return Nuspec.Parse(reader.ReadToEnd(), url, fromNuGetOrg: true);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new InvalidDataException($"{id} {version} is in no global packages folder, and nuget.org could not be read: {error.Message}");
        }
    }

    /// <summary>
    /// NUGET_PACKAGES when it is set, and nothing else, as NuGet takes it. Otherwise the default folder,
    /// then the one `dotnet nuget locals` reports, asked only when the default lacks a nuspec: the call
    /// costs a second, and a nuspec is the same in every folder that holds its version.
    /// </summary>
    private IEnumerable<string> Folders()
    {
        if (Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } variable)
        {
            yield return variable;
            yield break;
        }

        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        yield return fallback;
        if (!_asked)
        {
            _asked = true;
            _configured = Configured();
        }

        if (_configured is not null && !Same(_configured, fallback))
        {
            yield return _configured;
        }
    }

    private static bool Same(string one, string other) =>
        Path.GetFullPath(one).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(other).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    /// <summary>The global packages folder NuGet's configuration names for this repository, or null.</summary>
    private string? Configured()
    {
        string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host) ? host : "dotnet";
        try
        {
            (int exit, string output, _) = Tool.Run(dotnet, root, ["nuget", "locals", "global-packages", "--list"], TimeSpan.FromMinutes(1), []);
            const string Key = "global-packages:";
            return exit != 0 ? null : output.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith(Key, StringComparison.OrdinalIgnoreCase))
                .Select(line => line[Key.Length..].Trim())
                .FirstOrDefault(folder => folder.Length > 0);
        }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException)
        {
            return null;
        }
    }
}

/// <summary>The checkouts themselves: what each holds, and how one is fetched and removed.</summary>
static class Checkout
{
    /// <summary>
    /// The file in a checkout, written last, that records the commit it was made at, then the tag or
    /// branch on a second line when that was its pin.
    /// </summary>
    public const string PinFile = ".fetched-ref";

    public static (State State, string? Why) Inspect(string root, Source source, Pin pin)
    {
        string folder = source.PathUnder(root);
        if (!Directory.Exists(folder))
        {
            return (State.Missing, null);
        }

        string record = Path.Combine(folder, PinFile);
        if (!Directory.Exists(Path.Combine(folder, ".git")) || !File.Exists(record))
        {
            return (State.Unfinished, null);
        }

        string[] lines = [.. File.ReadAllLines(record).Select(line => line.Trim()).Where(line => line.Length > 0)];
        string made = lines.FirstOrDefault() ?? "";
        string fetched = lines.Length > 1 ? lines[1] : made;
        if (fetched != pin.Ref)
        {
            return (State.OffPin, $"{source.Folder} holds {Pin.Shorten(fetched)}, and the pin is {pin.Label}");
        }

        (int exit, string head, _) = Git.Run(folder, ["rev-parse", "HEAD"]);
        head = head.Trim();
        return exit == 0 && head == made
            ? (State.Present, null)
            : (State.OffPin, $"{source.Folder} was fetched at {Pin.Shorten(made)}, and its HEAD has moved to {(exit == 0 ? Pin.Shorten(head) : "nothing git can read")}");
    }

    /// <summary>Where a fetch of <paramref name="source"/> starts: its sibling checkout when it has one, else its url.</summary>
    public static string Origin(string root, Source source, Pin pin) => Sibling(root, source) is null ? pin.Url : source.Local!;

    /// <summary>Fetches the source at its pin, returning what the report line adds about where from.</summary>
    public static string Fetch(string root, Source source, Pin pin)
    {
        string folder = source.PathUnder(root);
        if (Sibling(root, source) is { } sibling)
        {
            try
            {
                Clone(root, folder, pin, sibling, source.Sparse);
                return $", from {source.Local}";
            }
            catch (Exception error) when (error is InvalidOperationException or TimeoutException)
            {
                if (Directory.Exists(folder))
                {
                    Delete(folder);
                }

                Clone(root, folder, pin, pin.Url, source.Sparse);
                return $", from {pin.Url}, as {source.Local} could not supply it: {error.Message}";
            }
        }

        Clone(root, folder, pin, pin.Url, source.Sparse);
        return "";
    }

    /// <summary>
    /// A tag or branch is cloned by name. A commit cannot be, so the checkout is made empty, and exactly
    /// that commit is fetched into it and checked out detached. Either way the pin record comes last.
    /// </summary>
    private static void Clone(string root, string folder, Pin pin, string url, string[] sparse)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
        if (pin.IsCommit)
        {
            Git.Check(root, ["init", "--quiet", folder]);
            foreach (string setting in Git.Settings)
            {
                string[] pair = setting.Split('=', 2);
                Git.Check(folder, ["config", pair[0], pair[1]]);
            }

            Git.Check(folder, ["remote", "add", "origin", url]);
            if (sparse.Length > 0)
            {
                // Before the fetch, so the checkout fetches the blobs of these folders alone.
                Git.Check(folder, ["sparse-checkout", "init", "--cone"]);
                Git.Check(folder, ["sparse-checkout", "set", .. sparse]);
            }

            Git.Check(folder, ["fetch", "--quiet", "--depth", "1", .. (sparse.Length > 0 ? (string[])["--filter=blob:none"] : []), "origin", pin.Ref]);
            Git.Check(folder, ["checkout", "--quiet", "--detach", "FETCH_HEAD"]);
        }
        else
        {
            List<string> arguments = ["clone", "--quiet", "--depth", "1", "--branch", pin.Ref];
            foreach (string setting in Git.Settings)
            {
                arguments.AddRange(["--config", setting]);
            }

            if (sparse.Length > 0)
            {
                arguments.AddRange(["--filter=blob:none", "--sparse"]);
            }

            arguments.AddRange([url, folder]);
            Git.Check(root, arguments);
            if (sparse.Length > 0)
            {
                Git.Check(folder, ["sparse-checkout", "set", .. sparse]);
            }
        }

        (int exit, string head, string errors) = Git.Run(folder, ["rev-parse", "HEAD"]);
        head = head.Trim();
        if (exit != 0 || !Pin.IsCommitHash(head))
        {
            throw new InvalidOperationException($"git rev-parse HEAD failed in the new checkout with exit {exit}: {errors.Trim()}");
        }

        if (pin.IsCommit && head != pin.Ref)
        {
            throw new InvalidOperationException($"the checkout is at {head}, and the pin is {pin.Ref}.");
        }

        // The checkout's own status stays clean, so it shows at once whether anything was changed in it.
        string info = Path.Combine(folder, ".git", "info");
        Directory.CreateDirectory(info);
        File.AppendAllText(Path.Combine(info, "exclude"), "\n/" + PinFile + "\n");
        File.WriteAllText(Path.Combine(folder, PinFile), pin.IsCommit ? head + "\n" : head + "\n" + pin.Ref + "\n");
    }

    /// <summary>The sibling checkout as a file:// URL, when the source names one and it exists.</summary>
    private static string? Sibling(string root, Source source)
    {
        if (source.Local is null)
        {
            return null;
        }

        string path = Path.GetFullPath(Path.Combine(root, source.Local));
        return Directory.Exists(path) ? "file://" + (path.StartsWith('/') ? "" : "/") + path.Replace('\\', '/') : null;
    }

    public static void Delete(string folder)
    {
        // git's object files are read-only, and Windows refuses to delete a read-only file. The hidden ones
        // count too: git hides .git on Windows, and an enumeration skips hidden entries by default.
        EnumerationOptions everything = new() { RecurseSubdirectories = true, AttributesToSkip = 0 };
        foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles("*", everything))
        {
            if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                file.Attributes &= ~FileAttributes.ReadOnly;
            }
        }

        Directory.Delete(folder, recursive: true);
    }

    public static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Delete(folder);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A partial fetch that will not go has no pin record, so the next run that fetches replaces it.
        }
    }
}

/// <summary>
/// The files beside the checkouts that stop MSBuild walking up from one, or from this script, into the
/// repository's own. They are committed; this writes any that is missing, with the same text.
/// </summary>
static class Shields
{
    private static readonly (string Name, string Text)[] Files =
    [
        ("Directory.Build.props", """
            <Project>
              <!-- A shield, committed, and written again by fetch-reference.cs beside it when it is missing.

                   The folders beside it are read-only upstream checkouts. MSBuild imports the first
                   Directory.Build.props it finds walking up from a project, so a checkout that ships none of
                   its own, and the script, would import the repository's, with its packages, its analyzers
                   and warnings as errors. They find this empty one first, and import nothing: the script
                   states its own settings at its top. -->
            </Project>
            """),
        ("Directory.Build.targets", """
            <Project>
              <!-- A shield, committed, and written again by fetch-reference.cs beside it when it is missing.

                   The folders beside it are read-only upstream checkouts. MSBuild imports the first
                   Directory.Build.targets it finds walking up from a project, so a checkout that ships none
                   of its own, and the script, would import the repository's, and the items and targets it
                   adds. They find this empty one first, and import nothing. -->
            </Project>
            """),
        ("Directory.Packages.props", """
            <Project>
              <!-- A shield, committed, and written again by fetch-reference.cs beside it when it is missing.

                   The folders beside it are read-only upstream checkouts. NuGet imports the first
                   Directory.Packages.props it finds walking up from a project, so a checkout that ships none
                   of its own would inherit the repository's central package management, and every
                   PackageReference with a Version would fail with NU1008, as DiffView's CI did. The flag set
                   false here hands each checkout back to its own rules. It pins nothing. -->
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """),
    ];

    public static void Write(string root)
    {
        string folder = Path.Combine(root, "reference");
        Directory.CreateDirectory(folder);
        foreach ((string name, string text) in Files.Where(file => !File.Exists(Path.Combine(folder, file.Name))))
        {
            File.WriteAllText(Path.Combine(folder, name), text.ReplaceLineEndings("\n") + "\n");
        }
    }
}

/// <summary>The repository this script lives in, as git sees it.</summary>
static class Repository
{
    /// <summary>
    /// The work tree holding this script, so a run from anywhere finds it, even from inside a checkout
    /// under reference/; failing that, the one holding the current directory.
    /// </summary>
    public static string Root()
    {
        string?[] starts = [AppContext.GetData("EntryPointFileDirectoryPath") as string, Directory.GetCurrentDirectory()];
        foreach (string start in starts.OfType<string>().Where(Directory.Exists))
        {
            (int exit, string output, _) = Git.Run(start, ["rev-parse", "--show-toplevel"]);
            if (exit == 0 && output.Trim().Length > 0)
            {
                return Path.GetFullPath(output.Trim());
            }
        }

        throw new InvalidOperationException("found no git work tree holding this script or the current directory.");
    }

    /// <summary>The tracked files matching <paramref name="patterns"/>, relative to the root, that are on disk.</summary>
    public static string[] Tracked(string root, IReadOnlyList<string> patterns)
    {
        (int exit, string output, string errors) = Git.Run(root, ["ls-files", "-z", "--", .. patterns]);
        if (exit != 0)
        {
            throw new InvalidOperationException($"git ls-files failed with exit {exit}: {errors.Trim()}");
        }

        return [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(path => File.Exists(Path.Combine(root, path)))];
    }

    /// <summary>Each path shown whose probe, relative to the root, git does not ignore. A tracked path is never ignored.</summary>
    public static string[] NotIgnored(string root, IReadOnlyList<(string Shown, string Probe)> paths)
    {
        if (paths.Count == 0)
        {
            return [];
        }

        (int exit, string output, string errors) = Git.Run(root, ["check-ignore", "--verbose", "--non-matching", "-z", "--stdin"],
            string.Concat(paths.Select(path => path.Probe + "\0")));
        if (exit is not (0 or 1))
        {
            throw new InvalidOperationException($"git check-ignore failed with exit {exit}: {errors.Trim()}");
        }

        // -z prints four fields for each path: source, line, pattern, path. A path no pattern ignores has
        // an empty pattern, and one a negated pattern matches is not ignored either.
        string[] fields = output.Split('\0');
        HashSet<string> ignored = new(StringComparer.Ordinal);
        for (int i = 0; i + 3 < fields.Length; i += 4)
        {
            if (fields[i + 2].Length > 0 && !fields[i + 2].StartsWith('!'))
            {
                ignored.Add(fields[i + 3].Replace('\\', '/'));
            }
        }

        return [.. paths.Where(path => !ignored.Contains(path.Probe)).Select(path => path.Shown)];
    }
}

/// <summary>git, run with the settings every checkout here takes, never prompting, and bounded.</summary>
static class Git
{
    /// <summary>LF on every platform, and paths past 260 characters on Windows.</summary>
    public static readonly string[] Settings = ["core.autocrlf=false", "core.eol=lf", "core.longpaths=true"];

    /// <summary>How long one git call may run before it is killed: a slow clone, not a hung one.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(15);

    /// <summary>
    /// GIT_TERMINAL_PROMPT stops git asking on the terminal; GCM_INTERACTIVE stops Git Credential
    /// Manager opening a window instead, as it does for a repository GitHub answers with 401.
    /// </summary>
    private static readonly (string Name, string Value)[] Quiet = [("GIT_TERMINAL_PROMPT", "0"), ("GCM_INTERACTIVE", "never")];

    public static (int Exit, string Output, string Errors) Run(string directory, IReadOnlyList<string> arguments, string input = "") =>
        Tool.Run("git", directory, [.. Settings.SelectMany(setting => (string[])["-c", setting]), .. arguments], Deadline, Quiet, input);

    public static void Check(string directory, IReadOnlyList<string> arguments)
    {
        (int exit, string output, string errors) = Run(directory, arguments);
        if (exit != 0)
        {
            string said = string.Join(" ", (errors + "\n" + output).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed with exit {exit}: {said}");
        }
    }
}

/// <summary>Runs a program to its end or its deadline, both streams captured and its input given, then closed.</summary>
static class Tool
{
    public static (int Exit, string Output, string Errors) Run(
        string program, string directory, IReadOnlyList<string> arguments, TimeSpan deadline, IReadOnlyList<(string Name, string Value)> environment, string input = "")
    {
        UTF8Encoding utf8 = new(encoderShouldEmitUTF8Identifier: false);
        ProcessStartInfo start = new(program)
        {
            WorkingDirectory = directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach ((string name, string value) in environment)
        {
            start.Environment[name] = value;
        }

        Process? started;
        try
        {
            started = Process.Start(start);
        }
        catch (Win32Exception error)
        {
            throw new InvalidOperationException($"{program} could not be started; is it installed and on PATH? {error.Message}");
        }

        using Process process = started ?? throw new InvalidOperationException($"{program} could not be started.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        try
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // It ended without reading its input; its exit code says why.
        }

        if (!process.WaitForExit(deadline))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception or AggregateException)
            {
                // It ended on its own between the deadline and the kill, or part of its tree had.
            }

            throw new TimeoutException($"timed out: {Path.GetFileNameWithoutExtension(program)} {string.Join(' ', arguments)} ran past {deadline.TotalMinutes:0} minutes and was killed.");
        }

        // A process it started and left running, such as a daemon, can hold the streams open past its
        // exit. The exit code is the answer, so its words are waited for briefly and no longer.
        _ = Task.WaitAll([output, errors], TimeSpan.FromSeconds(30));
        return (process.ExitCode, output.IsCompletedSuccessfully ? output.Result : "", errors.IsCompletedSuccessfully ? errors.Result : "");
    }
}
