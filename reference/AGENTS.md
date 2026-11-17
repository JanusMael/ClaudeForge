# AGENTS.md — `reference/`

Read-only checkouts of the upstream sources this repository builds against, one folder each, at the
versions the build pins. Git ignores the folders and tracks the files beside them: the script that
fetches them, its manifest `sources.json`, three MSBuild shields and these documents.
[`README.md`](README.md) is the whole account: fetching, the states, the manifest, and where each pin
comes from.

| Rule | Why |
|---|---|
| Never edit a file in a checkout | It is the upstream as the build compiles it. A change an upstream needs is a pull request there |
| Search a checkout by passing its path, as in `rg -n ListBoxItemSelectedBackground reference/Semi.Avalonia/src` | Git ignores the checkouts, so a search of the repository skips them, and it should keep skipping them unless the task is about one |
| Run `dotnet run --file reference/fetch-reference.cs -- --status` before trusting a checkout | A checkout that is missing, or off its pin since a package was raised, is the wrong version to read; a plain run fetches what is missing, and `--update` what is off its pin |
| Keep `fetch-reference.cs`'s build settings in its `#:property` lines | It builds under the shields, never the repository's props, so it builds alike wherever it is kept |
