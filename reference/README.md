# Reference sources

This folder holds read-only checkouts of the upstream sources this repository builds against, each at
the version the build pins:

| Source | Pinned by | Checked out |
|---|---|---|
| Semi.Avalonia | `Semi.Avalonia` | `src/Semi.Avalonia` |

Semi.Avalonia ships its theme XAML compiled into its assembly, so the package holds nothing to search:
which resource key a control uses is in `Controls/<Control>.axaml`, and what that key is, per theme, in
`Themes/Light` and `Themes/Dark`.

They are for reading what the build compiles against: a control's behaviour, a theme's resources, the
tests that show how upstream drives a feature. A search reads the exact version, whole and offline,
instead of single files fetched through `gh api`. Nothing automated reads them: no test, build or CI
job.

Each checkout is a folder of its own, and git ignores every folder here, through `/reference/*/` in
`.gitignore`. The files beside them are tracked:

| File | What it is |
|---|---|
| `fetch-reference.cs` | The script that fetches the checkouts, a .NET 10 file-based app |
| `sources.json` | Its manifest, one entry for each source |
| `README.md` | This file |
| `AGENTS.md`, `CLAUDE.md` | The rules for agents |
| `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props` | The shields, described below |

## Fetching

From anywhere inside the repository:

```bash
dotnet run --file reference/fetch-reference.cs
```

The script reads `sources.json`, works out each source's pin, and fetches what is missing: shallow, at
the pin, and sparse where the entry lists folders. Git converts no line endings in a checkout, so it
holds the same bytes on every platform, and every text file in it is LF. The script prints
a line when a fetch starts, and one for each source as it finishes, with its state:

| State | Meaning |
|---|---|
| `present` | Checked out at its pin |
| `fetched` | Fetched by this run |
| `missing` | Not there, or a fetch never finished; a plain run fetches it |
| `off-pin` | Checked out at another pin, or its HEAD has moved since; `--update` fetches it again |
| `failed` | The fetch failed, for the reason the line gives, and what it had fetched is removed |

| Option | Effect |
|---|---|
| `--status` | Reports only, fetching nothing |
| `--update` | Also fetches again each source off its pin |
| `--quiet` | Prints only what changed and what is wrong |
| `<name> ...` | Takes only the sources named, in any case |

It exits 0 when every source asked for is at its pin; 1 when one is missing, off its pin, or failed to
fetch; and 2 on a usage error, or a problem with the manifest, a pin or `.gitignore`.

## Where a pin comes from

An entry that names `packages` is fetched at the commit their nuspecs record, the
`<repository commit="…">` every package carries, for the version the repository pins. That is the
source the package was built from, which a tag need not be: Semi.Avalonia's tag `v12.1.0.1` sits one
commit past the commit its 12.1.0.1 package records.

- **The version** comes from the `PackageVersion` items of `Directory.Packages.props`, or, in a
  repository without central package management, from the `PackageReference` and `PackageVersion`
  items of its tracked project, props and targets files. A `VersionOverride` counts as a pin too, and a
  `$(Name)` takes the literal value a property of that name is given in those files.
- **It is normalised as NuGet does.** A fourth part is dropped when it is zero, build metadata and
  leading zeros go, and an exact range `[1.2.3]` is read as `1.2.3`. A floating version or any other
  range pins nothing, and is refused.
- **The nuspec** is read from the global packages folder, or from nuget.org when no folder holds it.
- **The entry's packages agree**: one version, one repository, one commit. A `url` given beside them
  must be the repository they record.

When the nuspecs record no commit, the entry's `tag`, which holds `{version}`, is fetched instead. A
source no package pins names its `ref`: a tag, a branch, or a commit's full hash.

## The manifest

`sources.json` is JSON that may hold comments. Each entry has these fields:

| Field | Meaning |
|---|---|
| `name` | The source's name on the command line and in the report |
| `dir` | Where it is checked out, relative to the repository root: a folder under `reference/` |
| `packages` | The packages whose pinned version, and the commit their nuspecs record, make the pin |
| `url` | The repository to fetch from. Beside `packages` it is optional, and must be theirs |
| `tag` | Beside `packages`, a tag holding `{version}`, fetched only when the nuspecs record no commit |
| `ref` | For a source no package pins: a tag, a branch, or a commit's full hash. It needs a `url` |
| `sparse` | The folders to check out; without it, everything |
| `local` | A sibling checkout to fetch from when it exists, relative to the repository root. The pin still holds, and the `url` serves when the sibling lacks it |

An entry has `packages` or `ref`, never both, and a field the script does not know is refused, so a
misspelt one cannot pass unnoticed.

## The shields

MSBuild imports the first `Directory.Build.props`, `Directory.Build.targets` and
`Directory.Packages.props` it finds walking up from a project. A checkout that ships none of its own
would inherit this repository's: its packages, its analyzers, warnings as errors, and central package
management, which fails every `PackageReference` that has a `Version` with NU1008, as it did in
DiffView's CI. The shields here
end the walk: the first two hold nothing but a comment, and the third turns central package management
off and pins nothing.

The script builds under them too, so it builds alike in every repository that keeps it, with the
settings its `#:property` lines state, warnings as errors among them. It writes any shield that is
missing.

## Rules

- **A checkout is read-only.** A change an upstream needs is a pull request to that upstream, never an
  edit here. `git -C reference/<name> status` shows whether anything in a checkout was changed.
- **A search reaches a checkout by its path**, as in `rg -n ListBoxItemSelectedBackground reference/Semi.Avalonia/src`.
  Ripgrep, and the tools built on it, skip what `.gitignore` excludes, so a search of the repository
  leaves the checkouts out, as it should unless the task is about one.
- **A raised package leaves its checkout `off-pin`** until
  `dotnet run --file reference/fetch-reference.cs -- --update` fetches it again.
